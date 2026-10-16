using Lingarr.Server.Services.Translation.Chain;

namespace Lingarr.Server.Models.Api;

/// <summary>Request body for validating a translation chain before it is saved.</summary>
public sealed class TranslationChainValidationRequest
{
    /// <summary>
    /// Raw <c>service_type</c> value the client wants to store. Historic single values, string arrays
    /// and the canonical object array are all accepted.
    /// </summary>
    public string? ServiceType { get; init; }
}

/// <summary>One problem found in a translation chain.</summary>
/// <param name="Kind">Machine readable problem kind.</param>
/// <param name="Index">Zero-based candidate index, or -1 for chain level problems.</param>
/// <param name="Message">Message suitable for display in the settings UI.</param>
/// <param name="Value">Offending provider or model text, when meaningful.</param>
public sealed record TranslationChainValidationIssue(
    TranslationChainIssueKind Kind,
    int Index,
    string Message,
    string? Value);

/// <summary>
/// Validation result for a translation chain: the canonical value the server would store, the
/// candidates it could read, and every problem found. An empty issue list means the chain is usable.
/// </summary>
public sealed class TranslationChainValidationResponse
{
    /// <summary>Canonical object-array form of the configured chain.</summary>
    public required string ServiceType { get; init; }

    /// <summary>Ordered candidates the chain resolves to, rendered as <c>custom (model-a)</c>.</summary>
    public required IReadOnlyList<string> Candidates { get; init; }

    /// <summary>True when the value could not yield a single candidate and the default provider applies.</summary>
    public required bool UsedDefaultFallback { get; init; }

    public required IReadOnlyList<TranslationChainValidationIssue> Issues { get; init; }

    /// <summary>
    /// True when the chain can be used exactly as configured: no issues and no fallback to the default
    /// provider. An empty value is therefore invalid even though it still translates through the
    /// compatibility default.
    /// </summary>
    public bool IsValid => Issues.Count == 0 && !UsedDefaultFallback;
}
