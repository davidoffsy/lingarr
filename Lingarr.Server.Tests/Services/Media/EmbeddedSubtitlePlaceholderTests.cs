using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class EmbeddedSubtitlePlaceholderTests
{
    [Fact]
    public void Create_FormatsPrefixPathAndIndex()
    {
        var placeholder = EmbeddedSubtitlePlaceholder.Create("/data/movies/Dune.2021.mkv", 3);

        Assert.Equal("embedded:/data/movies/Dune.2021.mkv:3", placeholder);
    }

    [Theory]
    [InlineData("embedded:/data/movies/Dune.2021.mkv:3", "/data/movies/Dune.2021.mkv", 3)]
    [InlineData(@"embedded:C:\Movies\Dune.2021.mkv:4", @"C:\Movies\Dune.2021.mkv", 4)]
    [InlineData("embedded:/tmp/Movie: Director's Cut.mkv:2", "/tmp/Movie: Director's Cut.mkv", 2)]
    public void TryParse_ReadsPathAndIndex(string value, string expectedPath, int expectedIndex)
    {
        Assert.True(EmbeddedSubtitlePlaceholder.TryParse(value, out var mkvPath, out var trackIndex));
        Assert.Equal(expectedPath, mkvPath);
        Assert.Equal(expectedIndex, trackIndex);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/data/movies/Dune.2021.en.srt")]
    [InlineData("embedded:")]
    [InlineData("embedded:/data/movies/Dune.2021.mkv")]
    [InlineData("embedded:/data/movies/Dune.2021.mkv:")]
    [InlineData("embedded:/data/movies/Dune.2021.mkv:-1")]
    [InlineData("embedded::3")]
    public void TryParse_RejectsNonPlaceholders(string? value)
    {
        Assert.False(EmbeddedSubtitlePlaceholder.TryParse(value, out var mkvPath, out var trackIndex));
        Assert.Equal(string.Empty, mkvPath);
        Assert.Equal(-1, trackIndex);
    }
}
