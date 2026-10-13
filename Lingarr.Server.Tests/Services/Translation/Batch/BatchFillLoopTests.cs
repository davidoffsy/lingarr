using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Translation;
using Lingarr.Server.Services.Translation.Batch;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Batch;

public class BatchFillLoopTests
{
    private static List<BatchSubtitleItem> Items(int count, int startPosition = 1) =>
        Enumerable.Range(startPosition, count)
            .Select(position => new BatchSubtitleItem { Position = position, Line = $"line-{position}" })
            .ToList();

    private static Dictionary<int, string> Translations(IEnumerable<BatchSubtitleItem> items) =>
        items.ToDictionary(item => item.Position, item => $"tr-{item.Position}");

    private static Mock<IBatchTranslationService> MockService(
        List<List<int>> calls,
        Func<List<BatchSubtitleItem>, Dictionary<int, string>> translate)
    {
        var mock = new Mock<IBatchTranslationService>();
        mock.Setup(service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> items, string _, string _, CancellationToken _) =>
            {
                calls.Add(items.Select(item => item.Position).ToList());
                return translate(items);
            });
        return mock;
    }

    [Fact]
    public async Task FillAsync_PartialThenHalvesSucceed_FillsAllInThreeCalls()
    {
        var calls = new List<List<int>>();
        var mock = MockService(calls, items =>
        {
            if (items.Count == 50)
            {
                return Translations(items.Take(30));
            }

            return Translations(items);
        });

        var result = await BatchFillLoop.FillAsync(
            mock.Object,
            Items(50),
            "en",
            "es",
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(3, calls.Count);
        Assert.Equal(Enumerable.Range(1, 50), calls[0]);
        Assert.Equal(Enumerable.Range(31, 10), calls[1]);
        Assert.Equal(Enumerable.Range(41, 10), calls[2]);
        Assert.Equal(50, result.Count);
        Assert.Equal(Enumerable.Range(1, 50), result.Keys.OrderBy(position => position));
    }

    [Fact]
    public async Task FillAsync_EmptyResultForMultipleItems_ThrowsWithoutSplitting()
    {
        var calls = new List<List<int>>();
        var mock = MockService(calls, _ => new Dictionary<int, string>());

        await Assert.ThrowsAnyAsync<TranslationException>(() =>
            BatchFillLoop.FillAsync(
                mock.Object,
                Items(5),
                "en",
                "es",
                NullLogger.Instance,
                CancellationToken.None));

        Assert.Single(calls);
        Assert.Equal(Enumerable.Range(1, 5), calls[0]);
    }

    [Fact]
    public async Task FillAsync_SingleRetryMiss_ReturnsHitsWithoutMissingPosition()
    {
        var calls = new List<List<int>>();
        var mock = MockService(calls, items =>
        {
            if (items.Count == 5)
            {
                return Translations(items.Take(4));
            }

            return new Dictionary<int, string>();
        });

        var result = await BatchFillLoop.FillAsync(
            mock.Object,
            Items(5),
            "en",
            "es",
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.Equal(Enumerable.Range(1, 5), calls[0]);
        Assert.Equal([5], calls[1]);
        Assert.Equal(4, result.Count);
        Assert.False(result.ContainsKey(5));
    }

    [Fact]
    public async Task FillAsync_HonorsCancellationToken()
    {
        var mock = new Mock<IBatchTranslationService>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BatchFillLoop.FillAsync(
                mock.Object,
                Items(3),
                "en",
                "es",
                NullLogger.Instance,
                cts.Token));

        mock.Verify(
            service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FillAsync_NestedAllMiss_ThrowsAndKeepsEarlierHits()
    {
        var calls = new List<List<int>>();
        var mock = MockService(calls, items =>
        {
            if (items.Count == 50)
            {
                return Translations(items.Take(30));
            }

            return new Dictionary<int, string>();
        });
        var accumulated = new Dictionary<int, string>();

        await Assert.ThrowsAnyAsync<TranslationException>(() =>
            BatchFillLoop.FillAsync(
                mock.Object,
                Items(50),
                "en",
                "es",
                NullLogger.Instance,
                CancellationToken.None,
                accumulated));

        Assert.Equal(3, calls.Count);
        Assert.Equal(Enumerable.Range(31, 10), calls[1]);
        Assert.Equal(Enumerable.Range(41, 10), calls[2]);
        Assert.Equal(30, accumulated.Count);
        Assert.Equal(Enumerable.Range(1, 30), accumulated.Keys.OrderBy(position => position));
    }

    [Fact]
    public async Task FillAsync_LeftHalfZeroHit_StillFillsRightHalf()
    {
        var calls = new List<List<int>>();
        var mock = MockService(calls, items =>
        {
            if (items.Count == 50)
            {
                return Translations(items.Take(30));
            }

            if (items[0].Position == 31)
            {
                return new Dictionary<int, string>();
            }

            return Translations(items);
        });
        var accumulated = new Dictionary<int, string>();

        await Assert.ThrowsAnyAsync<TranslationException>(() =>
            BatchFillLoop.FillAsync(
                mock.Object,
                Items(50),
                "en",
                "es",
                NullLogger.Instance,
                CancellationToken.None,
                accumulated));

        Assert.Equal(3, calls.Count);
        Assert.Equal(Enumerable.Range(31, 10), calls[1]);
        Assert.Equal(Enumerable.Range(41, 10), calls[2]);
        Assert.Equal(40, accumulated.Count);
        Assert.Equal(Enumerable.Range(1, 30).Concat(Enumerable.Range(41, 10)), accumulated.Keys.OrderBy(position => position));
    }

    [Fact]
    public async Task FillAsync_BlankReturnedValue_RetriesAsMiss()
    {
        var calls = new List<List<int>>();
        var mock = MockService(calls, items =>
        {
            if (items.Count == 2)
            {
                return new Dictionary<int, string>
                {
                    [1] = "tr-1",
                    [2] = "   "
                };
            }

            return Translations(items);
        });

        var result = await BatchFillLoop.FillAsync(
            mock.Object,
            Items(2),
            "en",
            "es",
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(2, calls.Count);
        Assert.Equal([1, 2], calls[0]);
        Assert.Equal([2], calls[1]);
        Assert.Equal("tr-1", result[1]);
        Assert.Equal("tr-2", result[2]);
    }

    [Fact]
    public async Task FillAsync_EmptyItems_ReturnsEmptyWithoutCallingService()
    {
        var mock = new Mock<IBatchTranslationService>();

        var result = await BatchFillLoop.FillAsync(
            mock.Object,
            [],
            "en",
            "es",
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Empty(result);
        mock.Verify(
            service => service.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
