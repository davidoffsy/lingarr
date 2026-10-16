namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Validates chain entries against the providers the server can actually build. The known-provider
/// set is passed in by the caller (the plugin registry, which holds built-in manifests and loaded
/// plugins) so this module never keeps a second copy of the provider list that could drift from the
/// factory.
/// Unknown, duplicated or unusable entries are reported as issues; nothing is thrown and no entry
/// is silently replaced.
/// </summary>
public static class TranslationChainValidator
{
    /// <summary>
    /// Validates <paramref name="entries"/> against <paramref name="knownProviders"/>
    /// (case-insensitive provider ids).
    /// </summary>
    /// <param name="entries">Ordered candidates to validate.</param>
    /// <param name="knownProviders">Providers the server can build, case-insensitive.</param>
    /// <param name="modelOverrideProviders">
    /// Providers that declare support for a per-candidate model override. When supplied, a candidate
    /// that overrides the model of any other provider is reported: the override would be silently
    /// ignored, which is exactly what the chain must not do.
    /// </param>
    public static IReadOnlyList<TranslationChainIssue> Validate(
        IReadOnlyList<TranslationChainEntry> entries,
        IReadOnlyCollection<string> knownProviders,
        IReadOnlyCollection<string>? modelOverrideProviders = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(knownProviders);

        var overrideCapable = modelOverrideProviders is null
            ? null
            : new HashSet<string>(modelOverrideProviders, StringComparer.OrdinalIgnoreCase);

        if (entries.Count == 0)
        {
            return
            [
                new TranslationChainIssue(
                    TranslationChainIssueKind.EmptyChain,
                    -1,
                    "The translation chain has no entries.")
            ];
        }

        var known = new HashSet<string>(knownProviders, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var issues = new List<TranslationChainIssue>();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var provider = entry.ProviderKey;

            if (provider.Length == 0)
            {
                issues.Add(new TranslationChainIssue(
                    TranslationChainIssueKind.EmptyProvider,
                    index,
                    "Chain entry has no provider identifier."));
                continue;
            }

            if (!known.Contains(provider))
            {
                issues.Add(new TranslationChainIssue(
                    TranslationChainIssueKind.UnknownProvider,
                    index,
                    $"Unknown translation provider '{entry.Provider}'.",
                    entry.Provider));
                continue;
            }

            var model = entry.ModelKey;
            if (model is not null && overrideCapable is not null && !overrideCapable.Contains(provider))
            {
                issues.Add(new TranslationChainIssue(
                    TranslationChainIssueKind.ModelOverrideNotSupported,
                    index,
                    $"Provider '{entry.Provider}' does not support a per-candidate model; remove the model override.",
                    model));
            }

            if (!seen.Add(TranslationChainIdentity.Key(provider, model)))
            {
                issues.Add(new TranslationChainIssue(
                    TranslationChainIssueKind.DuplicateProviderModel,
                    index,
                    model is null
                        ? $"Provider '{entry.Provider}' already appears earlier in the chain without a model."
                        : $"Provider '{entry.Provider}' with model '{model}' already appears earlier in the chain.",
                    model));
            }
        }

        return issues;
    }
}