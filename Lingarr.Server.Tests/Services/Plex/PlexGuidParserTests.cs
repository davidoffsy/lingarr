using Lingarr.Server.Services.Plex;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexGuidParserTests
{
    [Fact]
    public void Parse_StripsKnownPrefixes_AndIgnoresPlex()
    {
        var ids = PlexGuidParser.Parse([
            "plex://movie/5d776b9a594b2b001e6a6b9a",
            "tmdb://123",
            "imdb://tt1234567",
            "tvdb://456"
        ]);

        Assert.Equal("123", ids.TmdbId);
        Assert.Equal("tt1234567", ids.ImdbId);
        Assert.Equal("456", ids.TvdbId);
        Assert.True(ids.HasMovieIds);
        Assert.True(ids.HasShowIds);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_AndKeepsFirstMatch()
    {
        var ids = PlexGuidParser.Parse([
            "TMDB://111",
            "tmdb://222",
            "IMDB://tt0001",
            "TVDB://9"
        ]);

        Assert.Equal("111", ids.TmdbId);
        Assert.Equal("tt0001", ids.ImdbId);
        Assert.Equal("9", ids.TvdbId);
    }

    [Fact]
    public void Parse_EmptyOrUnknown_ReturnsNoIds()
    {
        var ids = PlexGuidParser.Parse([
            "plex://episode/abc",
            "",
            null,
            "unknown://1"
        ]);

        Assert.Null(ids.TmdbId);
        Assert.Null(ids.ImdbId);
        Assert.Null(ids.TvdbId);
        Assert.False(ids.HasMovieIds);
        Assert.False(ids.HasShowIds);
    }
}
