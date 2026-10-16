namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Classification of a problem found while reading or validating a translation chain. The kinds are
/// collected instead of thrown so a single bad entry can never silently replace a usable chain with
/// a default provider; callers decide between fallback and failure.
/// </summary>
public enum TranslationChainIssueKind
{
    /// <summary>The array form could not be read as JSON.</summary>
    MalformedJson,

    /// <summary>An entry is neither a provider name nor an object declaring a provider.</summary>
    InvalidEntry,

    /// <summary>An object entry declares a model that is neither a string nor null.</summary>
    InvalidModel,

    /// <summary>An entry has no provider identifier at all.</summary>
    EmptyProvider,

    /// <summary>The provider is not registered (typo, plugin not installed, provider removed).</summary>
    UnknownProvider,

    /// <summary>The same provider and model already appeared earlier in the chain.</summary>
    DuplicateProviderModel,

    /// <summary>
    /// The candidate carries a model override for a provider that does not declare
    /// <c>SupportsModelOverride</c>, so the override cannot be honoured.
    /// </summary>
    ModelOverrideNotSupported,

    /// <summary>The chain holds no usable entry and needs an explicit fallback decision.</summary>
    EmptyChain
}

/// <summary>
/// One problem reported by <see cref="TranslationChainParser"/> or <see cref="TranslationChainValidator"/>.
/// </summary>
/// <param name="Kind">Problem classification.</param>
/// <param name="Index">Zero-based position in the configured chain; -1 for chain-level issues.</param>
/// <param name="Message">Human readable description intended for logs and API errors.</param>
/// <param name="Value">Offending raw value when one is meaningful (provider or model text).</param>
public sealed record TranslationChainIssue(
    TranslationChainIssueKind Kind,
    int Index,
    string Message,
    string? Value = null);