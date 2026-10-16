using Lingarr.Contracts.Models;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Decides which candidates of a translation chain can serve a requested language pair, and in which
/// order they are tried.
/// <para>
/// Ordering rule: the best language match wins (exact before alias/script/neutral equivalents), and
/// candidates with the same match tier keep their configured order - so two models of the same
/// provider are tried in the order the user arranged them. Providers that do not support the pair are
/// skipped. Results are cached per language pair because a single translation run asks for the same
/// pair for every line.
/// </para>
/// <para>
/// Not thread-safe: one instance belongs to one translation run, which walks the candidates
/// sequentially.
/// </para>
/// </summary>
public sealed class TranslationCandidateScheduler
{
    private readonly IReadOnlyList<TranslationServiceEntry> _services;
    private readonly ILogger _logger;
    private readonly Dictionary<(string Source, string Target), IReadOnlyList<TranslationCandidate>> _byPair = [];
    private readonly HashSet<string> _loggedSkips = [];
    private readonly HashSet<TranslationCandidate> _loggedFallbacks = [];

    public TranslationCandidateScheduler(IReadOnlyList<TranslationServiceEntry> services, ILogger logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>Ordered candidates for a language pair; empty when no provider supports it.</summary>
    /// <param name="sourceLanguage">Requested source language code.</param>
    /// <param name="targetLanguage">Requested target language code.</param>
    /// <param name="batch">True when the caller needs batch capable candidates; used for logging only.</param>
    /// <param name="cancellationToken">Token to support cancellation of the operation.</param>
    public async Task<IReadOnlyList<TranslationCandidate>> GetCandidates(
        string sourceLanguage,
        string targetLanguage,
        bool batch,
        CancellationToken cancellationToken)
    {
        if (_byPair.TryGetValue((sourceLanguage, targetLanguage), out var cached))
        {
            return cached;
        }

        var candidates = new List<TranslationCandidate>(_services.Count);
        for (var index = 0; index < _services.Count; index++)
        {
            var entry = _services[index];
            var pair = await entry.Service.GetLanguagePair(sourceLanguage, targetLanguage, cancellationToken);
            if (pair is null)
            {
                if (_loggedSkips.Add(entry.Describe()))
                {
                    _logger.LogInformation(
                        "Skipping {Kind}translation service {Service}: no support for {Source}->{Target}.",
                        batch ? "batch " : string.Empty, entry.Describe(), sourceLanguage, targetLanguage);
                }
                continue;
            }

            candidates.Add(new TranslationCandidate(entry, pair, index));
        }

        var sorted = candidates
            .OrderBy(candidate => candidate.Pair.Tier)
            .ThenBy(candidate => candidate.ChainIndex)
            .ToList();

        _byPair[(sourceLanguage, targetLanguage)] = sorted;
        return sorted;
    }

    /// <summary>
    /// Logs once per candidate when a translation only succeeded through a non-exact language match.
    /// </summary>
    public void LogFallback(TranslationCandidate candidate, string requestedSource, string requestedTarget)
    {
        if (candidate.Pair.Tier == MatchTier.Exact)
        {
            return;
        }

        if (!_loggedFallbacks.Add(candidate))
        {
            return;
        }

        _logger.LogInformation(
            "Translation service {Service} matched {RequestedSource}->{RequestedTarget} as {MatchedSource}->{MatchedTarget} ({Tier}).",
            candidate.Entry.Describe(),
            requestedSource,
            requestedTarget,
            candidate.Pair.Source,
            candidate.Pair.Target,
            candidate.Pair.Tier);
    }
}
