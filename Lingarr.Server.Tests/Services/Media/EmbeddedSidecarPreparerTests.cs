using System;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Media;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class EmbeddedSidecarPreparerTests
{
    private readonly Mock<IEmbeddedSubtitleService> _embedded = new();

    [Fact]
    public async Task EnsureAsync_NonPlaceholder_ReturnsOriginalWithoutProbing()
    {
        const string sidecar = "/data/movies/Dune.2021.en.srt";

        var result = await EmbeddedSidecarPreparer.EnsureAsync(
            _embedded.Object,
            sidecar,
            CancellationToken.None);

        Assert.Equal(sidecar, result);
        _embedded.Verify(
            service => service.ProbeDetailedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _embedded.Verify(
            service => service.ExtractDetailedAsync(
                It.IsAny<string>(),
                It.IsAny<EmbeddedSubtitleTrack>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                It.IsAny<double?>(),
                It.IsAny<TimeSpan?>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureAsync_Placeholder_ExtractsAndReturnsSidecar()
    {
        const string mkvPath = "/data/movies/Dune.2021.mkv";
        const string sidecar = "/data/movies/Dune.2021.en.srt";
        var track = new EmbeddedSubtitleTrack
        {
            Index = 3,
            CodecName = "subrip",
            Language = "en",
            Extension = ".srt"
        };

        _embedded
            .Setup(service => service.ProbeDetailedAsync(mkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmbeddedSubtitleProbeResult.Success([track], [], 12.5));
        _embedded
            .Setup(service => service.ExtractDetailedAsync(
                mkvPath,
                track,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                12.5,
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(EmbeddedSubtitleExtractResult.Ok(sidecar));

        var result = await EmbeddedSidecarPreparer.EnsureAsync(
            _embedded.Object,
            EmbeddedSubtitlePlaceholder.Create(mkvPath, 3),
            CancellationToken.None);

        Assert.Equal(sidecar, result);
        _embedded.Verify(
            service => service.ExtractDetailedAsync(
                mkvPath,
                track,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                12.5,
                It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureAsync_ExistingSidecar_DoesNotFailWhenExtractReturnsPath()
    {
        const string mkvPath = "/data/movies/Dune.2021.mkv";
        const string sidecar = "/data/movies/Dune.2021.en.srt";
        var track = new EmbeddedSubtitleTrack
        {
            Index = 3,
            Language = "en",
            Extension = ".srt"
        };

        _embedded
            .Setup(service => service.ProbeDetailedAsync(mkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmbeddedSubtitleProbeResult.Success([track], []));
        _embedded
            .Setup(service => service.ExtractDetailedAsync(
                mkvPath,
                track,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                It.IsAny<double?>()))
            .ReturnsAsync(EmbeddedSubtitleExtractResult.Ok(sidecar));

        var result = await EmbeddedSidecarPreparer.EnsureAsync(
            _embedded.Object,
            EmbeddedSubtitlePlaceholder.Create(mkvPath, 3),
            CancellationToken.None);

        Assert.Equal(sidecar, result);
    }

    [Fact]
    public async Task EnsureAsync_MissingTrack_Throws()
    {
        const string mkvPath = "/data/movies/Dune.2021.mkv";
        _embedded
            .Setup(service => service.ProbeDetailedAsync(mkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmbeddedSubtitleProbeResult.Success([], []));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EmbeddedSidecarPreparer.EnsureAsync(
                _embedded.Object,
                EmbeddedSubtitlePlaceholder.Create(mkvPath, 3),
                CancellationToken.None));

        Assert.Contains("no longer extractable", ex.Message);
        _embedded.Verify(
            service => service.ExtractDetailedAsync(
                It.IsAny<string>(),
                It.IsAny<EmbeddedSubtitleTrack>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                It.IsAny<double?>(),
                It.IsAny<TimeSpan?>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureAsync_ExtractFailure_ThrowsVisibleError()
    {
        const string mkvPath = "/data/movies/Dune.2021.mkv";
        var track = new EmbeddedSubtitleTrack { Index = 3, Language = "en", Extension = ".srt" };
        _embedded
            .Setup(service => service.ProbeDetailedAsync(mkvPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmbeddedSubtitleProbeResult.Success([track], []));
        _embedded
            .Setup(service => service.ExtractDetailedAsync(
                mkvPath,
                track,
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                It.IsAny<double?>()))
            .ReturnsAsync(EmbeddedSubtitleExtractResult.Fail(EmbeddedSubtitleExtractResult.TimedOut(TimeSpan.FromMinutes(60))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EmbeddedSidecarPreparer.EnsureAsync(
                _embedded.Object,
                EmbeddedSubtitlePlaceholder.Create(mkvPath, 3),
                CancellationToken.None));

        Assert.Equal(EmbeddedSubtitleExtractResult.TimedOut(TimeSpan.FromMinutes(60)), ex.Message);
    }

    [Fact]
    public async Task EnsureAsync_Cancellation_PropagatesWithoutExtractingWhenAlreadyCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EmbeddedSidecarPreparer.EnsureAsync(
                _embedded.Object,
                EmbeddedSubtitlePlaceholder.Create("/data/movies/Dune.2021.mkv", 3),
                cts.Token));

        _embedded.Verify(
            service => service.ExtractDetailedAsync(
                It.IsAny<string>(),
                It.IsAny<EmbeddedSubtitleTrack>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<IProgress<FfmpegExtractProgress>>(),
                It.IsAny<double?>(),
                It.IsAny<TimeSpan?>()),
            Times.Never);
    }
}
