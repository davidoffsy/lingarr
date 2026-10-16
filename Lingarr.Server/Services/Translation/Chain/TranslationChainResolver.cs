using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Applies the runtime policy on top of <see cref="TranslationChainParser"/>: turns a raw
/// <c>service_type</c> value into the ordered candidates the rest of the system uses.
/// <para>
/// The parser deliberately reports problems instead of deciding what to do with them; this module
/// makes the decision once for every read entry point:
/// </para>
/// <list type="bullet">
/// <item>an empty value uses the default provider and is reported as an
/// <see cref="TranslationChainIssueKind.EmptyChain"/> issue, so the settings validation can tell the
/// user their chain is not the one being used;</item>
/// <item>a value that is unreadable as a whole (malformed JSON, or no usable entry at all) also
/// falls back to the default provider, and says so through
/// <see cref="TranslationChainResolution.UsedDefaultFallback"/> and the issues;</item>
/// <item>a readable value keeps every usable candidate even when individual entries are bad, so one
/// broken entry can never rewrite a legitimate chain to the default provider.</item>
/// </list>
/// </summary>
public static class TranslationChainResolver
{
    /// <summary>Provider used when the configured value provides no usable candidate.</summary>
    public const string DefaultProvider = SettingKeys.Translation.DefaultServiceType;

    /// <summary>
    /// Reads <paramref name="raw"/> into ordered candidates. Never throws.
    /// </summary>
    public static TranslationChainResolution Resolve(string? raw, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new TranslationChainResolution(DefaultChain, [EmptyChainIssue], true);
        }

        var parsed = TranslationChainParser.Parse(raw);
        if (parsed.Entries.Count == 0)
        {
            LogIssues(parsed.Issues, logger);
            logger?.LogWarning(
                "service_type '{Value}' holds no usable chain entry; falling back to '{Default}'.",
                raw,
                DefaultProvider);
            return new TranslationChainResolution(DefaultChain, parsed.Issues, true);
        }

        LogIssues(parsed.Issues, logger);
        return new TranslationChainResolution(parsed.Entries, parsed.Issues, false);
    }

    /// <summary>
    /// Reads <paramref name="raw"/> into the canonical object-array form. Used when a legacy value is
    /// promoted at startup or applied from the environment.
    /// </summary>
    public static string Normalise(string? raw, ILogger? logger = null) =>
        TranslationChainSerializer.Serialize(Resolve(raw, logger).Entries);

    private static readonly IReadOnlyList<TranslationChainEntry> DefaultChain =
        [new TranslationChainEntry(DefaultProvider, null)];

    private static readonly TranslationChainIssue EmptyChainIssue = new(
        TranslationChainIssueKind.EmptyChain,
        -1,
        $"The translation chain is empty; the default provider '{DefaultProvider}' applies.");

    private static void LogIssues(IReadOnlyList<TranslationChainIssue> issues, ILogger? logger)
    {
        if (logger is null)
        {
            return;
        }

        foreach (var issue in issues)
        {
            logger.LogWarning(
                "service_type chain problem ({Kind}) at index {Index}: {Message}",
                issue.Kind,
                issue.Index,
                issue.Message);
        }
    }
}
