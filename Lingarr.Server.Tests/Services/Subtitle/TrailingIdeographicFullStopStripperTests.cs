using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class TrailingIdeographicFullStopStripperTests
{
    [Fact]
    public void Apply_NullOrEmpty_DoesNothing()
    {
        TrailingIdeographicFullStopStripper.Apply((IList<string>?)null);
        TrailingIdeographicFullStopStripper.Apply((IEnumerable<SubtitleItem>?)null);
        TrailingIdeographicFullStopStripper.Apply(new List<string>());
    }

    [Theory]
    [InlineData("我回来了。", "我回来了")]
    [InlineData("你好。我回来了。", "你好。我回来了")]
    [InlineData("我回来了", "我回来了")]
    [InlineData("Hello.", "Hello.")]
    [InlineData("你好？", "你好？")]
    [InlineData("你好！", "你好！")]
    [InlineData("你好……", "你好……")]
    [InlineData("你好。。。", "你好。。。")]
    [InlineData("。。", "。。")]
    [InlineData("。", "。")]
    [InlineData("我回来了。」", "我回来了。」")]
    [InlineData("我回来了。\"", "我回来了。\"")]
    [InlineData("{\\i1}我回来了。{\\i0}", "{\\i1}我回来了{\\i0}")]
    [InlineData("<i>我回来了。</i>", "<i>我回来了</i>")]
    [InlineData("我回来了。 ", "我回来了 ")]
    [InlineData("你好。\\N我回来了。", "你好。\\N我回来了")]
    public void Apply_Lines_StripsOnlyASingleTrailingIdeographicFullStop(string input, string expected)
    {
        List<string> lines = [input];

        TrailingIdeographicFullStopStripper.Apply(lines);

        Assert.Equal(expected, Assert.Single(lines));
    }

    [Fact]
    public void Apply_MultiLineCue_StripsOnlyTheLastVisiblePeriod()
    {
        List<string> lines = ["你好。", "我回来了。"];

        TrailingIdeographicFullStopStripper.Apply(lines);

        Assert.Equal(["你好。", "我回来了"], lines);
    }

    [Fact]
    public void Apply_LastLineEmpty_StripsPeriodOnPreviousLine()
    {
        List<string> lines = ["我回来了。", ""];

        TrailingIdeographicFullStopStripper.Apply(lines);

        Assert.Equal(["我回来了", ""], lines);
    }

    [Fact]
    public void Apply_SubtitleItems_OnlyMutatesTranslatedLines()
    {
        var item = new SubtitleItem
        {
            Lines = ["源。"],
            PlaintextLines = ["源。"],
            TranslatedLines = ["我回来了。"]
        };

        TrailingIdeographicFullStopStripper.Apply([item]);

        Assert.Equal(["源。"], item.Lines);
        Assert.Equal(["源。"], item.PlaintextLines);
        Assert.Equal(["我回来了"], item.TranslatedLines);
    }
}
