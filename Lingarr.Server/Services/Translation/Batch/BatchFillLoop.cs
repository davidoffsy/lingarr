using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Translation;

namespace Lingarr.Server.Services.Translation.Batch;

/// <summary>
/// Calls <see cref="IBatchTranslationService.TranslateBatchAsync"/> until every requested
/// position is filled or the miss is terminal. Does not write original lines.
/// </summary>
public static class BatchFillLoop
{
    /// <summary>
    /// Translates <paramref name="items"/> and retries only the missing positions, splitting
    /// them in half until each remainder has size 1. Hits are written into
    /// <paramref name="accumulated"/> as soon as they arrive, including when a later request
    /// fails and this method throws.
    /// </summary>
    public static async Task<Dictionary<int, string>> FillAsync(
        IBatchTranslationService service,
        IReadOnlyList<BatchSubtitleItem> items,
        string sourceLanguage,
        string targetLanguage,
        ILogger logger,
        CancellationToken cancellationToken,
        Dictionary<int, string>? accumulated = null)
    {
        var filled = accumulated ?? new Dictionary<int, string>();
        if (items.Count == 0)
        {
            return filled;
        }

        await FillCore(
            service,
            items,
            sourceLanguage,
            targetLanguage,
            logger,
            cancellationToken,
            filled,
            depth: 1,
            originalBatchSize: items.Count);

        return filled;
    }

    private static async Task FillCore(
        IBatchTranslationService service,
        IReadOnlyList<BatchSubtitleItem> items,
        string sourceLanguage,
        string targetLanguage,
        ILogger logger,
        CancellationToken cancellationToken,
        Dictionary<int, string> filled,
        int depth,
        int originalBatchSize)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (items.Count == 0)
        {
            return;
        }

        var maxDepth = BatchCompleteness.MaxDepth(originalBatchSize);
        if (depth > maxDepth)
        {
            throw new TranslationException(
                $"Batch fill exceeded maximum depth {maxDepth} for batch size {originalBatchSize}.");
        }

        var request = items as List<BatchSubtitleItem> ?? items.ToList();
        var returned = await service.TranslateBatchAsync(
            request,
            sourceLanguage,
            targetLanguage,
            cancellationToken);

        var requestedPositions = new HashSet<int>(request.Count);
        foreach (var item in request)
        {
            requestedPositions.Add(item.Position);
        }

        var roundHits = new Dictionary<int, string>();
        foreach (var pair in returned)
        {
            if (!requestedPositions.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            roundHits[pair.Key] = pair.Value;
            filled[pair.Key] = pair.Value;
        }

        var missing = BatchCompleteness.MissingItems(request, roundHits);
        if (missing.Count == 0)
        {
            return;
        }

        if (roundHits.Count == 0)
        {
            if (request.Count == 1)
            {
                return;
            }

            logger.LogWarning(
                "Batch translation returned no hits for {Count} positions; treating service as failed.",
                request.Count);
            throw new BatchZeroHitException(request.Count);
        }

        if (missing.Count == 1)
        {
            logger.LogInformation(
                "Batch translation missed {MissingCount} of {RequestedCount} positions; retrying position {Position}.",
                missing.Count,
                request.Count,
                missing[0].Position);
            await FillCore(
                service,
                missing,
                sourceLanguage,
                targetLanguage,
                logger,
                cancellationToken,
                filled,
                depth + 1,
                originalBatchSize);
            return;
        }

        var (left, right) = BatchCompleteness.SplitInHalf(missing);
        logger.LogInformation(
            "Batch translation missed {MissingCount} of {RequestedCount} positions; splitting into {LeftCount} and {RightCount}.",
            missing.Count,
            request.Count,
            left.Count,
            right.Count);

        var pendingFailure = await FillHalf(
            service,
            left,
            sourceLanguage,
            targetLanguage,
            logger,
            cancellationToken,
            filled,
            depth + 1,
            originalBatchSize);
        var rightFailure = await FillHalf(
            service,
            right,
            sourceLanguage,
            targetLanguage,
            logger,
            cancellationToken,
            filled,
            depth + 1,
            originalBatchSize);
        var failure = pendingFailure ?? rightFailure;
        if (failure != null)
        {
            throw failure;
        }
    }

    /// <summary>
    /// Runs one half of a split. A translation failure (including a zero-hit) is returned
    /// instead of thrown so the sibling half still runs. Cancellation is not swallowed.
    /// </summary>
    private static async Task<TranslationException?> FillHalf(
        IBatchTranslationService service,
        IReadOnlyList<BatchSubtitleItem> items,
        string sourceLanguage,
        string targetLanguage,
        ILogger logger,
        CancellationToken cancellationToken,
        Dictionary<int, string> filled,
        int depth,
        int originalBatchSize)
    {
        try
        {
            await FillCore(
                service,
                items,
                sourceLanguage,
                targetLanguage,
                logger,
                cancellationToken,
                filled,
                depth,
                originalBatchSize);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TranslationException ex)
        {
            logger.LogWarning(
                ex,
                "Batch fill half of {Count} positions failed; continuing with the other half.",
                items.Count);
            return ex;
        }
    }
}
