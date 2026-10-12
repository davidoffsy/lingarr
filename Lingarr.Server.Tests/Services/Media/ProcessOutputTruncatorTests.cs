using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class ProcessOutputTruncatorTests
{
    [Fact]
    public void Tail_ReturnsEmptyForNullOrBlank()
    {
        Assert.Equal(string.Empty, ProcessOutputTruncator.Tail(null));
        Assert.Equal(string.Empty, ProcessOutputTruncator.Tail("   "));
    }

    [Fact]
    public void Tail_KeepsShortText()
    {
        Assert.Equal("short error", ProcessOutputTruncator.Tail("  short error  "));
    }

    [Fact]
    public void Tail_KeepsTheEndOfLongFfmpegStderr()
    {
        var error = "Unable to choose an output format for 'episode.en.srt.extracting'; Invalid argument";
        var truncated = ProcessOutputTruncator.Tail(new string('A', 600) + error, error.Length + 3);

        Assert.Equal("..." + error, truncated);
        Assert.DoesNotContain("AAAA", truncated);
    }
}
