namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Canonical identity of a chain candidate, shared by duplicate detection (factory) and validation so
/// the two can never disagree about what "the same candidate twice" means.
/// </summary>
public static class TranslationChainIdentity
{
    /// <summary>
    /// Separator between provider and model that cannot occur in either part, so joined keys stay
    /// unambiguous. Mirrored by the settings UI in <c>Lingarr.Client/src/ts/translationChain.ts</c>.
    /// </summary>
    public const char Separator = '\u001f';

    /// <summary>Comparable identity of a canonical provider key and an optional canonical model.</summary>
    public static string Key(string providerKey, string? modelKey) =>
        providerKey + Separator + (modelKey ?? string.Empty);

    /// <summary>Comparable identity of <paramref name="entry"/>.</summary>
    public static string Key(TranslationChainEntry entry) => Key(entry.ProviderKey, entry.ModelKey);
}