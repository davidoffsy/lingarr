using Lingarr.Contracts.Models.Batch;

namespace Lingarr.Server.Services.Translation.Batch;

/// <summary>
/// Merges structured batch responses into the position → translated line dictionary
/// used by the translation pipeline. Duplicate positions are joined with newlines and
/// whitespace-only lines are dropped.
/// </summary>
public static class BatchResponseMerger
{
    public static Dictionary<int, string> MergeByPosition(List<StructuredBatchResponse> items) =>
        items
            .GroupBy(item => item.Position)
            .ToDictionary(
                group => group.Key,
                group => string.Join("\n", group.Select(i => i.Line).Where(l => !string.IsNullOrWhiteSpace(l))));
}
