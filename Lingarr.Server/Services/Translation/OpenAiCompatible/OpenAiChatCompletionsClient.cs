using System.Text.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Integrations.Translation;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Non-streaming OpenAI Chat Completions over HTTP for one provider identity.
/// Handles request-body construction, thinking/max-token injection, transient error
/// classification and response parsing. Retry and fallback policy stay with the caller
/// (the translation service), which owns the configured retry settings.
/// </summary>
public sealed class OpenAiChatCompletionsClient : IOpenAiCompatibleCompletionClient
{
    private readonly HttpClient _httpClient;
    private readonly IRequestTemplateService _requestTemplateService;
    private readonly ILogger _logger;
    private readonly string _serviceLabel;
    private readonly string? _sessionId;

    /// <param name="httpClient">
    /// Shared client. The caller owns the per-request timeout through the cancellation token; this
    /// class never mutates the client, so one instance can serve several providers.
    /// </param>
    /// <param name="requestTemplateService">Builds the JSON body from the provider template.</param>
    /// <param name="logger">Logs request failures without leaking API keys.</param>
    /// <param name="serviceLabel">Human-readable provider name used in errors and logs.</param>
    /// <param name="sessionId">Stable session id for OpenCode Go routing; generated per request when null.</param>
    public OpenAiChatCompletionsClient(
        HttpClient httpClient,
        IRequestTemplateService requestTemplateService,
        ILogger logger,
        string serviceLabel,
        string? sessionId = null)
    {
        _httpClient = httpClient;
        _requestTemplateService = requestTemplateService;
        _logger = logger;
        _serviceLabel = serviceLabel;
        _sessionId = sessionId;
    }

    /// <summary>
    /// Sends one chat completion and returns the assistant message content.
    /// Empty choices and transport errors surface as <see cref="TranslationResponseException"/>;
    /// non-transient HTTP failures surface as <see cref="TranslationException"/>.
    /// </summary>
    public async Task<string> CompleteAsync(
        string endpoint,
        string? apiKey,
        string requestTemplate,
        Dictionary<string, string> replacements,
        AiThinkingMode thinking,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var bodyJson = _requestTemplateService.BuildRequestBody(requestTemplate, replacements);
        NonStreamingPolicy.EnsureRequestIsNotStreaming(bodyJson, _serviceLabel);
        using var response = await PostChatRequestAsync(endpoint, apiKey, bodyJson, thinking, maxTokens, cancellationToken);
        return await ReadChatContentAsync(endpoint, response, cancellationToken);
    }

    /// <summary>
    /// Translates a batch in a single request. Tries structured output first and falls
    /// back to plain JSON extraction because not every OpenAI-compatible gateway supports
    /// <c>response_format</c>. Unparsable responses raise <see cref="TranslationParseException"/>.
    /// </summary>
    public async Task<Dictionary<int, string>> CompleteBatchAsync(
        string endpoint,
        string? apiKey,
        string requestTemplate,
        Dictionary<string, string> replacements,
        AiThinkingMode thinking,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // Reject a streaming template before either attempt. A configuration error must not fall
        // through into the JSON fallback, which would send the same streaming request again.
        var bodyJson = _requestTemplateService.BuildRequestBody(requestTemplate, replacements);
        NonStreamingPolicy.EnsureRequestIsNotStreaming(bodyJson, _serviceLabel);

        try
        {
            return await CompleteBatchWithStructuredOutputAsync(
                endpoint, apiKey, requestTemplate, replacements, thinking, maxTokens, cancellationToken);
        }
        // Fall back to plain JSON only for failures that a different request shape can fix. Cancellation
        // must stay cancellation, credentials must stay credentials (the chain skips the provider), a
        // rate limit must not be re-sent immediately as a second request, and a terminal response
        // (refusal, failed payload, event stream) must not be retried as a different shape.
        catch (Exception ex) when (ex is not (OperationCanceledException
            or TranslationAuthenticationException
            or TranslationConfigurationException
            or TranslationResponseException
            or TranslationUnusableResponseException))
        {
            _logger.LogWarning(ex, "Structured output failed, falling back to JSON parsing");
            return await CompleteBatchWithJsonParsingAsync(
                endpoint, apiKey, requestTemplate, replacements, thinking, maxTokens, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> PostChatRequestAsync(
        string endpoint,
        string? apiKey,
        string bodyJson,
        AiThinkingMode thinking,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var withTokens = AiMaxTokens.ApplyChat(_requestTemplateService, bodyJson, maxTokens);
        var requestJson = AiThinking.Apply(_requestTemplateService, withTokens, thinking);
        using var request = OpenAiCompatibleHttp.CreateJsonPost(endpoint, requestJson, apiKey, _sessionId);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private async Task<string> ReadChatContentAsync(
        string endpoint,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var responseBody = await ReadSuccessfulBodyAsync(
            endpoint, response, "chat API request", cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationResponseException("Invalid or empty response from chat API.");
        }

        return chatResponse.Choices[0].Message.Content;
    }

    /// <summary>
    /// Reads the body of a response and either returns it or throws the classified HTTP failure.
    /// Event-stream answers are rejected here: a streamed fragment would otherwise be parsed as a
    /// half-finished JSON document.
    /// </summary>
    private async Task<string> ReadSuccessfulBodyAsync(
        string endpoint,
        HttpResponseMessage response,
        string requestDescription,
        CancellationToken cancellationToken)
    {
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "{ServiceLabel} {RequestDescription} to {Endpoint} failed with status {StatusCode}: {ResponseContent}",
                _serviceLabel,
                requestDescription,
                endpoint,
                response.StatusCode,
                responseBody);
            var error =
                $"{_serviceLabel} {requestDescription} failed with status {response.StatusCode}: {responseBody}";
            throw HttpFailure.FromResponse(_serviceLabel, response, error);
        }

        NonStreamingPolicy.EnsureResponseIsNotStreaming(
            response.Content.Headers.ContentType?.MediaType, responseBody, _serviceLabel);
        return responseBody;
    }

    private async Task<Dictionary<int, string>> CompleteBatchWithStructuredOutputAsync(
        string endpoint,
        string? apiKey,
        string requestTemplate,
        Dictionary<string, string> replacements,
        AiThinkingMode thinking,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var responseFormat = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "batch_translation_response",
                strict = true,
                schema = new
                {
                    type = "object",
                    properties = new
                    {
                        translations = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    position = new
                                    {
                                        type = "integer",
                                        description = "Position number of the subtitle item"
                                    },
                                    line = new
                                    {
                                        type = "string",
                                        description = "Translated subtitle text"
                                    }
                                },
                                required = new[] { "position", "line" },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[] { "translations" },
                    additionalProperties = false
                }
            }
        };

        var bodyJson = _requestTemplateService.BuildRequestBody(requestTemplate, replacements);
        bodyJson = _requestTemplateService.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["response_format"] = responseFormat
        });

        using var response = await PostChatRequestAsync(endpoint, apiKey, bodyJson, thinking, maxTokens, cancellationToken);
        var responseBody = await ReadSuccessfulBodyAsync(
            endpoint, response, "structured output batch request", cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);
        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationException($"No completion choices returned from {_serviceLabel}");
        }

        var translatedJson = chatResponse.Choices[0].Message.Content;
        return BatchJsonParser.ParseStructuredWrapper(translatedJson, _logger);
    }

    private async Task<Dictionary<int, string>> CompleteBatchWithJsonParsingAsync(
        string endpoint,
        string? apiKey,
        string requestTemplate,
        Dictionary<string, string> replacements,
        AiThinkingMode thinking,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var bodyJson = _requestTemplateService.BuildRequestBody(requestTemplate, replacements);

        using var response = await PostChatRequestAsync(endpoint, apiKey, bodyJson, thinking, maxTokens, cancellationToken);
        var responseBody = await ReadSuccessfulBodyAsync(
            endpoint, response, "JSON parsing batch request", cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationException($"No completion choices returned from {_serviceLabel}");
        }

        var translatedJson = chatResponse.Choices[0].Message.Content;
        return BatchJsonParser.ParseJsonArray(translatedJson, _logger);
    }
}
