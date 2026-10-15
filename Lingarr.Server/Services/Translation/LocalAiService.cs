using System.Net;
using System.Text.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Integrations.Translation;
using Lingarr.Server.Services.Translation.Base;
using Lingarr.Server.Services.Translation.LocalAi;

namespace Lingarr.Server.Services.Translation;

public class LocalAiService : BaseLanguageService, ITranslationService, IBatchTranslationService, IProofreadService
{
    private readonly HttpClient _httpClient;
    private readonly IRequestTemplateService _requestTemplateService;
    private string? _model;
    private string? _endpoint;
    private string? _apiKey;
    private string? _chatRequestTemplate;
    private string? _generateRequestTemplate;
    private bool _isChatEndpoint;
    private AiThinkingMode _aiThinking;
    private int _maxTokens;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly string _sessionId = OpenCodeGoHeaders.NewSessionId();

    /// <inheritdoc />
    public override string? ModelName => _model;

    // retry settings
    private int _maxRetries;
    private TimeSpan _retryDelay;
    private int _retryDelayMultiplier;

    public LocalAiService(
        ISettingService settings,
        HttpClient httpClient,
        ILogger<LocalAiService> logger,
        LanguageCodeService languageCodeService,
        IRequestTemplateService requestTemplateService)
        : base(settings, logger, languageCodeService)
    {
        _httpClient = httpClient;
        _requestTemplateService = requestTemplateService;
    }

    /// <summary>
    /// Initializes the translation service with necessary configurations and credentials.
    /// This method is thread-safe and ensures one-time initialization of service dependencies.
    /// </summary>
    /// <param name="sourceLanguage">The source language code for translation</param>
    /// <param name="targetLanguage">The target language code for translation</param>
    /// <returns>A task that represents the asynchronous initialization operation</returns>
    /// <exception cref="InvalidOperationException">Thrown when required configuration settings are missing or invalid</exception>
    private async Task InitializeAsync(string sourceLanguage, string targetLanguage)
    {
        if (_initialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var settings = await _settings.GetSettings([
                SettingKeys.Translation.LocalAi.Model,
                SettingKeys.Translation.LocalAi.Endpoint,
                SettingKeys.Translation.LocalAi.Protocol,
                SettingKeys.Translation.LocalAi.ChatRequestTemplate,
                SettingKeys.Translation.LocalAi.GenerateRequestTemplate,
                SettingKeys.Translation.AiPrompt,
                SettingKeys.Translation.AiUserPrompt,
                SettingKeys.Translation.AiMaxTokens,
                SettingKeys.Translation.AiThinking,
                SettingKeys.Translation.ProofreadPrompt,
                SettingKeys.Translation.ProofreadUserPrompt,
                SettingKeys.Translation.RequestTimeout,
                SettingKeys.Translation.MaxRetries,
                SettingKeys.Translation.RetryDelay,
                SettingKeys.Translation.RetryDelayMultiplier,
                SettingKeys.Translation.LanguageCodeFormat
            ]);
            _model = settings[SettingKeys.Translation.LocalAi.Model];
            _endpoint = settings[SettingKeys.Translation.LocalAi.Endpoint];
            _chatRequestTemplate = !string.IsNullOrEmpty(settings[SettingKeys.Translation.LocalAi.ChatRequestTemplate])
                ? settings[SettingKeys.Translation.LocalAi.ChatRequestTemplate]
                : _requestTemplateService.GetDefaultTemplate(SettingKeys.Translation.LocalAi.ChatRequestTemplate);
            _generateRequestTemplate = !string.IsNullOrEmpty(settings[SettingKeys.Translation.LocalAi.GenerateRequestTemplate])
                ? settings[SettingKeys.Translation.LocalAi.GenerateRequestTemplate]
                : _requestTemplateService.GetDefaultTemplate(SettingKeys.Translation.LocalAi.GenerateRequestTemplate);

            if (string.IsNullOrEmpty(_model) || string.IsNullOrEmpty(_endpoint))
            {
                throw new InvalidOperationException("Local AI service requires both endpoint address and model name to be configured in settings.");
            }

            SetLanguageReplacements(sourceLanguage, targetLanguage, settings[SettingKeys.Translation.LanguageCodeFormat]);
            _prompt = settings[SettingKeys.Translation.AiPrompt];
            _userPrompt = settings[SettingKeys.Translation.AiUserPrompt];
            _aiThinking = AiThinking.Parse(settings.GetValueOrDefault(SettingKeys.Translation.AiThinking));
            _maxTokens = AiMaxTokens.Parse(settings.GetValueOrDefault(SettingKeys.Translation.AiMaxTokens));
            _proofreadPrompt = settings.GetValueOrDefault(SettingKeys.Translation.ProofreadPrompt);
            _proofreadUserPrompt = settings.GetValueOrDefault(SettingKeys.Translation.ProofreadUserPrompt);
            settings.TryGetValue(SettingKeys.Translation.LocalAi.Protocol, out var protocolSetting);
            var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(
                _endpoint,
                LocalAiProtocolParser.Parse(protocolSetting));
            _endpoint = resolved.Url;
            _isChatEndpoint = resolved.IsChat;

            var requestTimeout = int.TryParse(settings[SettingKeys.Translation.RequestTimeout],
                out var timeOut)
                ? timeOut
                : 5;
            _httpClient.Timeout = TimeSpan.FromMinutes(requestTimeout);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            var apiKey = await _settings.GetEncryptedSetting(SettingKeys.Translation.LocalAi.ApiKey);
            _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            if (string.IsNullOrEmpty(_apiKey) && _isChatEndpoint)
            {
                _logger.LogWarning(
                    "LocalAI API key is not configured. Authenticated OpenAI-compatible gateways will reject chat completions.");
            }

            _maxRetries = int.TryParse(settings[SettingKeys.Translation.MaxRetries], out var maxRetries) 
                ? maxRetries 
                : 5;
            var retryDelaySeconds = int.TryParse(settings[SettingKeys.Translation.RetryDelay], out var delaySeconds) 
                ? delaySeconds 
                : 1;
            _retryDelay = TimeSpan.FromSeconds(retryDelaySeconds);
            _retryDelayMultiplier = int.TryParse(settings[SettingKeys.Translation.RetryDelayMultiplier], out var multiplier) 
                ? multiplier 
                : 2;

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        List<string>? contextLinesBefore,
        List<string>? contextLinesAfter,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        var replacements = GetReplacements(_model!, text, contextLinesBefore, contextLinesAfter);
        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);

        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await CompleteWithLocalAiApi(replacements, retry.Token);
            }
            catch (TranslationResponseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Too many requests. Max retries exhausted for text: {Text}", text);
                    throw new TranslationException("Too many requests. Retry limit reached.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "Transient LocalAI error. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);
            }
            catch (TranslationException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during translation.", ex);
            }
        }

        throw new TranslationException("Translation failed after maximum retry attempts.");
    }

    /// <inheritdoc />
    public async Task<string> ProofreadAsync(
        string sourceText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        var replacements = GetProofreadReplacements(_model!, sourceText, translatedText);
        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);

        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await CompleteWithLocalAiApi(replacements, linked.Token);
            }
            catch (TranslationResponseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Too many requests. Max retries exhausted for text: {Text}", translatedText);
                    throw new TranslationException("Too many requests. Retry limit reached.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "Transient LocalAI error. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    delay, attempt, _maxRetries);
            }
            catch (TranslationException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during proofread attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during proofread.", ex);
            }
        }

        throw new TranslationException("Proofread failed after maximum retry attempts.");
    }

    private async Task<string> CompleteWithLocalAiApi(
        Dictionary<string, string> replacements,
        CancellationToken cancellationToken)
    {
        return _isChatEndpoint
            ? await TranslateWithChatApi(replacements, cancellationToken)
            : await TranslateWithGenerateApi(replacements, cancellationToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string jsonBody, CancellationToken cancellationToken)
    {
        using var request = LocalAiHttp.CreateJsonPost(_endpoint!, jsonBody, _apiKey, _sessionId);
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Posts an OpenAI-compatible chat body. Max tokens and thinking are injected
    /// only when the user set them; nothing is probed or remembered after a 400.
    /// </summary>
    private async Task<HttpResponseMessage> PostChatRequestAsync(string bodyJson, CancellationToken cancellationToken)
    {
        var withTokens = AiMaxTokens.ApplyChat(_requestTemplateService, bodyJson, _maxTokens);
        var requestJson = AiThinking.Apply(_requestTemplateService, withTokens, _aiThinking);
        return await PostJsonAsync(requestJson, cancellationToken);
    }

    /// <summary>
    /// Translates a batch of subtitles in a single API call using structured outputs fallback
    /// Since LocalAI may not support structured outputs, we'll attempt structured format first,
    /// then fall back to regular parsing if needed. Responses that cannot be parsed are retried
    /// using the configured retry settings, as local models occasionally emit malformed JSON.
    /// </summary>
    /// <param name="subtitleBatch">List of subtitles with position and content</param>
    /// <param name="sourceLanguage">Source language code</param>
    /// <param name="targetLanguage">Target language code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping position to translated content</returns>
    public async Task<Dictionary<int, string>> TranslateBatchAsync(
        List<BatchSubtitleItem> subtitleBatch,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(sourceLanguage, targetLanguage);

        using var retry = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, retry.Token);
        
        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                return await TranslateBatchWithLocalAiApi(subtitleBatch, linked.Token);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries exhausted ({StatusCode}) for batch translation", ex.StatusCode);
                    throw new TranslationException($"Retry limit reached after {ex.StatusCode}.", ex);
                }

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "{ServiceName} received {StatusCode}. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    "LocalAI", ex.StatusCode, delay, attempt, _maxRetries);
            }
            catch (TranslationParseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries exhausted for batch translation, the model kept returning an unparsable response");
                    throw new TranslationException("Retry limit reached after unparsable response.", ex);
                }

                _logger.LogWarning(
                    "{ServiceName} returned an unparsable response. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    "LocalAI", delay, attempt, _maxRetries);

                await Task.Delay(delay, linked.Token).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during batch translation attempt {Attempt}", attempt);
                throw new TranslationException("Unexpected error occurred during batch translation.", ex);
            }
        }

        throw new TranslationException("Batch translation failed after maximum retry attempts.");
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithLocalAiApi(
        List<BatchSubtitleItem> subtitleBatch,
        CancellationToken cancellationToken)
    {
        if (!_isChatEndpoint)
        {
            return await TranslateBatchWithGenerateApi(subtitleBatch, cancellationToken);
        }

        // Try structured output first (OpenAI-compatible format)
        try
        {
            return await TranslateBatchWithStructuredOutput(subtitleBatch, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Structured output failed, falling back to JSON parsing");
            return await TranslateBatchWithJsonParsing(subtitleBatch, cancellationToken);
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithStructuredOutput(
        List<BatchSubtitleItem> subtitleBatch,
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

        var replacements = GetBatchReplacements(_model!, JsonSerializer.Serialize(subtitleBatch));
        var bodyJson = _requestTemplateService.BuildRequestBody(_chatRequestTemplate!, replacements);
        bodyJson = _requestTemplateService.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["response_format"] = responseFormat
        });

        var response = await PostChatRequestAsync(bodyJson, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "LocalAI structured output batch request failed with status {StatusCode}: {ResponseContent}",
                response.StatusCode, 
                responseContent);
            throw new TranslationException(
                $"LocalAI structured output batch request failed with status {response.StatusCode}: {responseContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);
        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationException("No completion choices returned from LocalAI");
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

            return MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse structured JSON response: {Json}", translatedJson);
            throw new TranslationParseException("Failed to parse structured translated subtitles", ex);
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithJsonParsing(
        List<BatchSubtitleItem> subtitleBatch,
        CancellationToken cancellationToken)
    {
        var replacements = GetBatchReplacements(_model!, JsonSerializer.Serialize(subtitleBatch));
        var bodyJson = _requestTemplateService.BuildRequestBody(_chatRequestTemplate!, replacements);

        var response = await PostChatRequestAsync(bodyJson, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "LocalAI JSON parsing batch request failed with status {StatusCode}: {ResponseContent}",
                response.StatusCode, 
                responseContent);
            throw new TranslationException(
                $"LocalAI JSON parsing batch request failed with status {response.StatusCode}: {responseContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationException("No completion choices returned from LocalAI");
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

            return MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON response: {Json}", translatedJson);
            throw new TranslationParseException("Failed to parse JSON translated subtitles", ex);
        }
    }

    private async Task<Dictionary<int, string>> TranslateBatchWithGenerateApi(
        List<BatchSubtitleItem> subtitleBatch,
        CancellationToken cancellationToken)
    {
        var replacements = GetBatchReplacements(_model!, JsonSerializer.Serialize(subtitleBatch));
        replacements["systemPrompt"] +=
            "\n\nPlease return the response as a JSON array with objects containing 'position' and 'line' fields. Example: [{\"position\": 1, \"line\": \"translated text\"}]";
        var bodyJson = _requestTemplateService.BuildRequestBody(_generateRequestTemplate!, replacements);
        bodyJson = _requestTemplateService.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["stream"] = false
        });

        var response = await PostJsonAsync(bodyJson, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "LocalAI generate API batch request failed with status {StatusCode}: {ResponseContent}",
                response.StatusCode, responseContent);
            throw new TranslationException(
                $"LocalAI generate API batch request failed with status {response.StatusCode}: {responseContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var generateResponse = JsonSerializer.Deserialize<GenerateResponse>(responseBody);

        if (generateResponse == null || string.IsNullOrEmpty(generateResponse.Response))
        {
            throw new TranslationException("Invalid or empty response from generate API.");
        }

        var translatedJson = generateResponse.Response;

        // Try to extract JSON from the response
        var jsonStart = translatedJson.IndexOf('[');
        var jsonEnd = translatedJson.LastIndexOf(']');

        if (jsonStart != -1 && jsonEnd != -1 && jsonEnd > jsonStart)
        {
            translatedJson = translatedJson.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        try
        {
            var translatedItems = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translatedJson);

            if (translatedItems == null)
            {
                throw new TranslationParseException("Failed to deserialize translated subtitles from generate API");
            }

            return MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse generate API JSON response: {Json}", translatedJson);
            throw new TranslationParseException("Failed to parse generate API translated subtitles", ex);
        }
    }

    private async Task<string> TranslateWithGenerateApi(
        Dictionary<string, string> replacements,
        CancellationToken cancellationToken)
    {
        var bodyJson = _requestTemplateService.BuildRequestBody(_generateRequestTemplate!, replacements);
        var response = await PostJsonAsync(bodyJson, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "LocalAI generate API request failed with status {StatusCode}: {ResponseContent}",
                response.StatusCode, responseContent);
            throw new TranslationException(
                $"LocalAI generate API request failed with status {response.StatusCode}: {responseContent}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var generateResponse = JsonSerializer.Deserialize<GenerateResponse>(responseBody);

        if (generateResponse == null || string.IsNullOrEmpty(generateResponse.Response))
        {
            throw new TranslationException("Invalid or empty response from generate API.");
        }

        return generateResponse.Response;
    }

    private async Task<string> TranslateWithChatApi(
        Dictionary<string, string> replacements,
        CancellationToken cancellationToken)
    {
        var bodyJson = _requestTemplateService.BuildRequestBody(_chatRequestTemplate!, replacements);
        var response = await PostChatRequestAsync(bodyJson, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "LocalAI chat API request to {Endpoint} failed with status {StatusCode}: {ResponseContent}",
                _endpoint, 
                response.StatusCode, 
                responseContent);
            var error = $"LocalAI chat API request failed with status {response.StatusCode}: {responseContent}";
            if (LocalAiHttp.IsTransient(response.StatusCode))
            {
                throw new TranslationResponseException(error);
            }

            throw new TranslationException(error);
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody);

        if (chatResponse?.Choices == null || chatResponse.Choices.Count == 0)
        {
            throw new TranslationResponseException("Invalid or empty response from chat API.");
        }

        return chatResponse.Choices[0].Message.Content;
    }

    /// <inheritdoc />
    public override async Task<ModelsResponse> GetModels()
    {
        var endpoint = await _settings.GetSetting(SettingKeys.Translation.LocalAi.Endpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new ModelsResponse
            {
                Message = "LocalAI endpoint is not configured."
            };
        }

        var protocol = await _settings.GetSetting(SettingKeys.Translation.LocalAi.Protocol);
        var apiKey = await _settings.GetEncryptedSetting(SettingKeys.Translation.LocalAi.ApiKey);
        return await LocalAiModelCatalog.FetchAsync(_httpClient, endpoint, protocol, apiKey);
    }
}