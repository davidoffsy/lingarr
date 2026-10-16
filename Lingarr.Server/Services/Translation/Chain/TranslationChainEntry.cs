namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// One candidate in the ordered translation chain: a provider identifier with an optional model
/// override. The chain is a flat ordered list, so the same provider may appear several times as
/// long as the candidates differ (same provider, different model).
/// </summary>
/// <param name="Provider">
/// Provider identifier as configured (for example <c>custom</c> or <c>openai</c>). Provider ids are
/// compared case-insensitively; the configured spelling is preserved for round-tripping.
/// </param>
/// <param name="Model">
/// Model override for this candidate, or <c>null</c> to use the provider's configured default model.
/// </param>
public readonly record struct TranslationChainEntry(string Provider, string? Model)
{
    /// <summary>
    /// Canonical form of <see cref="Provider"/> used for identity checks (duplicate detection,
    /// known-provider lookups) while <see cref="Provider"/> keeps the configured spelling.
    /// </summary>
    public string ProviderKey => (Provider ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Canonical form of <see cref="Model"/>, or <c>null</c> when the candidate has no model
    /// override. A blank model is treated as "no override" so the provider default applies.
    /// Model text itself is compared case-sensitively.
    /// </summary>
    public string? ModelKey => string.IsNullOrWhiteSpace(Model) ? null : Model.Trim();

    /// <summary>True when this candidate carries a model override.</summary>
    public bool HasModelOverride => ModelKey is not null;

    /// <summary>
    /// Configuration form of this candidate for logs and error messages:
    /// <c>custom (model-a)</c> when a model override is set, otherwise the bare provider id.
    /// </summary>
    public string Describe() => ModelKey is null ? Provider : $"{Provider} ({ModelKey})";

    /// <summary>Comma separated <see cref="Describe"/> of <paramref name="entries"/>.</summary>
    public static string DescribeAll(IEnumerable<TranslationChainEntry> entries) =>
        string.Join(", ", entries.Select(entry => entry.Describe()));
}