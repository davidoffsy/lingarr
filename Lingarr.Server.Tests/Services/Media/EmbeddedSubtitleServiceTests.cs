using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class EmbeddedSubtitleServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly string _mkvPath;
    private readonly Mock<IExternalProcessRunner> _processRunner = new();
    private readonly EmbeddedSubtitleService _service;

    public EmbeddedSubtitleServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lingarr-mkv-embed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _mkvPath = Path.Combine(_directory, "Dune.2021.mkv");
        File.WriteAllBytes(_mkvPath, [0x1]);

        _service = new EmbeddedSubtitleService(
            _processRunner.Object,
            new LanguageCodeService(),
            NullLogger<EmbeddedSubtitleService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task ProbeAsync_KeepsTextTracksAndDropsImageAndWebVtt()
    {
        SetupProbe(Succeed("""
            {
              "streams": [
                {
                  "index": 2,
                  "codec_name": "subrip",
                  "disposition": { "default": 1, "forced": 0, "hearing_impaired": 0 },
                  "tags": { "language": "eng", "title": "English" }
                },
                {
                  "index": 3,
                  "codec_name": "ass",
                  "disposition": { "default": 0, "forced": 0, "hearing_impaired": 0 },
                  "tags": { "language": "eng" }
                },
                {
                  "index": 4,
                  "codec_name": "hdmv_pgs_subtitle",
                  "disposition": { "default": 0, "forced": 0, "hearing_impaired": 0 },
                  "tags": { "language": "eng" }
                },
                {
                  "index": 5,
                  "codec_name": "dvd_subtitle",
                  "tags": { "language": "eng" }
                },
                {
                  "index": 6,
                  "codec_name": "webvtt",
                  "tags": { "language": "eng" }
                },
                {
                  "index": 7,
                  "codec_name": "ssa",
                  "tags": { "language": "jpn" }
                }
              ]
            }
            """));

        var tracks = await _service.ProbeAsync(_mkvPath);

        Assert.Equal(3, tracks.Count);
        Assert.Collection(
            tracks,
            track =>
            {
                Assert.Equal(2, track.Index);
                Assert.Equal(".srt", track.Extension);
                Assert.Equal("en", track.Language);
                Assert.True(track.IsDefault);
                Assert.Equal("English", track.Title);
            },
            track =>
            {
                Assert.Equal(3, track.Index);
                Assert.Equal(".ass", track.Extension);
            },
            track =>
            {
                Assert.Equal(7, track.Index);
                Assert.Equal(".ssa", track.Extension);
                Assert.Equal("ja", track.Language);
            });
    }

    [Theory]
    [InlineData("eng", "en")]
    [InlineData("chi", "zh")]
    [InlineData("zho", "zh")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en-us")]
    [InlineData("und", "und")]
    [InlineData("xyzzy", "unknown")]
    [InlineData("", "unknown")]
    public async Task ProbeAsync_MapsLanguageTags(string mkvLanguage, string expected)
    {
        var languageJson = string.IsNullOrEmpty(mkvLanguage)
            ? "{}"
            : $$"""{ "language": "{{mkvLanguage}}" }""";

        SetupProbe(Succeed($$"""
            {
              "streams": [
                {
                  "index": 2,
                  "codec_name": "subrip",
                  "tags": {{languageJson}}
                }
              ]
            }
            """));

        var tracks = await _service.ProbeAsync(_mkvPath);

        var track = Assert.Single(tracks);
        Assert.Equal(expected, track.Language);
        Assert.Equal(expected is "unknown" or "und", !track.CanAutoTranslate);
    }

    [Fact]
    public async Task ProbeAsync_MapsForcedAndSdhCaptions()
    {
        SetupProbe(Succeed("""
            {
              "streams": [
                {
                  "index": 2,
                  "codec_name": "subrip",
                  "disposition": { "default": 0, "forced": 1, "hearing_impaired": 0 },
                  "tags": { "language": "eng" }
                },
                {
                  "index": 3,
                  "codec_name": "subrip",
                  "disposition": { "default": 0, "forced": 0, "hearing_impaired": 1 },
                  "tags": { "language": "eng" }
                }
              ]
            }
            """));

        var tracks = await _service.ProbeAsync(_mkvPath);

        Assert.Equal("forced", tracks[0].Caption);
        Assert.True(tracks[0].IsForced);
        Assert.Equal("sdh", tracks[1].Caption);
        Assert.True(tracks[1].IsHearingImpaired);
        Assert.Equal("Dune.2021.en.forced.srt", tracks[0].BuildSidecarFileName("Dune.2021"));
        Assert.Equal("Dune.2021.en.sdh.srt", tracks[1].BuildSidecarFileName("Dune.2021"));
    }

    [Fact]
    public async Task ProbeAsync_MissingFfprobe_ReturnsEmpty()
    {
        SetupProbe(ExternalProcessResult.NotFound("ffprobe"));

        var tracks = await _service.ProbeAsync(_mkvPath);

        Assert.Empty(tracks);
    }

    [Fact]
    public async Task ProbeAsync_Timeout_ReturnsEmpty()
    {
        SetupProbe(new ExternalProcessResult
        {
            FileName = "ffprobe",
            ExecutableFound = true,
            TimedOut = true,
            ExitCode = -1
        });

        var tracks = await _service.ProbeAsync(_mkvPath);

        Assert.Empty(tracks);
    }

    [Fact]
    public async Task ProbeAsync_NonMkvOrMissingFile_DoesNotRunFfprobe()
    {
        var tracks = await _service.ProbeAsync(Path.Combine(_directory, "movie.mp4"));
        Assert.Empty(tracks);

        tracks = await _service.ProbeAsync(Path.Combine(_directory, "missing.mkv"));
        Assert.Empty(tracks);

        _processRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProbeAsync_PassesFfprobeArgumentsWithoutAShell()
    {
        IReadOnlyList<string>? arguments = null;
        _processRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, TimeSpan, CancellationToken>((_, args, _, _) => arguments = args)
            .ReturnsAsync(Succeed("""{ "streams": [] }"""));

        await _service.ProbeAsync(_mkvPath);

        Assert.NotNull(arguments);
        Assert.Equal("-v", arguments[0]);
        Assert.Equal("error", arguments[1]);
        Assert.Equal("-select_streams", arguments[2]);
        Assert.Equal("s", arguments[3]);
        Assert.Equal("-show_entries", arguments[4]);
        Assert.Equal(
            "stream=index,codec_name:stream_tags=language,title:stream_disposition=default,forced,hearing_impaired",
            arguments[5]);
        Assert.Equal("-of", arguments[6]);
        Assert.Equal("json", arguments[7]);
        Assert.Equal(_mkvPath, arguments[8]);
    }

    [Fact]
    public async Task ExtractAsync_WritesSidecarAndValidatesNonEmptyOutput()
    {
        var track = new EmbeddedSubtitleTrack
        {
            Index = 2,
            CodecName = "subrip",
            Language = "en",
            Extension = ".srt"
        };

        SetupExtract(writeOutput: true, outputContent: "1\n00:00:01,000 --> 00:00:02,000\nHello\n");

        var outputPath = await _service.ExtractAsync(_mkvPath, track);

        var expected = Path.Combine(_directory, "Dune.2021.en.srt");
        Assert.Equal(expected, outputPath);
        Assert.True(File.Exists(expected));
        Assert.True(new FileInfo(expected).Length > 0);
    }

    [Fact]
    public async Task ExtractAsync_DoesNotOverwriteExistingSidecar()
    {
        var existing = Path.Combine(_directory, "Dune.2021.en.srt");
        File.WriteAllText(existing, "already extracted");

        var track = new EmbeddedSubtitleTrack
        {
            Index = 2,
            CodecName = "subrip",
            Language = "en",
            Extension = ".srt"
        };

        var outputPath = await _service.ExtractAsync(_mkvPath, track);

        Assert.Equal(existing, outputPath);
        Assert.Equal("already extracted", File.ReadAllText(existing));
        _processRunner.Verify(
            runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExtractAsync_EmptyOutput_ReturnsNull()
    {
        var track = new EmbeddedSubtitleTrack
        {
            Index = 2,
            CodecName = "subrip",
            Language = "en",
            Extension = ".srt"
        };

        SetupExtract(writeOutput: true, outputContent: string.Empty);

        var outputPath = await _service.ExtractAsync(_mkvPath, track);

        Assert.Null(outputPath);
        Assert.False(File.Exists(Path.Combine(_directory, "Dune.2021.en.srt")));
    }

    [Fact]
    public async Task ExtractAsync_MissingFfmpeg_ReturnsNull()
    {
        var track = new EmbeddedSubtitleTrack
        {
            Index = 2,
            CodecName = "subrip",
            Language = "en",
            Extension = ".srt"
        };

        _processRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExternalProcessResult.NotFound("ffmpeg"));

        var outputPath = await _service.ExtractAsync(_mkvPath, track);

        Assert.Null(outputPath);
    }

    [Fact]
    public async Task ExtractAsync_PassesCopyArgumentsForTheStreamIndex()
    {
        IReadOnlyList<string>? arguments = null;
        var track = new EmbeddedSubtitleTrack
        {
            Index = 4,
            CodecName = "ass",
            Language = "zh",
            Caption = "forced",
            Extension = ".ass"
        };

        _processRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<string>, TimeSpan, CancellationToken>((_, args, _, _) =>
            {
                arguments = args;
                File.WriteAllText(args[^1], "[Script Info]\n");
            })
            .ReturnsAsync(Succeed(string.Empty));

        var outputPath = await _service.ExtractAsync(_mkvPath, track);

        Assert.Equal(Path.Combine(_directory, "Dune.2021.zh.forced.ass"), outputPath);
        Assert.NotNull(arguments);
        Assert.Equal("-y", arguments[0]);
        Assert.Equal("-i", arguments[1]);
        Assert.Equal(_mkvPath, arguments[2]);
        Assert.Equal("-map", arguments[3]);
        Assert.Equal("0:4", arguments[4]);
        Assert.Equal("-c:s", arguments[5]);
        Assert.Equal("copy", arguments[6]);
        Assert.Equal(outputPath, arguments[7]);
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

    private void SetupExtract(bool writeOutput, string outputContent)
    {
        _processRunner
            .Setup(runner => runner.RunAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string fileName, IReadOnlyList<string> arguments, TimeSpan _, CancellationToken _) =>
            {
                if (writeOutput)
                {
                    File.WriteAllText(arguments[^1], outputContent);
                }

                return Succeed(string.Empty);
            });
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
