using Lingarr.Contracts.Models.Batch;

namespace Lingarr.Server.Services.Translation.Batch;

/// <summary>
/// Pure helpers for comparing a batch request with the translations that came back
/// and for splitting a missing list in half.
/// </summary>
public static class BatchCompleteness
{
    /// <summary>
    /// Returns requested items that have no usable translation in <paramref name="returned"/>.
    /// Extra keys are ignored. A present key with a null or whitespace value is a miss.
    /// </summary>
    public static List<BatchSubtitleItem> MissingItems(
        IReadOnlyList<BatchSubtitleItem> requested,
        IReadOnlyDictionary<int, string> returned)
    {
        var missing = new List<BatchSubtitleItem>();
        foreach (var item in requested)
        {
            if (!returned.TryGetValue(item.Position, out var value) || string.IsNullOrWhiteSpace(value))
            {
                missing.Add(item);
            }
        }

        return missing;
    }

    /// <summary>
    /// Splits <paramref name="items"/> into two lists. When the count is odd, the left list
    /// receives the extra item. An empty input yields two empty lists.
    /// </summary>
    public static (List<T> Left, List<T> Right) SplitInHalf<T>(IReadOnlyList<T> items)
    {
        var count = items.Count;
        var mid = (count + 1) / 2;
        var left = new List<T>(mid);
        var right = new List<T>(count - mid);
        for (var index = 0; index < count; index++)
        {
            if (index < mid)
            {
                left.Add(items[index]);
            }
            else
            {
                right.Add(items[index]);
            }
        }

        return (left, right);
    }

    /// <summary>
    /// Maximum fill-loop depth for a batch of <paramref name="batchSize"/>:
    /// <c>ceil(log2(max(batchSize, 1))) + 1</c>.
    /// </summary>
    public static int MaxDepth(int batchSize)
    {
        var size = Math.Max(batchSize, 1);
        return (int)Math.Ceiling(Math.Log2(size)) + 1;
    }
}
