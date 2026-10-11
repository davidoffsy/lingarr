using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class ExternalProcessRunnerTests
{
    private readonly ExternalProcessRunner _runner = new(NullLogger<ExternalProcessRunner>.Instance);

    [Fact]
    public async Task RunAsync_MissingExecutable_ReturnsNotFoundWithoutThrowing()
    {
        var result = await _runner.RunAsync(
            "lingarr-missing-ffmpeg-binary",
            [],
            TimeSpan.FromSeconds(2));

        Assert.False(result.ExecutableFound);
        Assert.False(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_EmptyFileName_ReturnsNotFoundWithoutThrowing()
    {
        var result = await _runner.RunAsync("  ", [], TimeSpan.FromSeconds(2));

        Assert.False(result.ExecutableFound);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_SuccessfulCommand_CapturesStdoutAndExitCode()
    {
        SkipIfMissing("/bin/echo");

        var result = await _runner.RunAsync(
            "/bin/echo",
            ["hello"],
            TimeSpan.FromSeconds(5));

        Assert.True(result.ExecutableFound);
        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_DoesNotInterpretArgumentsThroughAShell()
    {
        SkipIfMissing("/bin/echo");

        var result = await _runner.RunAsync(
            "/bin/echo",
            ["hello world", ";", "echo", "pwned"],
            TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Equal("hello world ; echo pwned", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_IsCaptured()
    {
        SkipIfMissing("/bin/false");

        var result = await _runner.RunAsync(
            "/bin/false",
            [],
            TimeSpan.FromSeconds(5));

        Assert.True(result.ExecutableFound);
        Assert.False(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_Timeout_KillsProcessAndReturnsTimedOut()
    {
        SkipIfMissing("/bin/sleep");

        var result = await _runner.RunAsync(
            "/bin/sleep",
            ["5"],
            TimeSpan.FromMilliseconds(200));

        Assert.True(result.ExecutableFound);
        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task RunAsync_CallerCancellation_ThrowsOperationCanceledException()
    {
        SkipIfMissing("/bin/sleep");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync("/bin/sleep", ["5"], TimeSpan.FromSeconds(10), cts.Token));
    }

    [Fact]
    public async Task RunAsync_InvalidTimeout_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _runner.RunAsync("/bin/echo", [], TimeSpan.Zero));
    }

    [Fact]
    public void FfmpegExecutable_UsesPathOverridesWhenSet()
    {
        var previousFfmpeg = Environment.GetEnvironmentVariable(FfmpegExecutable.FfmpegPathVariable);
        var previousFfprobe = Environment.GetEnvironmentVariable(FfmpegExecutable.FfprobePathVariable);
        try
        {
            Environment.SetEnvironmentVariable(FfmpegExecutable.FfmpegPathVariable, null);
            Environment.SetEnvironmentVariable(FfmpegExecutable.FfprobePathVariable, null);
            Assert.Equal("ffmpeg", FfmpegExecutable.Ffmpeg);
            Assert.Equal("ffprobe", FfmpegExecutable.Ffprobe);

            Environment.SetEnvironmentVariable(FfmpegExecutable.FfmpegPathVariable, " /custom/ffmpeg ");
            Environment.SetEnvironmentVariable(FfmpegExecutable.FfprobePathVariable, "/custom/ffprobe");
            Assert.Equal("/custom/ffmpeg", FfmpegExecutable.Ffmpeg);
            Assert.Equal("/custom/ffprobe", FfmpegExecutable.Ffprobe);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FfmpegExecutable.FfmpegPathVariable, previousFfmpeg);
            Environment.SetEnvironmentVariable(FfmpegExecutable.FfprobePathVariable, previousFfprobe);
        }
    }

    private static void SkipIfMissing(string path)
    {
        if (!File.Exists(path))
        {
            Assert.Skip($"Required test executable was not found: {path}");
        }
    }
}
