using System.Text.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Integrations.Translation;
using Lingarr.Server.Services.Translation.Batch;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Non-streaming OpenAI Chat Completions over HTTP for one provider identity.
/// Handles request-body construction, thinking/max-token injection, transient error
/// classification and response parsing. Retry and fallback policy stay with the caller
/// (the translation service), which owns the configured retry settings.
/// </summary>
public sealed class OpenAiChatCompletionsClient
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
        var response = await PostChatRequestAsync(endpoint, apiKey, bodyJson, thinking, maxTokens, cancellationToken);
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
        try
        {
            return await CompleteBatchWithStructuredOutputAsync(
                endpoint, apiKey, requestTemplate, replacements, thinking, maxTokens, cancellationToken);
        }
        // Fall back to plain JSON only for failures that a different request shape can fix. Cancellation
        // must stay cancellation, credentials must stay credentials (the chain skips the provider), and a
        // rate limit must not be re-sent immediately as a second request.
        catch (Exception ex) when (ex is not (OperationCanceledException
            or TranslationAuthenticationException
            or TranslationConfigurationException
            or TranslationResponseException))
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
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "{ServiceLabel} chat API request to {Endpoint} failed with status {StatusCode}: {ResponseContent}",
                _serviceLabel,
                endpoint,
                response.StatusCode,
                responseContent);
            var error = $"{_serviceLabel} chat API request failed with status {response.StatusCode}: {responseContent}";
            throw HttpFailure.FromResponse(_serviceLabel, response, error);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationResponseException("Invalid or empty response from chat API.");
        }

        return chatResponse.Choices[0].Message.Content;
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

        var response = await PostChatRequestAsync(endpoint, apiKey, bodyJson, thinking, maxTokens, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "{ServiceLabel} structured output batch request failed with status {StatusCode}: {ResponseContent}",
                _serviceLabel,
                response.StatusCode,
                responseContent);
            throw HttpFailure.FromResponse(
                _serviceLabel,
                response,
                $"{_serviceLabel} structured output batch request failed with status {response.StatusCode}: {responseContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);
        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationException($"No completion choices returned from {_serviceLabel}");
        }

        var translatedJson = chatResponse.Choices[0].Message.Content;

        try
        {
            // Parse the wrapper object first, extract the translations array
            var responseWrapper = JsonSerializer.Deserialize<JsonElement>(translatedJson);
            if (!responseWrapper.TryGetProperty("translations", out var translationsElement))
            {
                throw new TranslationParseException("Response does not contain 'translations' property");
            }

            var translatedItems =
                JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translationsElement.GetRawText());

            if (translatedItems == null)
            {
                throw new TranslationParseException("Failed to deserialize translated subtitles");
            }

            return BatchResponseMerger.MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse structured JSON response: {Json}", translatedJson);
            throw new TranslationParseException("Failed to parse structured translated subtitles", ex);
        }
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

        var response = await PostChatRequestAsync(endpoint, apiKey, bodyJson, thinking, maxTokens, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "{ServiceLabel} JSON parsing batch request failed with status {StatusCode}: {ResponseContent}",
                _serviceLabel,
                response.StatusCode,
                responseContent);
            throw HttpFailure.FromResponse(
                _serviceLabel,
                response,
                $"{_serviceLabel} JSON parsing batch request failed with status {response.StatusCode}: {responseContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationException($"No completion choices returned from {_serviceLabel}");
        }

        // Try to extract JSON
        var translatedJson = chatResponse.Choices[0].Message.Content;
        var jsonStart = translatedJson.IndexOf('[');
        var jsonEnd = translatedJson.LastIndexOf(']');
        if (jsonStart != -1 && jsonEnd != -1 && jsonEnd > jsonStart)
        {
            translatedJson = translatedJson.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        try
        {
            var translatedItems = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translatedJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (translatedItems == null)
            {
                throw new TranslationParseException("Failed to deserialize translated subtitles from JSON parsing");
            }

            return BatchResponseMerger.MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response: {Json}", translatedJson);
            throw new TranslationParseException("Failed to parse JSON translated subtitles", ex);
        }
    }
}
