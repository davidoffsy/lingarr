using GTranslate.Translators;
using Lingarr.Contracts.Translation;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services.Translation.Chain;

namespace Lingarr.Server.Services.Translation;

public class TranslationFactory : ITranslationServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TranslationFactory> _logger;

    public TranslationFactory(IServiceProvider serviceProvider,
        ILogger<TranslationFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public ITranslationService CreateTranslationService(string serviceType)
    {
        var languageCodeService = _serviceProvider.GetRequiredService<LanguageCodeService>();
        return serviceType.ToLower() switch
        {
            "libretranslate" => new LibreService(
                _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(),
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<LibreService>>(),
                languageCodeService),

            "google" => new GTranslatorService<GoogleTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "/app/Statics/google_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<GoogleTranslator>>(),
                languageCodeService
            ),

            "bing" => new GTranslatorService<BingTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "/app/Statics/bing_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<BingTranslator>>(),
                languageCodeService
            ),

            "microsoft" => new GTranslatorService<MicrosoftTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "/app/Statics/microsoft_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<MicrosoftTranslator>>(),
                languageCodeService
            ),

            "yandex" => new GTranslatorService<YandexTranslator>(
                _serviceProvider,
                _serviceProvider.GetRequiredService<IHttpClientFactory>(),
                "/app/Statics/yandex_languages.json",
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<YandexTranslator>>(),
                languageCodeService
            ),

            "deepl" => new DeepLService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<DeepLService>>(),
                languageCodeService
            ),

            "openai" => new OpenAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                _serviceProvider.GetRequiredService<ILogger<OpenAiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "anthropic" => new AnthropicService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<AnthropicService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "localai" => new LocalAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<LocalAiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "custom" => new CustomService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<CustomService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "deepseek" => new DeepSeekService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<DeepSeekService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "mistral" => new MistralService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<MistralService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "xai" => new XAiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<XAiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            "gemini" => new GoogleGeminiService(
                _serviceProvider.GetRequiredService<ISettingService>(),
                CreateHttpClient(),
                _serviceProvider.GetRequiredService<ILogger<GoogleGeminiService>>(),
                languageCodeService,
                _serviceProvider.GetRequiredService<IRequestTemplateService>()
            ),

            // load any registered plugins
            _ => _serviceProvider.GetKeyedService<ITranslationService>(serviceType.ToLowerInvariant())
                 ?? throw new ArgumentException("Unsupported translation service type", nameof(serviceType))
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<TranslationServiceEntry> CreateTranslationServices(IReadOnlyList<TranslationChainEntry> entries)
    {
        var candidates = new List<TranslationServiceEntry>(entries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var name = entry.ProviderKey;
            // The same (provider, model) twice adds no fallback value: the second candidate would
            // always fail exactly like the first. Kept explicit so a duplicate never silently
            // consumes retries, while the same provider with a different model stays usable.
            var identity = TranslationChainIdentity.Key(name, entry.ModelKey);
            if (!seen.Add(identity))
            {
                _logger.LogWarning(
                    "Skipping duplicate translation candidate '{Candidate}': it already appears earlier in the chain.",
                    entry.Describe());
                continue;
            }

            try
            {
                var service = CreateTranslationService(name);
                if (!ApplyModelOverride(name, entry, service))
                {
                    continue;
                }

                candidates.Add(new TranslationServiceEntry(
                    name,
                    service,
                    service as IBatchTranslationService,
                    entry.ModelKey));
            }
            catch (ArgumentException)
            {
                _logger.LogWarning("Skipping unknown translation service '{ServiceType}'.", name);
            }
        }
        return candidates;
    }

    /// <summary>
    /// Applies the candidate's model override to a freshly built instance. Returns false when the
    /// provider does not declare support for overrides, in which case the candidate is dropped rather
    /// than silently translated with the provider default model.
    /// </summary>
    private bool ApplyModelOverride(string provider, TranslationChainEntry entry, ITranslationService service)
    {
        var model = entry.ModelKey;
        if (model is null)
        {
            return true;
        }

        if (service is IModelOverridable overridable)
        {
            overridable.OverrideModel(model);
            return true;
        }

        _logger.LogWarning(
            "Provider '{Provider}' does not support a per-candidate model; skipping the candidate with model '{Model}'. " +
            "A provider must not declare SupportsModelOverride without implementing IModelOverridable.",
            provider,
            model);
        return false;
    }

    /// <summary>
    /// A new <see cref="HttpClient"/> from the factory. Each call is a distinct instance, so a
    /// provider can set <see cref="HttpClient.Timeout"/> without affecting other candidates.
    /// </summary>
    private HttpClient CreateHttpClient() =>
        _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
}
