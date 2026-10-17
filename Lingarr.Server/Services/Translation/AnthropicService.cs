using System.Text.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Translation.AnthropicMessages;
using Lingarr.Server.Services.Translation.Base;

namespace Lingarr.Server.Services.Translation;

public class AnthropicService : BaseLanguageService, ITranslationService, IBatchTranslationService, IProofreadService, IModelOverridable
{
    private readonly string? _endpoint = "https://api.anthropic.com/v1";
    private readonly HttpClient _httpClient;
    private readonly IRequestTemplateService _requestTemplateService;
    private readonly AnthropicMessagesClient _messagesClient;
    private string? _model;
    private string? _apiKey;
    private string? _version;
    private string? _requestTemplate;
    private int _maxTokens;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private TimeSpan _requestTimeout;

    /// <inheritdoc />
    public override string? ModelName => _model;

    /// <inheritdoc />
    public void OverrideModel(string model) => SetModelOverride(model);

    // retry settings
    private int _maxRetries;
    private TimeSpan _retryDelay;
    private int _retryDelayMultiplier;

    public AnthropicService(ISettingService settings,
        HttpClient httpClient,
        ILogger<AnthropicService> logger,
        LanguageCodeService languageCodeService,
        IRequestTemplateService requestTemplateService)
        : base(settings, logger, languageCodeService)
    {
        _httpClient = httpClient;
        _requestTemplateService = requestTemplateService;
        // Official Anthropic stays on the lenient parser (strict: false). Headers and timeout are
        // applied per request, so this shared HttpClient is not mutated.
        _messagesClient = new AnthropicMessagesClient(httpClient, requestTemplateService, logger, "Anthropic");
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
                SettingKeys.Translation.Anthropic.Model,
                SettingKeys.Translation.Anthropic.Version,
                SettingKeys.Translation.Anthropic.RequestTemplate,
                SettingKeys.Translation.AiPrompt,
                SettingKeys.Translation.AiUserPrompt,
                SettingKeys.Translation.AiMaxTokens,
                SettingKeys.Translation.ProofreadPrompt,
                SettingKeys.Translation.ProofreadUserPrompt,
                SettingKeys.Translation.RequestTimeout,
                SettingKeys.Translation.MaxRetries,
                SettingKeys.Translation.RetryDelay,
                SettingKeys.Translation.RetryDelayMultiplier,
                SettingKeys.Translation.LanguageCodeFormat
            ]);
            _model = ResolveModel(settings[SettingKeys.Translation.Anthropic.Model]);
            _apiKey = await _settings.GetEncryptedSetting(SettingKeys.Translation.Anthropic.ApiKey);
            _version = settings[SettingKeys.Translation.Anthropic.Version];
            _requestTemplate = !string.IsNullOrEmpty(settings[SettingKeys.Translation.Anthropic.RequestTemplate])
                ? settings[SettingKeys.Translation.Anthropic.RequestTemplate]
                : _requestTemplateService.GetDefaultTemplate(SettingKeys.Translation.Anthropic.RequestTemplate);

            if (string.IsNullOrEmpty(_model) || string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_version))
            {
                throw new InvalidOperationException("Anthropic API key, model or version is not configured.");
            }

            SetLanguageReplacements(sourceLanguage, targetLanguage, settings[SettingKeys.Translation.LanguageCodeFormat]);
            _prompt = settings[SettingKeys.Translation.AiPrompt];
            _userPrompt = settings[SettingKeys.Translation.AiUserPrompt];
            _maxTokens = AiMaxTokens.Parse(settings.GetValueOrDefault(SettingKeys.Translation.AiMaxTokens));
            _proofreadPrompt = settings.GetValueOrDefault(SettingKeys.Translation.ProofreadPrompt);
            _proofreadUserPrompt = settings.GetValueOrDefault(SettingKeys.Translation.ProofreadUserPrompt);

            var requestTimeout = int.TryParse(settings[SettingKeys.Translation.RequestTimeout],
                out var timeOut)
                ? timeOut
                : 5;
            _requestTimeout = TimeSpan.FromMinutes(requestTimeout);

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

        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var replacements = GetReplacements(_model!, text, contextLinesBefore, contextLinesAfter);
                using var requestTimeout = CreateRequestTimeout(cancellationToken);
                return await CompleteWithAnthropicApi(replacements, requestTimeout.Token);
            }
            catch (TranslationResponseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries exhausted ({StatusCode}) for text: {Text}", ex.StatusCode, text);
                    throw new TranslationException($"Retry limit reached after {ex.StatusCode}.", ex);
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "{ServiceName} received {StatusCode}. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    "Anthropic", ex.StatusCode, delay, attempt, _maxRetries);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TranslationException)
            {
                // Re-throw so the chain keeps the failure classification (rejected credentials, unusable
                // configuration, rate limits) instead of wrapping it into a generic translation failure.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during Anthropic translation");
                throw new TranslationException("Failed to translate using Anthropic", ex);
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

        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var replacements = GetProofreadReplacements(_model!, sourceText, translatedText);
                using var requestTimeout = CreateRequestTimeout(cancellationToken);
                return await CompleteWithAnthropicApi(replacements, requestTimeout.Token);
            }
            catch (TranslationResponseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries exhausted ({StatusCode}) for text: {Text}", ex.StatusCode, translatedText);
                    throw new TranslationException($"Retry limit reached after {ex.StatusCode}.", ex);
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "{ServiceName} received {StatusCode}. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    "Anthropic", ex.StatusCode, delay, attempt, _maxRetries);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TranslationException)
            {
                // Re-throw so the chain keeps the failure classification (rejected credentials, unusable
                // configuration, rate limits) instead of wrapping it into a generic translation failure.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during Anthropic proofread");
                throw new TranslationException("Failed to proofread using Anthropic", ex);
            }
        }

        throw new TranslationException("Proofread failed after maximum retry attempts.");
    }

    private Task<string> CompleteWithAnthropicApi(
        Dictionary<string, string> replacements,
        CancellationToken cancellationToken) =>
        _messagesClient.CompleteAsync(
            $"{_endpoint}/messages",
            _apiKey,
            _version!,
            _requestTemplate!,
            replacements,
            _maxTokens,
            strict: false,
            cancellationToken);

    /// <summary>
    /// Translates a batch of subtitles in a single API call using Anthropic's structured output
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

        var delay = _retryDelay;
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            try
            {
                using var requestTimeout = CreateRequestTimeout(cancellationToken);
                return await TranslateBatchWithAnthropicApi(subtitleBatch, requestTimeout.Token);
            }
            catch (TranslationResponseException ex)
            {
                if (attempt == _maxRetries)
                {
                    _logger.LogError(ex, "Max retries exhausted ({StatusCode}) for batch translation", ex.StatusCode);
                    throw new TranslationException($"Retry limit reached after {ex.StatusCode}.", ex);
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(delay.Ticks * _retryDelayMultiplier);

                _logger.LogWarning(
                    "{ServiceName} received {StatusCode}. Retrying in {Delay}... (Attempt {Attempt}/{MaxRetries})",
                    "Anthropic", ex.StatusCode, delay, attempt, _maxRetries);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TranslationException)
            {
                // Re-throw so the chain keeps the failure classification (rejected credentials, unusable
                // configuration, rate limits) instead of wrapping it into a generic translation failure.
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

    private Task<Dictionary<int, string>> TranslateBatchWithAnthropicApi(
        List<BatchSubtitleItem> subtitleBatch,
        CancellationToken cancellationToken)
    {
        var replacements = GetBatchReplacements(_model!, JsonSerializer.Serialize(subtitleBatch));
        return _messagesClient.CompleteBatchAsync(
            $"{_endpoint}/messages",
            _apiKey,
            _version!,
            _requestTemplate!,
            replacements,
            _maxTokens,
            strict: false,
            cancellationToken);
    }

    /// <summary>
    /// Per-request cancellation token: the caller's token combined with the configured request
    /// timeout. The injected <see cref="HttpClient"/> is shared and never mutated, so the same
    /// instance can serve several providers with different timeouts.
    /// </summary>
    private CancellationTokenSource CreateRequestTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        return timeout;
    }

    /// <inheritdoc />
    public override async Task<ModelsResponse> GetModels()
    {
        var apiKey = await _settings.GetEncryptedSetting(SettingKeys.Translation.Anthropic.ApiKey);
        var version = await _settings.GetSetting(SettingKeys.Translation.Anthropic.Version);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new ModelsResponse
            {
                Message = "Anthropic API key is not configured."
            };
        }

        if (string.IsNullOrEmpty(version))
        {
            return new ModelsResponse
            {
                Message = "Anthropic version is not configured."
            };
        }

        return await AnthropicMessagesModelCatalog.FetchAsync(_httpClient, _endpoint!, apiKey, version);
    }
}