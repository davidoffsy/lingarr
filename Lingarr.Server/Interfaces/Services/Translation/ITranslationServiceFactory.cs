using Lingarr.Contracts.Translation;
using Lingarr.Server.Services.Translation.Chain;

namespace Lingarr.Server.Interfaces.Services.Translation;

public interface ITranslationServiceFactory
{
    /// <summary>
    /// Creates and returns an instance of a translation service based on the specified service type.
    /// </summary>
    /// <param name="serviceType">The provider name (case-insensitive), e.g. "libretranslate", "openai", "anthropic", "deepl", "gemini", "deepseek", "mistral", "xai", "localai", "custom", "google", "bing", "microsoft", "yandex".</param>
    /// <returns>An instance of <see cref="ITranslationService"/> corresponding to the specified service type.</returns>
    /// <exception cref="ArgumentException">Thrown when an unsupported service type is specified.</exception>
    ITranslationService CreateTranslationService(string serviceType);

    /// <summary>
    /// Builds the ordered list of translation candidates from parsed chain entries. Every candidate is
    /// constructed as an independent instance so a model override cannot leak into its neighbours.
    /// Unknown providers, unconstructable providers and model overrides unsupported by a provider are
    /// skipped with a warning, so a single bad entry never prevents the rest of the chain from working.
    /// </summary>
    /// <param name="entries">Ordered candidates. Position 0 is the primary; the rest are fallbacks.</param>
    /// <returns>Ordered list of candidates; empty if no entry could be constructed.</returns>
    IReadOnlyList<TranslationServiceEntry> CreateTranslationServices(IReadOnlyList<TranslationChainEntry> entries);
}