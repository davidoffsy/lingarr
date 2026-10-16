namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Result of reading a <c>service_type</c> value into chain entries. The parser never throws and
/// never substitutes a default provider: it reports what it could read and what it could not, and
/// the caller decides whether that is a fallback or a failure.
/// </summary>
/// <param name="Entries">Ordered candidates that could be read, each one a (provider, model) pair.</param>
/// <param name="Issues">Problems found while reading; empty when the value was read cleanly.</param>
public sealed record TranslationChainParseResult(
    IReadOnlyList<TranslationChainEntry> Entries,
    IReadOnlyList<TranslationChainIssue> Issues)
{
    /// <summary>Empty chain without issues: the setting was empty, not broken.</summary>
    public static readonly TranslationChainParseResult Empty = new([], []);

    public bool HasEntries => Entries.Count > 0;

    public bool HasIssues => Issues.Count > 0;
}