using System;
using System.IO;
using Lingarr.Server.Services.Subtitle.Bilingual;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle.Bilingual;

public class BilingualAssPathTests
{
    [Theory]
    [InlineData("Movie.zh.srt", "Movie.zh.bilingual.ass")]
    [InlineData("Movie.zh.ass", "Movie.zh.bilingual.ass")]
    [InlineData("Movie.srt", "Movie.bilingual.ass")]
    [InlineData("Movie.zh.hi.srt", "Movie.zh.hi.bilingual.ass")]
    [InlineData("Movie.zh.lingarr.srt", "Movie.zh.lingarr.bilingual.ass")]
    public void FromMonolingual_AppendsBilingualAssSegment(string monolingual, string expectedFileName)
    {
        var path = BilingualAssPath.FromMonolingual(monolingual);

        Assert.Equal(expectedFileName, Path.GetFileName(path));
        Assert.NotEqual(monolingual, path);
    }

    [Fact]
    public void FromMonolingual_KeepsDirectory()
    {
        var monolingual = Path.Combine("media", "Movie.zh.srt");

        var path = BilingualAssPath.FromMonolingual(monolingual);

        Assert.Equal(Path.Combine("media", "Movie.zh.bilingual.ass"), path);
    }

    [Fact]
    public void FromMonolingual_NeverEqualsSourcePath()
    {
        const string source = "Movie.en.srt";
        const string monolingual = "Movie.zh.srt";

        var sidecar = BilingualAssPath.FromMonolingual(monolingual);

        Assert.NotEqual(source, sidecar);
        Assert.NotEqual(monolingual, sidecar);
    }

    [Theory]
    [InlineData("Movie.zh.bilingual.ass", true)]
    [InlineData("Movie.bilingual.ass", true)]
    [InlineData("Movie.ZH.BILINGUAL.ass", true)]
    [InlineData("Movie.zh.srt", false)]
    [InlineData("Movie.zh.ass", false)]
    [InlineData("bilingual.srt", true)]
    public void IsBilingualSidecar_FiltersOnFilenamePart(string fileName, bool expected)
    {
        Assert.Equal(expected, BilingualAssPath.IsBilingualSidecar(fileName));
    }

    [Fact]
    public void TempPath_AppendsTmpToAss()
    {
        Assert.Equal("Movie.zh.bilingual.ass.tmp", BilingualAssPath.TempPath("Movie.zh.bilingual.ass"));
    }
}
