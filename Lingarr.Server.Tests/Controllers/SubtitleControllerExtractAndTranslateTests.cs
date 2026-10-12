using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Media;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class SubtitleControllerExtractAndTranslateTests : IDisposable
{
    private readonly string _directory;
    private readonly string _mkvPath;
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _context;
    private readonly Mock<IExternalProcessRunner> _processRunner = new();
    private readonly Mock<ITranslationRequestService> _translationRequests = new();
    private readonly SubtitleController _controller;
    private readonly int _movieId;
    private TranslateAbleSubtitle? _created;

    public SubtitleControllerExtractAndTranslateTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lingarr-extract-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _mkvPath = Path.Combine(_directory, "Dune.2021.mkv");
        File.WriteAllBytes(_mkvPath, [0x1]);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new LingarrDbContext(options);
        _context.Database.EnsureCreated();

        _context.Movies.Add(new Movie
        {
            RadarrId = 12,
            Title = "Dune",
            FileName = "Dune.2021",
            Path = _directory,
            DateAdded = DateTime.UtcNow,
            IncludeInTranslation = true
        });
        _context.SaveChanges();
        _movieId = _context.Movies.First().Id;

        _translationRequests
            .Setup(service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()))
            .ReturnsAsync((TranslateAbleSubtitle request) =>
            {
                _created = request;
                return 42;
            });

        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(It.IsAny<string>())).ReturnsAsync((string?)null);
        var embeddedService = new EmbeddedSubtitleService(
            _processRunner.Object,
            new LanguageCodeService(),
            settings.Object,
            NullLogger<EmbeddedSubtitleService>.Instance);

        _controller = new SubtitleController(
            Mock.Of<ISubtitleService>(),
            _context,
            new MediaFileLocator(NullLogger<MediaFileLocator>.Instance),
            embeddedService,
            _translationRequests.Object,
            NullLogger<SubtitleController>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task ExtractAndTranslate_NoSidecar_EnqueuesPlaceholderWithoutExtracting()
    {
        SetupProbe(EnglishTrack());

        var result = await Extract(trackIndex: 3, sourceLanguage: "en", targetLanguage: "zh");

        Assert.Equal(42, result.JobId);
        Assert.NotNull(_created);
        Assert.Equal(_movieId, _created!.MediaId);
        Assert.Equal(MediaType.Movie, _created.MediaType);
        Assert.Equal("en", _created.SourceLanguage);
        Assert.Equal("zh", _created.TargetLanguage);
        Assert.Equal(EmbeddedSubtitlePlaceholder.Create(_mkvPath, 3), _created.SubtitlePath);
        AssertFfmpegWasNotRun();
    }

    [Fact]
    public async Task ExtractAndTranslate_ExistingSidecar_SkipsExtractAndUsesSidecarPath()
    {
        var sidecar = Path.Combine(_directory, "Dune.2021.en.srt");
        File.WriteAllText(sidecar, "already extracted");
        SetupProbe(EnglishTrack());

        var result = await Extract(trackIndex: 3, sourceLanguage: "ignored", targetLanguage: "zh");

        Assert.Equal(42, result.JobId);
        Assert.Equal(sidecar, _created!.SubtitlePath);
        Assert.Equal("en", _created.SourceLanguage);
        AssertFfmpegWasNotRun();
    }

    [Fact]
    public async Task ExtractAndTranslate_UndTrack_UsesUserSourceLanguage()
    {
        SetupProbe(TrackJson(3, "subrip", "und"));

        await Extract(trackIndex: 3, sourceLanguage: "en", targetLanguage: "zh");

        Assert.Equal("en", _created!.SourceLanguage);
        Assert.Equal(EmbeddedSubtitlePlaceholder.Create(_mkvPath, 3), _created.SubtitlePath);
    }

    [Fact]
    public async Task ExtractAndTranslate_UndTrackWithoutSourceLanguage_ReturnsBadRequest()
    {
        SetupProbe(TrackJson(3, "subrip", "und"));

        var action = await _controller.ExtractAndTranslate(new ExtractAndTranslateRequest
        {
            MediaId = _movieId,
            MediaType = MediaType.Movie,
            TrackIndex = 3,
            TargetLanguage = "zh"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        _translationRequests.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAndTranslate_UnknownTrack_IsNotExtractable()
    {
        SetupProbe(EnglishTrack());

        var action = await _controller.ExtractAndTranslate(new ExtractAndTranslateRequest
        {
            MediaId = _movieId,
            MediaType = MediaType.Movie,
            TrackIndex = 99,
            SourceLanguage = "en",
            TargetLanguage = "zh"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        _translationRequests.Verify(
            service => service.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAndTranslate_NoMkv_ReturnsBadRequest()
    {
        File.Delete(_mkvPath);

        var action = await _controller.ExtractAndTranslate(new ExtractAndTranslateRequest
        {
            MediaId = _movieId,
            MediaType = MediaType.Movie,
            TrackIndex = 3,
            SourceLanguage = "en",
            TargetLanguage = "zh"
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        _processRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private async Task<TranslationJobDto> Extract(int trackIndex, string sourceLanguage, string targetLanguage)
    {
        var action = await _controller.ExtractAndTranslate(new ExtractAndTranslateRequest
        {
            MediaId = _movieId,
            MediaType = MediaType.Movie,
            TrackIndex = trackIndex,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<TranslationJobDto>(ok.Value);
    }

    private void AssertFfmpegWasNotRun()
    {
        _processRunner.Verify(
            runner => runner.RunAsync(
                It.Is<string>(fileName =>
                    fileName.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase) &&
                    !fileName.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupProbe(string stdout)
    {
        _processRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalProcessResult
            {
                FileName = "ffprobe",
                ExecutableFound = true,
                TimedOut = false,
                ExitCode = 0,
                StandardOutput = stdout
            });
    }

    private static string EnglishTrack() => TrackJson(3, "subrip", "eng");

    private static string TrackJson(int index, string codec, string language) => $$"""
        {
          "streams": [
            {
              "index": {{index}},
              "codec_name": "{{codec}}",
              "tags": { "language": "{{language}}" }
            }
          ]
        }
        """;
}
