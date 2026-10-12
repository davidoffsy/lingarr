using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class FfmpegProgressParserTests
{
    [Theory]
    [InlineData("out_time_us=1000000", 1000000)]
    [InlineData("out_time_us=0", 0)]
    [InlineData("  out_time_us=5000000  ", 5000000)]
    public void TryParseOutTimeUs_ParsesMicroseconds(string line, long expected)
    {
        Assert.True(FfmpegProgressParser.TryParseOutTimeUs(line, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("progress=continue")]
    [InlineData("out_time_us=N/A")]
    [InlineData("out_time_us=-1")]
    public void TryParseOutTimeUs_IgnoresInvalidLines(string? line)
    {
        Assert.False(FfmpegProgressParser.TryParseOutTimeUs(line, out _));
    }

    [Fact]
    public void IsEnd_DetectsProgressEnd()
    {
        Assert.True(FfmpegProgressParser.IsEnd("progress=end"));
        Assert.True(FfmpegProgressParser.IsEnd(" progress=end "));
        Assert.False(FfmpegProgressParser.IsEnd("progress=continue"));
    }

    [Fact]
    public void ToPercent_MapsOutTimeAgainstDuration()
    {
        Assert.Equal(50, FfmpegProgressParser.ToPercent(5_000_000, 10));
        Assert.Equal(100, FfmpegProgressParser.ToPercent(20_000_000, 10));
        Assert.Equal(0, FfmpegProgressParser.ToPercent(0, 10));
        Assert.Null(FfmpegProgressParser.ToPercent(1_000_000, 0));
    }
}
