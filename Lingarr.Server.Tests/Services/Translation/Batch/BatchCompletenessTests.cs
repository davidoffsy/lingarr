using System;
using System.Collections.Generic;
using System.Linq;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Server.Services.Translation.Batch;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Batch;

public class BatchCompletenessTests
{
    private static List<BatchSubtitleItem> Items(int count, int startPosition = 1) =>
        Enumerable.Range(startPosition, count)
            .Select(position => new BatchSubtitleItem { Position = position, Line = $"line-{position}" })
            .ToList();

    private static Dictionary<int, string> Translations(IEnumerable<BatchSubtitleItem> items) =>
        items.ToDictionary(item => item.Position, item => $"tr-{item.Position}");

    [Fact]
    public void MissingItems_FullHit_ReturnsEmpty()
    {
        var requested = Items(50);
        var returned = Translations(requested);

        var missing = BatchCompleteness.MissingItems(requested, returned);

        Assert.Empty(missing);
    }

    [Fact]
    public void MissingItems_TailTwentyOfFifty_ReturnsThosePositions()
    {
        var requested = Items(50);
        var returned = Translations(requested.Take(30));

        var missing = BatchCompleteness.MissingItems(requested, returned);

        Assert.Equal(Enumerable.Range(31, 20), missing.Select(item => item.Position));
    }

    [Fact]
    public void MissingItems_IgnoresExtraReturnedKeys()
    {
        var requested = Items(3);
        var returned = new Dictionary<int, string>
        {
            [1] = "a",
            [2] = "b",
            [99] = "extra"
        };

        var missing = BatchCompleteness.MissingItems(requested, returned);

        Assert.Equal([3], missing.Select(item => item.Position));
    }

    [Fact]
    public void MissingItems_EmptyReturnedValueIsAMiss()
    {
        var requested = Items(1);
        var returned = new Dictionary<int, string> { [1] = string.Empty };

        var missing = BatchCompleteness.MissingItems(requested, returned);

        Assert.Equal([1], missing.Select(item => item.Position));
    }

    [Fact]
    public void MissingItems_WhitespaceReturnedValueIsAMiss()
    {
        var requested = Items(1);
        var returned = new Dictionary<int, string> { [1] = "  " };

        var missing = BatchCompleteness.MissingItems(requested, returned);

        Assert.Equal([1], missing.Select(item => item.Position));
    }

    [Fact]
    public void SplitInHalf_Twenty_SplitsTenAndTen()
    {
        var (left, right) = BatchCompleteness.SplitInHalf(Enumerable.Range(31, 20).ToList());

        Assert.Equal(Enumerable.Range(31, 10), left);
        Assert.Equal(Enumerable.Range(41, 10), right);
    }

    [Fact]
    public void SplitInHalf_Five_PutsExtraOnTheLeft()
    {
        var (left, right) = BatchCompleteness.SplitInHalf(Enumerable.Range(1, 5).ToList());

        Assert.Equal([1, 2, 3], left);
        Assert.Equal([4, 5], right);
    }

    [Fact]
    public void SplitInHalf_Empty_ReturnsTwoEmptyLists()
    {
        var (left, right) = BatchCompleteness.SplitInHalf(Array.Empty<int>());

        Assert.Empty(left);
        Assert.Empty(right);
    }

    [Fact]
    public void MaxDepth_MatchesCeilLog2PlusOne()
    {
        Assert.Equal(1, BatchCompleteness.MaxDepth(0));
        Assert.Equal(1, BatchCompleteness.MaxDepth(1));
        Assert.Equal(2, BatchCompleteness.MaxDepth(2));
        Assert.Equal(4, BatchCompleteness.MaxDepth(5));
        Assert.Equal(7, BatchCompleteness.MaxDepth(50));
        Assert.Equal(8, BatchCompleteness.MaxDepth(100));
    }
}
