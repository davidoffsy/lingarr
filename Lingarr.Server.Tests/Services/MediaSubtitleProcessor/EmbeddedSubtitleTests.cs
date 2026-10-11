using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Models.FileSystem;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.MediaSubtitleProcessor;

public class EmbeddedSubtitleTests : MediaSubtitleProcessorTestBase
{
    private const string MkvPath = "/movies/test/test.movie.mkv";

    [Fact]
    public async Task ProcessMedia_ScanDisabled_DoesNotProbeEvenWhenNoSidecars()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SetupStandardSettings();

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.False(result);
        MediaFileLocatorMock.Verify(l => l.FindMkv(It.IsAny<IMedia>()), Times.Never);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ProbeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMedia_EmbeddedSource_ExtractsThenCreatesRequest()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = "en",
                    Extension = ".srt"
                }
            });
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ExtractAsync(MkvPath, It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/movies/test/test.movie.en.srt");

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.True(result);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.Is<TranslateAbleSubtitle>(t =>
                t.SourceLanguage == "en" &&
                t.TargetLanguage == "ro" &&
                t.SubtitlePath == "/movies/test/test.movie.en.srt" &&
                t.SubtitleFormat == ".srt")),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMedia_SidecarAndEmbeddedSameLanguage_DoesNotExtractOrDuplicate()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.movie.en.srt",
                    FileName = "test.movie.en",
                    Language = "en",
                    Caption = "",
                    Format = ".srt"
                }
            ]);
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = "en",
                    Extension = ".srt"
                }
            });

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.True(result);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()),
            Times.Never);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.Is<TranslateAbleSubtitle>(t =>
                t.SubtitlePath == "/movies/test/test.movie.en.srt")),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMedia_UndeterminedEmbeddedLanguage_DoesNotTranslate()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = EmbeddedSubtitleTrack.UndeterminedLanguage,
                    Extension = ".srt"
                }
            });

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.False(result);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMedia_TargetSidecarAlreadyExists_SkipsEmbeddedSource()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.movie.ro.srt",
                    FileName = "test.movie.ro",
                    Language = "ro",
                    Caption = "",
                    Format = ".srt"
                }
            ]);
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = "en",
                    Extension = ".srt"
                }
            });

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.False(result);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ExtractAsync(It.IsAny<string>(), It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMedia_IgnoreCaptions_SkipsForcedEmbeddedWhenRegularExists()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SetupStandardSettings(ignoreCaptions: "true");
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = "en",
                    Caption = "forced",
                    Extension = ".srt",
                    IsForced = true
                },
                new EmbeddedSubtitleTrack
                {
                    Index = 3,
                    CodecName = "subrip",
                    Language = "en",
                    Extension = ".srt",
                    IsDefault = true
                }
            });
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ExtractAsync(MkvPath, It.Is<EmbeddedSubtitleTrack>(t => t.Index == 3), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/movies/test/test.movie.en.srt");

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.True(result);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ExtractAsync(MkvPath, It.Is<EmbeddedSubtitleTrack>(t => t.Index == 3), It.IsAny<CancellationToken>()),
            Times.Once);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ExtractAsync(MkvPath, It.Is<EmbeddedSubtitleTrack>(t => t.Index == 2), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMedia_FfprobeMissing_StillProcessesSidecar()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(
            [
                new Subtitles
                {
                    Path = "/movies/test/test.movie.en.srt",
                    FileName = "test.movie.en",
                    Language = "en",
                    Caption = "",
                    Format = ".srt"
                }
            ]);
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Array.Empty<EmbeddedSubtitleTrack>());

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.True(result);
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.Is<TranslateAbleSubtitle>(t =>
                t.SubtitlePath == "/movies/test/test.movie.en.srt")),
            Times.Once);
    }

    [Fact]
    public async Task ProcessMedia_ExtractFailure_DoesNotCreateRequestOrPersistHash()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Subtitles>());
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = "en",
                    Extension = ".srt"
                }
            });
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ExtractAsync(MkvPath, It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await Processor.ProcessMedia(movie, MediaType.Movie);

        Assert.False(result);
        Assert.True(string.IsNullOrEmpty(movie.MediaHash));
        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessMedia_SameEmbeddedTracks_SecondRunShortCircuitsViaHash()
    {
        var movie = await CreateTestMovie();
        SubtitleServiceMock
            .Setup(s => s.GetSubtitles(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(() => Task.FromResult(new List<Subtitles>()));
        SetupStandardSettings();
        EnableEmbeddedScan();
        MediaFileLocatorMock.Setup(l => l.FindMkv(movie)).Returns(MkvPath);
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ProbeAsync(MkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new EmbeddedSubtitleTrack
                {
                    Index = 2,
                    CodecName = "subrip",
                    Language = "en",
                    Extension = ".srt"
                }
            });
        EmbeddedSubtitleServiceMock
            .Setup(s => s.ExtractAsync(MkvPath, It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("/movies/test/test.movie.en.srt");

        Assert.True(await Processor.ProcessMedia(movie, MediaType.Movie));
        Assert.False(await Processor.ProcessMedia(movie, MediaType.Movie));

        TranslationRequestServiceMock.Verify(
            s => s.CreateRequest(It.IsAny<TranslateAbleSubtitle>()),
            Times.Once);
        EmbeddedSubtitleServiceMock.Verify(
            s => s.ExtractAsync(MkvPath, It.IsAny<EmbeddedSubtitleTrack>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void EnableEmbeddedScan()
    {
        SettingServiceMock
            .Setup(s => s.GetSetting(SettingKeys.Translation.ScanEmbeddedSubtitles))
            .ReturnsAsync("true");
    }
}
