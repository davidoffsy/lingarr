using System;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class FfmpegExtractCommandTests
{
    [Theory]
    [InlineData(".srt", "srt")]
    [InlineData(".SRT", "srt")]
    [InlineData(".ass", "ass")]
    [InlineData(".ssa", "ssa")]
    public void TryGetMuxer_MapsExtractableExtensions(string extension, string muxer)
    {
        Assert.True(FfmpegExtractCommand.TryGetMuxer(extension, out var mapped));
        Assert.Equal(muxer, mapped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".vtt")]
    [InlineData("srt")]
    public void TryGetMuxer_RejectsUnknownExtensions(string? extension)
    {
        Assert.False(FfmpegExtractCommand.TryGetMuxer(extension, out var muxer));
        Assert.Equal(string.Empty, muxer);
    }

    [Fact]
    public void BuildArguments_SetsMuxerForExtractingSuffix()
    {
        var arguments = FfmpegExtractCommand.BuildArguments(
            "/tv/show.mkv",
            2,
            "srt",
            "/tv/show.en.srt.extracting");

        Assert.Equal(
        [
            "-y",
            "-i", "/tv/show.mkv",
            "-map", "0:2",
            "-c:s", "copy",
            "-progress", "pipe:1",
            "-nostats",
            "-f", "srt",
            "/tv/show.en.srt.extracting"
        ], arguments);
    }

    [Fact]
    public async Task BuildArguments_ExtractingSuffix_SucceedsWithRealFfmpeg()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var runner = new ExternalProcessRunner(NullLogger<ExternalProcessRunner>.Instance);
        var version = await runner.RunAsync(
            FfmpegExecutable.Ffmpeg,
            ["-version"],
            TimeSpan.FromSeconds(10),
            cancellationToken);
        if (!version.ExecutableFound || !version.Succeeded)
        {
            Assert.Skip("ffmpeg was not found");
        }

        var directory = Path.Combine(Path.GetTempPath(), "lingarr-ffmpeg-extract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var srtPath = Path.Combine(directory, "source.srt");
            var mkvPath = Path.Combine(directory, "NCIS.S24E01.1080p.WEB.h264-ETHEL[EZTVx.to].mkv");
            var tempPath = Path.Combine(directory, "episode.en.srt.extracting");
            await File.WriteAllTextAsync(
                srtPath,
                "1\n00:00:00,000 --> 00:00:01,000\nHello world\n",
                cancellationToken);

            var mux = await runner.RunAsync(
                FfmpegExecutable.Ffmpeg,
                ["-y", "-f", "srt", "-i", srtPath, "-c", "copy", mkvPath],
                TimeSpan.FromSeconds(15),
                cancellationToken);
            Assert.True(mux.Succeeded, mux.StandardError);

            var extract = await runner.RunStreamingAsync(
                FfmpegExecutable.Ffmpeg,
                FfmpegExtractCommand.BuildArguments(mkvPath, 0, "srt", tempPath),
                TimeSpan.FromSeconds(15),
                onStandardOutputLine: null,
                cancellationToken);

            Assert.True(extract.Succeeded, extract.StandardError);
            Assert.Equal(0, extract.ExitCode);
            Assert.True(File.Exists(tempPath));
            Assert.True(new FileInfo(tempPath).Length > 0);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
