using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation.Attribution;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class StatisticsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _context;
    private readonly StatisticsService _statisticsService;

    public StatisticsServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new LingarrDbContext(options);
        _context.Database.EnsureCreated();

        _statisticsService = new StatisticsService(_context, new Mock<ISettingService>().Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static TranslationRequest NewRequest() => new()
    {
        Title = "Movie",
        SourceLanguage = "en",
        TargetLanguage = "es",
        MediaType = MediaType.Movie,
        Status = TranslationStatus.Completed
    };

    private static List<SubtitleItem> NewSubtitles(int count, int lineLength = 4) =>
        Enumerable.Range(0, count).Select(position =>
        {
            var line = new string((char)('a' + position), lineLength);
            return new SubtitleItem
            {
                Position = position,
                Lines = [line],
                PlaintextLines = [line],
                TranslatedLines = [line]
            };
        }).ToList();

    private static TranslationSourceSummary SummaryOf(params TranslationSource[] sources) =>
        TranslationSourceSummary.From(sources);

    [Fact]
    public async Task UpdateTranslationStatisticsFromSubtitles_SingleSource_CountsFileOnceAndLinesPerSource()
    {
        var sources = SummaryOf(
            new TranslationSource("openai", "gpt-4o"),
            new TranslationSource("openai", "gpt-4o"),
            new TranslationSource("openai", "gpt-4o"));

        await _statisticsService.UpdateTranslationStatisticsFromSubtitles(NewRequest(), sources, NewSubtitles(3));

        var stats = await _statisticsService.GetStatistics();
        Assert.Equal(1, stats.TotalFilesTranslated);
        Assert.Equal(3, stats.TotalLinesTranslated);
        Assert.Equal(12, stats.TotalCharactersTranslated);
        Assert.Equal(1, stats.TranslationsByService["openai"]);
        Assert.Equal(1, stats.TranslationsByModel["openai:gpt-4o"]);
        Assert.Equal(3, stats.LinesBySource["openai:gpt-4o"]);
        Assert.Equal(1, stats.TranslationsByMediaType["Movie"]);
        Assert.Equal(1, stats.SubtitlesByLanguage["en:es"]);

        // The invariant the front end relies on: the per source line counts cover every counted line
        // and every counted file is attributed to exactly one source.
        Assert.Equal(stats.TotalLinesTranslated, stats.LinesBySource.Values.Sum());
        Assert.Equal(stats.TotalFilesTranslated, stats.TranslationsByService.Values.Sum());

        var daily = await _statisticsService.GetDailyStatistics();
        Assert.Equal(1, Assert.Single(daily).TranslationCount);
    }

    [Fact]
    public async Task UpdateTranslationStatisticsFromSubtitles_NoAttributedLines_LeavesEveryCounterUntouched()
    {
        // A resume where every position was already translated by an earlier run: subtitles were
        // written, but this run attributed no new line, so it must not count as a finished file.
        var updated = await _statisticsService.UpdateTranslationStatisticsFromSubtitles(
            NewRequest(), TranslationSourceSummary.Empty, NewSubtitles(2));

        Assert.Equal(0, updated);

        var stats = await _statisticsService.GetStatistics();
        Assert.Equal(0, stats.TotalFilesTranslated);
        Assert.Equal(0, stats.TotalLinesTranslated);
        Assert.Equal(0, stats.TotalCharactersTranslated);
        Assert.Empty(stats.TranslationsByService);
        Assert.Empty(stats.TranslationsByModel);
        Assert.Empty(stats.TranslationsByMediaType);
        Assert.Empty(stats.SubtitlesByLanguage);
        Assert.Empty(stats.LinesBySource);
        Assert.Empty(await _statisticsService.GetDailyStatistics());
    }

    [Fact]
    public async Task UpdateTranslationStatisticsFromSubtitles_MixedSources_CountsFileForDominantAndLinesForEverySource()
    {
        var sources = SummaryOf(
            new TranslationSource("openai", "gpt-4o"),
            new TranslationSource("openai", "gpt-4o"),
            new TranslationSource("deepl", null));

        await _statisticsService.UpdateTranslationStatisticsFromSubtitles(NewRequest(), sources, NewSubtitles(3));

        var stats = await _statisticsService.GetStatistics();
        Assert.Equal(1, stats.TotalFilesTranslated);
        Assert.Equal(3, stats.TotalLinesTranslated);

        // File counted once, under the dominant source only.
        Assert.Equal(1, stats.TranslationsByService["openai"]);
        Assert.False(stats.TranslationsByService.ContainsKey("deepl"));
        Assert.Equal(stats.TotalFilesTranslated, stats.TranslationsByService.Values.Sum());

        // Lines counted for both sources, including the one without a model.
        Assert.Equal(2, stats.LinesBySource["openai:gpt-4o"]);
        Assert.Equal(1, stats.LinesBySource["deepl"]);
        Assert.Equal(stats.TotalLinesTranslated, stats.LinesBySource.Values.Sum());
    }

    [Fact]
    public async Task UpdateTranslationStatisticsFromSubtitles_MultiLineCue_CountsOnePositionAndKeepsTheInvariant()
    {
        // Attribution is one source per subtitle position. A cue with two Lines must not make
        // TotalLinesTranslated (previously Sum(Lines.Count)) diverge from Sum(LinesBySource).
        var sources = SummaryOf(new TranslationSource("openai", "gpt-4o"));
        var subtitles = new List<SubtitleItem>
        {
            new()
            {
                Position = 0,
                Lines = ["hello", "world"],
                PlaintextLines = ["hello", "world"],
                TranslatedLines = ["hola", "mundo"]
            }
        };

        await _statisticsService.UpdateTranslationStatisticsFromSubtitles(NewRequest(), sources, subtitles);

        var stats = await _statisticsService.GetStatistics();
        Assert.Equal(1, stats.TotalLinesTranslated);
        Assert.Equal(1, stats.LinesBySource["openai:gpt-4o"]);
        Assert.Equal(stats.TotalLinesTranslated, stats.LinesBySource.Values.Sum());
        Assert.Equal(10, stats.TotalCharactersTranslated);
    }

    [Fact]
    public async Task UpdateTranslationStatisticsFromLines_NoAttributedLines_LeavesEveryCounterUntouched()
    {
        var updated = await _statisticsService.UpdateTranslationStatisticsFromLines(
            NewRequest(), TranslationSourceSummary.Empty, []);

        Assert.Equal(0, updated);

        var stats = await _statisticsService.GetStatistics();
        Assert.Equal(0, stats.TotalFilesTranslated);
        Assert.Equal(0, stats.TotalLinesTranslated);
        Assert.Empty(stats.LinesBySource);
        Assert.Empty(await _statisticsService.GetDailyStatistics());
    }

    [Fact]
    public async Task ResetStatistics_ClearsLinesBySource()
    {
        var sources = SummaryOf(new TranslationSource("openai", "gpt-4o"), new TranslationSource("deep", "l"));
        await _statisticsService.UpdateTranslationStatisticsFromSubtitles(NewRequest(), sources, NewSubtitles(2));
        Assert.NotEmpty((await _statisticsService.GetStatistics()).LinesBySource);

        await _statisticsService.ResetStatistics();

        var stats = await _statisticsService.GetStatistics();
        Assert.Empty(stats.LinesBySource);
        Assert.Empty(stats.TranslationsByService);
        Assert.Empty(stats.TranslationsByModel);
        Assert.Empty(stats.TranslationsByMediaType);
        Assert.Empty(stats.SubtitlesByLanguage);
        Assert.Equal(0, stats.TotalFilesTranslated);
        Assert.Equal(0, stats.TotalLinesTranslated);
        Assert.Equal(0, stats.TotalCharactersTranslated);
        Assert.Empty(await _statisticsService.GetDailyStatistics());
    }
}
