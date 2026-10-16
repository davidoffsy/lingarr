using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Translation.Attribution;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

/// <summary>
/// Guards the order of the statistics and translator-info steps of <see cref="TranslationJob"/>: the
/// statistics must describe the same positions the attribution covers, so the intro line that
/// <c>AddTranslatorInfo</c> prepends to the subtitle file can never be counted as a translated line.
/// </summary>
public class TranslationJobTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _context;
    private readonly List<string> _calls = [];
    private readonly List<SubtitleItem> _statisticsSubtitles = [];
    private readonly List<SubtitleItem> _writtenSubtitles = [];
    private readonly TranslationRequest _request;
    private readonly TranslationJob _job;
    private TranslationSourceSummary? _statisticsSources;

    public TranslationJobTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new LingarrDbContext(options);
        _context.Database.EnsureCreated();

        _request = new TranslationRequest
        {
            Title = "Movie",
            SourceLanguage = "en",
            TargetLanguage = "es",
            SubtitleToTranslate = "movie.srt",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.Pending
        };
        _context.TranslationRequests.Add(_request);
        _context.SaveChanges();

        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock.Setup(service => service.ModelName).Returns("test-model");
        translationServiceMock
            .Setup(service => service.GetLanguagePair(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string source, string target, CancellationToken _) =>
                new LanguagePair { Source = source, Target = target, Tier = MatchTier.Exact });
        translationServiceMock
            .Setup(service => service.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, string _, List<string>? _, List<string>? _, CancellationToken _) =>
                $"line:{text}");

        var sourceSubtitles = new List<SubtitleItem> { NewSubtitle(0, "First"), NewSubtitle(1, "Second") };

        var subtitleServiceMock = new Mock<ISubtitleService>();
        subtitleServiceMock
            .Setup(service => service.ReadSubtitles(It.IsAny<string>()))
            .ReturnsAsync(() => sourceSubtitles);
        subtitleServiceMock
            .Setup(service => service.FixOverlappingSubtitles(It.IsAny<List<SubtitleItem>>()))
            .Returns((List<SubtitleItem> subtitles) => subtitles);
        subtitleServiceMock
            .Setup(service => service.CreateFilePath(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Returns("movie.es.srt");
        subtitleServiceMock
            .Setup(service => service.AddTranslatorInfo(
                It.IsAny<TranslationSourceSummary>(),
                It.IsAny<List<SubtitleItem>>()))
            .Callback((TranslationSourceSummary _, List<SubtitleItem> subtitles) =>
            {
                _calls.Add("AddTranslatorInfo");
                // Mirrors the real service: the intro is prepended to the written list.
                subtitles.Insert(0, NewSubtitle(0, "# Translated with Lingarr using Openai - test-model translator #"));
            });
        subtitleServiceMock
            .Setup(service => service.WriteSubtitles(
                It.IsAny<string>(),
                It.IsAny<List<SubtitleItem>>(),
                It.IsAny<bool>()))
            .Callback((string _, List<SubtitleItem> subtitles, bool _) =>
            {
                _calls.Add("WriteSubtitles");
                _writtenSubtitles.AddRange(subtitles);
            });

        var statisticsServiceMock = new Mock<IStatisticsService>();
        statisticsServiceMock
            .Setup(service => service.UpdateTranslationStatisticsFromSubtitles(
                It.IsAny<TranslationRequest>(),
                It.IsAny<TranslationSourceSummary>(),
                It.IsAny<List<SubtitleItem>>()))
            .Callback((TranslationRequest _, TranslationSourceSummary sources, List<SubtitleItem> subtitles) =>
            {
                _calls.Add("Statistics");
                _statisticsSources = sources;
                _statisticsSubtitles.AddRange(subtitles);
            })
            .ReturnsAsync(0);

        var translationRequestServiceMock = new Mock<ITranslationRequestService>();
        translationRequestServiceMock
            .Setup(service => service.UpdateTranslationRequest(
                It.IsAny<TranslationRequest>(),
                It.IsAny<TranslationStatus>(),
                It.IsAny<string?>()))
            .ReturnsAsync((TranslationRequest request, TranslationStatus status, string? _) =>
            {
                request.Status = status;
                return request;
            });

        var progressServiceMock = new Mock<IProgressService>();
        progressServiceMock
            .Setup(service => service.EmitLine(
                It.IsAny<TranslationRequest>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<LanguagePair?>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        var translationServiceFactoryMock = new Mock<ITranslationServiceFactory>();
        translationServiceFactoryMock
            .Setup(factory => factory.CreateTranslationServices(It.IsAny<IReadOnlyList<TranslationChainEntry>>()))
            .Returns(new List<TranslationServiceEntry>
            {
                new("openai", translationServiceMock.Object, null)
            });

        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.ServiceType, "[\"openai\"]" },
            { SettingKeys.Translation.FixOverlappingSubtitles, "false" },
            { SettingKeys.Translation.StripSubtitleFormatting, "false" },
            { SettingKeys.Translation.PreserveLineBreaks, "false" },
            { SettingKeys.Translation.AddTranslatorInfo, "true" },
            { SettingKeys.SubtitleValidation.ValidateSubtitles, "false" },
            { SettingKeys.Translation.AiContextBefore, "0" },
            { SettingKeys.Translation.AiContextAfter, "0" },
            { SettingKeys.Translation.AiContextUseTranslated, "false" },
            { SettingKeys.Translation.UseBatchTranslation, "false" },
            { SettingKeys.Translation.MaxBatchSize, "10000" },
            { SettingKeys.Translation.RemoveLanguageTag, "true" },
            { SettingKeys.Translation.UseSubtitleTagging, "false" },
            { SettingKeys.Translation.SubtitleTag, string.Empty }
        };
        var settingServiceMock = new Mock<ISettingService>();
        settingServiceMock
            .Setup(service => service.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(() => new Dictionary<string, string>(settings));

        _job = new TranslationJob(
            NullLogger<TranslationJob>.Instance,
            settingServiceMock.Object,
            _context,
            progressServiceMock.Object,
            subtitleServiceMock.Object,
            new Mock<IScheduleService>().Object,
            statisticsServiceMock.Object,
            translationServiceFactoryMock.Object,
            translationRequestServiceMock.Object,
            new Mock<ITranslationRequestEventService>().Object,
            new Mock<IEmbeddedSubtitleService>().Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private static SubtitleItem NewSubtitle(int position, string line) => new()
    {
        Position = position,
        Lines = [line],
        PlaintextLines = [line]
    };

    [Fact]
    public async Task Execute_AddTranslatorInfoEnabled_RecordsStatisticsBeforeTheIntroIsPrepended()
    {
        await _job.Execute(_request, CancellationToken.None);

        // The statistics must be recorded while the list still holds only translated lines.
        Assert.Equal(["Statistics", "AddTranslatorInfo", "WriteSubtitles"], _calls);

        Assert.Equal(2, _statisticsSubtitles.Count);
        Assert.DoesNotContain(_statisticsSubtitles, subtitle =>
            subtitle.Lines.Any(line => line.StartsWith("# Translated with Lingarr")));

        // Attribution covers both translated lines and nothing else.
        var sources = Assert.IsType<TranslationSourceSummary>(_statisticsSources);
        var count = Assert.Single(sources.Counts);
        Assert.Equal("openai", count.Source.Provider);
        Assert.Equal("test-model", count.Source.Model);
        Assert.Equal(2, count.Lines);

        // The intro still reaches the written subtitle file.
        Assert.Equal(3, _writtenSubtitles.Count);
        Assert.Contains(_writtenSubtitles, subtitle =>
            subtitle.Lines.Any(line => line.StartsWith("# Translated with Lingarr")));
        Assert.Equal(TranslationStatus.Completed, _request.Status);
    }
}
