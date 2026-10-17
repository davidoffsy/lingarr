using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Translation;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Proofread outcome: the (possibly unchanged) revision plus the candidate that produced it, so the
/// caller can attribute the line to the provider and model that really proofread it.
/// </summary>
/// <param name="Revision">Reviewed text returned by the provider.</param>
/// <param name="Entry">Candidate that returned <paramref name="Revision"/>.</param>
public readonly record struct ProofreadResult(string Revision, TranslationServiceEntry Entry);

/// <summary>
/// Runs proofread requests across the proofread-capable candidates of a translation chain, in
/// configured order, falling back to the next candidate when one fails. Candidates without proofread
/// support are ignored, and a candidate whose provider rejected the credentials skips the remaining
/// candidates of that provider.
/// </summary>
public static class ProofreadCandidateRunner
{
    /// <summary>Candidates of <paramref name="services"/> that can proofread, in configured order.</summary>
    public static IReadOnlyList<TranslationServiceEntry> Candidates(IReadOnlyList<TranslationServiceEntry> services) =>
        services.Where(entry => entry.Service is IProofreadService).ToList();

    /// <summary>
    /// Proofreads one line with the first candidate that answers, or returns null when no candidate
    /// produced a revision. Cancellation is always propagated, never turned into a fallback.
    /// Providers marked unusable (rejected credentials, unusable configuration) are skipped and
    /// added to <paramref name="unusableProviders"/> so a later line of the same job does not retry them.
    /// </summary>
    /// <param name="candidates">Proofread-capable candidates, in the order they should be tried.</param>
    /// <param name="sourceText">Source line the translation is reviewed against.</param>
    /// <param name="translatedText">Current translation of <paramref name="sourceText"/>.</param>
    /// <param name="sourceLanguage">Source language code.</param>
    /// <param name="targetLanguage">Target language code.</param>
    /// <param name="logger">Logger for fallback and failure diagnostics.</param>
    /// <param name="unusableProviders">
    /// Providers that already failed in a way that affects every model. The runner mutates this set.
    /// </param>
    /// <param name="cancellationToken">Token to support cancellation of the operation.</param>
    public static async Task<ProofreadResult?> ProofreadAsync(
        IReadOnlyList<TranslationServiceEntry> candidates,
        string sourceText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        ILogger logger,
        HashSet<string> unusableProviders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(unusableProviders);

        if (candidates.Count == 0)
        {
            return null;
        }

        Exception? lastError = null;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (unusableProviders.Contains(candidate.Name))
            {
                continue;
            }

            try
            {
                var revision = await ((IProofreadService)candidate.Service).ProofreadAsync(
                    sourceText,
                    translatedText,
                    sourceLanguage,
                    targetLanguage,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(revision))
                {
                    lastError = new TranslationException(
                        $"{candidate.Describe()} returned an empty proofread result.");
                    logger.LogWarning(
                        "Proofreading service {Service} returned an empty result.", candidate.Describe());
                    continue;
                }

                return new ProofreadResult(revision, candidate);
            }
            catch (TranslationAuthenticationException ex)
            {
                MarkProviderUnusable(unusableProviders, candidate, ex, logger,
                    "the provider rejected the configured credentials.");
                lastError = ex;
            }
            catch (TranslationConfigurationException ex)
            {
                MarkProviderUnusable(unusableProviders, candidate, ex, logger,
                    "the candidate's settings cannot produce a request (missing endpoint, model or an unusable address).");
                lastError = ex;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogWarning(ex, "Proofreading service {Service} failed.", candidate.Describe());
            }
        }

        logger.LogWarning(
            lastError,
            "All configured proofreading services failed for the requested line.");
        return null;
    }

    /// <summary>
    /// Marks a provider as unusable for the rest of the run: rejected credentials and unusable
    /// configuration affect every model of that provider, so retrying its other candidates cannot help.
    /// </summary>
    private static void MarkProviderUnusable(
        HashSet<string> unusableProviders,
        TranslationServiceEntry candidate,
        Exception failure,
        ILogger logger,
        string reason)
    {
        if (unusableProviders.Add(candidate.Name))
        {
            logger.LogWarning(
                failure,
                "Skipping the remaining {Provider} candidates: {Reason}",
                candidate.Describe(),
                reason);
        }
    }
}
