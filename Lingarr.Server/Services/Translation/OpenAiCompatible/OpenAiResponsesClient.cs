using Lingarr.Contracts.Exceptions;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Non-streaming OpenAI Responses over HTTP for one provider identity. Builds the request body
/// (<c>input</c> messages, <c>max_output_tokens</c>, optional <c>text.format</c> structured output),
/// enforces the non-streaming contract and reads the terminal payload through
/// <see cref="OpenAiResponsesPayloadParser"/>. Retry and fallback policy stay with the caller,
/// which owns the configured retry settings.
/// </summary>
public sealed class OpenAiResponsesClient : IOpenAiCompatibleCompletionClient
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
    public OpenAiResponsesClient(
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

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="thinking"/> is not used: <c>ai_thinking</c> writes the <c>thinking</c> field of
    /// the Chat Completions extension and the Responses payload has no equivalent parameter.
    /// </remarks>
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

        using var response = await PostResponsesRequestAsync(endpoint, apiKey, bodyJson, maxTokens, cancellationToken);
        var responseBody = await ReadSuccessfulBodyAsync(
            endpoint, response, "responses API request", cancellationToken);

        return OpenAiResponsesPayloadParser.ExtractText(responseBody, _serviceLabel);
    }

    /// <inheritdoc />
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
                endpoint, apiKey, requestTemplate, replacements, maxTokens, cancellationToken);
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
                endpoint, apiKey, requestTemplate, replacements, maxTokens, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> PostResponsesRequestAsync(
        string endpoint,
        string? apiKey,
        string bodyJson,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var requestJson = AiMaxTokens.ApplyResponses(_requestTemplateService, bodyJson, maxTokens);
        using var request = OpenAiCompatibleHttp.CreateJsonPost(endpoint, requestJson, apiKey, _sessionId);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

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
                "{ServiceLabel} {RequestDescription} to {Endpoint} failed with status {StatusCode}: {ResponseBody}",
                _serviceLabel,
                requestDescription,
                endpoint,
                response.StatusCode,
                responseBody);
            throw HttpFailure.FromResponse(
                _serviceLabel,
                response,
                $"{_serviceLabel} {requestDescription} failed with status {response.StatusCode}: {responseBody}");
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
        int maxTokens,
        CancellationToken cancellationToken)
    {
        // Responses configures structured output through text.format, not response_format.
        var structuredOutput = new
        {
            format = new
            {
                type = "json_schema",
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
            ["text"] = structuredOutput
        });

        using var response = await PostResponsesRequestAsync(endpoint, apiKey, bodyJson, maxTokens, cancellationToken);
        var responseBody = await ReadSuccessfulBodyAsync(
            endpoint, response, "structured output batch request", cancellationToken);

        return BatchJsonParser.ParseStructuredWrapper(
            OpenAiResponsesPayloadParser.ExtractText(responseBody, _serviceLabel),
            _logger);
    }

    private async Task<Dictionary<int, string>> CompleteBatchWithJsonParsingAsync(
        string endpoint,
        string? apiKey,
        string requestTemplate,
        Dictionary<string, string> replacements,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        var bodyJson = _requestTemplateService.BuildRequestBody(requestTemplate, replacements);

        using var response = await PostResponsesRequestAsync(endpoint, apiKey, bodyJson, maxTokens, cancellationToken);
        var responseBody = await ReadSuccessfulBodyAsync(
            endpoint, response, "JSON parsing batch request", cancellationToken);

        return BatchJsonParser.ParseJsonArray(
            OpenAiResponsesPayloadParser.ExtractText(responseBody, _serviceLabel),
            _logger);
    }
}
