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

public class SubtitleControllerProbeTests : IDisposable
{
    private readonly string _directory;
    private readonly string _mkvPath;
    private readonly SqliteConnection _connection;
    private readonly LingarrDbContext _context;
    private readonly Mock<IExternalProcessRunner> _processRunner = new();
    private readonly Mock<ISubtitleService> _subtitleService = new();
    private readonly SubtitleController _controller;
    private readonly int _movieId;
    private readonly int _episodeId;

    public SubtitleControllerProbeTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lingarr-probe-api-" + Guid.NewGuid().ToString("N"));
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

        var show = new Show
        {
            SonarrId = 1,
            Title = "Test Show",
            Path = _directory,
            DateAdded = DateTime.UtcNow
        };
        var season = new Season
        {
            SeasonNumber = 1,
            Path = _directory,
            Show = show
        };
        season.Episodes.Add(new Episode
        {
            SonarrId = 101,
            EpisodeNumber = 1,
            Title = "Pilot",
            FileName = "Dune.2021",
            Path = _directory,
            Season = season,
            DateAdded = DateTime.UtcNow
        });
        show.Seasons.Add(season);
        _context.Shows.Add(show);
        _context.SaveChanges();

        _movieId = _context.Movies.First().Id;
        _episodeId = _context.Episodes.First().Id;

        var settings = new Mock<ISettingService>();
        settings.Setup(service => service.GetSetting(It.IsAny<string>())).ReturnsAsync((string?)null);
        var embeddedService = new EmbeddedSubtitleService(
            _processRunner.Object,
            new LanguageCodeService(),
            settings.Object,
            NullLogger<EmbeddedSubtitleService>.Instance);

        _controller = new SubtitleController(
            _subtitleService.Object,
            _context,
            new MediaFileLocator(NullLogger<MediaFileLocator>.Instance),
            embeddedService,
            Mock.Of<ITranslationRequestService>(),
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
    public async Task Probe_NoMkv_ReturnsNotFoundReason()
    {
        File.Delete(_mkvPath);

        var result = await ProbeMovie();

        Assert.False(result.Found);
        Assert.Equal(EmbeddedSubtitleProbeResult.ReasonNoMkv, result.Reason);
        _processRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _subtitleService.Verify(
            service => service.GetAllSubtitles(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Probe_Timeout_ReturnsTimeoutReason()
    {
        SetupProbe(new ExternalProcessResult
        {
            FileName = "ffprobe",
            ExecutableFound = true,
            TimedOut = true,
            ExitCode = -1
        });

        var result = await ProbeMovie();

        Assert.False(result.Found);
        Assert.Equal(EmbeddedSubtitleProbeResult.ReasonTimeout, result.Reason);
        Assert.Equal("Dune.2021.mkv", result.FileName);
    }

    [Fact]
    public async Task Probe_MissingFfprobe_ReturnsNoFfprobeReason()
    {
        SetupProbe(ExternalProcessResult.NotFound("ffprobe"));

        var result = await ProbeMovie();

        Assert.False(result.Found);
        Assert.Equal(EmbeddedSubtitleProbeResult.ReasonNoFfprobe, result.Reason);
    }

    [Fact]
    public async Task Probe_FiltersPgsIntoSkippedAndKeepsTextTracks()
    {
        SetupProbe(Succeed("""
            {
              "streams": [
                {
                  "index": 3,
                  "codec_name": "subrip",
                  "disposition": { "default": 1, "forced": 0, "hearing_impaired": 0 },
                  "tags": { "language": "eng", "title": "English" }
                },
                {
                  "index": 4,
                  "codec_name": "hdmv_pgs_subtitle",
                  "tags": { "language": "eng" }
                },
                {
                  "index": 5,
                  "codec_name": "webvtt",
                  "tags": { "language": "eng" }
                }
              ]
            }
            """));

        var result = await ProbeMovie();

        Assert.True(result.Found);
        Assert.Null(result.Reason);
        Assert.Equal("Dune.2021.mkv", result.FileName);

        var track = Assert.Single(result.Tracks);
        Assert.Equal(3, track.Index);
        Assert.Equal("en", track.Language);
        Assert.Equal("subrip", track.CodecName);
        Assert.Equal(".srt", track.Extension);
        Assert.Equal("English", track.Title);
        Assert.True(track.IsDefault);
        Assert.True(track.CanAutoTranslate);
        Assert.True(track.Extractable);
        Assert.False(track.SidecarExists);

        Assert.Equal(2, result.Skipped.Count);
        Assert.Contains(result.Skipped, skipped =>
            skipped.Index == 4 &&
            skipped.CodecName == "hdmv_pgs_subtitle" &&
            skipped.Reason == EmbeddedSubtitleProbeResult.SkipReasonImage);
        Assert.Contains(result.Skipped, skipped =>
            skipped.Index == 5 &&
            skipped.CodecName == "webvtt" &&
            skipped.Reason == EmbeddedSubtitleProbeResult.SkipReasonUnsupported);
    }

    [Theory]
    [InlineData("eng", "en", true)]
    [InlineData("chi", "zh", true)]
    [InlineData("und", "und", false)]
    [InlineData("xyzzy", "unknown", false)]
    public async Task Probe_MapsLanguageTags(string mkvLanguage, string expected, bool canAutoTranslate)
    {
        SetupProbe(Succeed($$"""
            {
              "streams": [
                {
                  "index": 2,
                  "codec_name": "subrip",
                  "tags": { "language": "{{mkvLanguage}}" }
                }
              ]
            }
            """));

        var result = await ProbeMovie();

        var track = Assert.Single(result.Tracks);
        Assert.Equal(expected, track.Language);
        Assert.Equal(canAutoTranslate, track.CanAutoTranslate);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task Probe_MarksExistingSidecar()
    {
        File.WriteAllText(Path.Combine(_directory, "Dune.2021.en.srt"), "already extracted");
        SetupProbe(Succeed("""
            {
              "streams": [
                {
                  "index": 2,
                  "codec_name": "subrip",
                  "tags": { "language": "eng" }
                }
              ]
            }
            """));

        var result = await ProbeMovie();

        var track = Assert.Single(result.Tracks);
        Assert.True(track.SidecarExists);
    }

    [Fact]
    public async Task Probe_EmptyTextTracks_ReturnsFoundWithEmptyTracks()
    {
        SetupProbe(Succeed("""
            {
              "streams": [
                {
                  "index": 4,
                  "codec_name": "hdmv_pgs_subtitle"
                }
              ]
            }
            """));

        var result = await ProbeMovie();

        Assert.True(result.Found);
        Assert.Empty(result.Tracks);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("image", skipped.Reason);
    }

    [Fact]
    public async Task Probe_Episode_UsesLocatorAndReturnsTracks()
    {
        SetupProbe(Succeed("""
            {
              "streams": [
                {
                  "index": 2,
                  "codec_name": "ass",
                  "tags": { "language": "jpn" }
                }
              ]
            }
            """));

        var action = await _controller.Probe(new ProbeEmbeddedRequest
        {
            MediaId = _episodeId,
            MediaType = MediaType.Episode
        }, CancellationToken.None);

        var result = AssertOk(action);
        var track = Assert.Single(result.Tracks);
        Assert.Equal("ja", track.Language);
        Assert.Equal(".ass", track.Extension);
    }

    [Fact]
    public async Task Probe_UnknownMovie_ReturnsNotFound()
    {
        var action = await _controller.Probe(new ProbeEmbeddedRequest
        {
            MediaId = 99999,
            MediaType = MediaType.Movie
        }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(action.Result);
    }

    [Fact]
    public async Task Probe_UnsupportedMediaType_ReturnsBadRequest()
    {
        var action = await _controller.Probe(new ProbeEmbeddedRequest
        {
            MediaId = _movieId,
            MediaType = MediaType.Show
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

    private async Task<ProbeEmbeddedResponse> ProbeMovie()
    {
        var action = await _controller.Probe(new ProbeEmbeddedRequest
        {
            MediaId = _movieId,
            MediaType = MediaType.Movie
        }, CancellationToken.None);
        return AssertOk(action);
    }

    private static ProbeEmbeddedResponse AssertOk(ActionResult<ProbeEmbeddedResponse> action)
    {
        var ok = Assert.IsType<OkObjectResult>(action.Result);
        return Assert.IsType<ProbeEmbeddedResponse>(ok.Value);
    }

    private void SetupProbe(ExternalProcessResult result)
    {
        _processRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static ExternalProcessResult Succeed(string stdout) => new()
    {
        FileName = "ffprobe",
        ExecutableFound = true,
        TimedOut = false,
        ExitCode = 0,
        StandardOutput = stdout
    };
}
