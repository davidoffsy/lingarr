using Lingarr.Core;
using Xunit;

namespace Lingarr.Server.Tests.Core;

public class LingarrVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.1", "0.1.0", false)]
    [InlineData("0.1.0", "1.4.0", true)]
    public void Compare_StableVersions_DetectsNewerRelease(string current, string latest, bool expectedNew)
    {
        var info = LingarrVersion.Compare(current, latest);

        Assert.False(info.IsDevelopment);
        Assert.Equal(expectedNew, info.NewVersion);
        Assert.Equal(current, info.CurrentVersion);
        Assert.Equal(LingarrVersion.Normalize(latest), info.LatestVersion);
    }

    [Fact]
    public void Compare_DevelopmentBuild_DoesNotAdvertiseAnUpdate()
    {
        var info = LingarrVersion.Compare("0.0.0-dev", "0.1.0");

        Assert.True(info.IsDevelopment);
        Assert.False(info.NewVersion);
        Assert.Equal("0.0.0-dev", info.CurrentVersion);
        Assert.Equal("0.1.0", info.LatestVersion);
    }

    [Fact]
    public void Compare_MissingLatest_FallsBackToCurrent()
    {
        var info = LingarrVersion.Compare("0.1.0", null);

        Assert.False(info.NewVersion);
        Assert.Equal("0.1.0", info.CurrentVersion);
        Assert.Equal("0.1.0", info.LatestVersion);
    }

    [Theory]
    [InlineData("v0.1.0", "0.1.0")]
    [InlineData("V0.1.0", "0.1.0")]
    [InlineData(" 0.1.0 ", "0.1.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_StripsSemverPrefix(string? input, string? expected)
    {
        Assert.Equal(expected, LingarrVersion.Normalize(input));
    }
}
