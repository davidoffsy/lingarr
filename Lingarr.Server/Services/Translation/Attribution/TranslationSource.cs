using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation.Attribution;

/// <summary>
/// Provider and model that actually produced a translated line. <see cref="Model"/> is null when the
/// provider has no model concept (LibreTranslate, DeepL, Google/Bing/Microsoft/Yandex) or when a
/// provider with one did not report it.
/// </summary>
/// <param name="Provider">Provider identifier as built, for example <c>custom</c>.</param>
/// <param name="Model">Model that translated the line, when the provider uses one.</param>
public readonly record struct TranslationSource(string Provider, string? Model)
{
    /// <summary>
    /// Effective source of a chain candidate: its model override when it has one, otherwise the model
    /// the provider resolved from its own settings.
    /// </summary>
    public static TranslationSource FromCandidate(TranslationServiceEntry entry) =>
        new(entry.Name, entry.Model ?? entry.Service.ModelName);

    /// <summary>Source rendered for logs, subtitles and statistics, for example <c>custom:model-a</c>.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Provider : $"{Provider}:{Model}";
}
