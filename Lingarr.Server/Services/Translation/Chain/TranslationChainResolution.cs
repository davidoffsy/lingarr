namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Ordered candidates the runtime should use for a raw <c>service_type</c> value, plus the problems
/// found on the way. <see cref="Entries"/> is never empty: an unusable value falls back to the
/// built-in default provider so every caller gets a usable chain, while
/// <see cref="UsedDefaultFallback"/> and <see cref="Issues"/> describe how that happened.
/// </summary>
/// <param name="Entries">Usable candidates in configured order.</param>
/// <param name="Issues">Problems found while reading the value; empty when it was read cleanly.</param>
/// <param name="UsedDefaultFallback">
/// True when the configured value could not provide a single usable candidate and the default
/// provider was substituted instead.
/// </param>
public sealed record TranslationChainResolution(
    IReadOnlyList<TranslationChainEntry> Entries,
    IReadOnlyList<TranslationChainIssue> Issues,
    bool UsedDefaultFallback)
{
    /// <summary>True when any problem was found, whether or not the chain is still usable.</summary>
    public bool HasIssues => Issues.Count > 0;
}
