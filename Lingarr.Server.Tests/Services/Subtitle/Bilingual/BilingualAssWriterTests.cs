using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle.Bilingual;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle.Bilingual;

public class BilingualAssWriterTests
{
    [Fact]
    public async Task WriteAsync_SrtItems_EmitsV4HeaderLayerEventsAndTwoStyles()
    {
        var item = Cue(1000, 4000, ["Hello"], ["你好"]);

        var output = await Write(item);

        Assert.Contains("ScriptType: v4.00+", output);
        Assert.Contains("PlayResX: 1920", output);
        Assert.Contains("PlayResY: 1080", output);
        Assert.Contains("[V4+ Styles]", output);
        Assert.Contains("Style: LingarrTarget,", output);
        Assert.Contains("Style: LingarrSource,", output);
        Assert.Contains("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text", output);
        Assert.Contains("Dialogue: 0,0:00:01.00,0:00:04.00,LingarrTarget,,0,0,0,,", output);
        Assert.DoesNotContain("Format: Marked,", output);
    }

    [Fact]
    public async Task WriteAsync_AssSourceItems_UsesLingarrStylesNotSourceStyles()
    {
        var item = Cue(0, 2000, ["Hi"], ["你好"]);
        item.SsaFormat = new SsaFormat
        {
            ScriptInfo = ["[Script Info]", "PlayResY: 720"],
            Styles = ["[V4+ Styles]", "Style: Default,Arial,48,&H00FFFFFF"]
        };

        var output = await Write(item);

        Assert.Contains("Style: LingarrTarget,", output);
        Assert.Contains("PlayResY: 1080", output);
        Assert.DoesNotContain("Style: Default,Arial,48", output);
        Assert.DoesNotContain("PlayResY: 720", output);
    }

    [Fact]
    public async Task WriteAsync_UsesHardCodedFontSizesAndColors()
    {
        var output = await Write(Cue(0, 1000, ["A"], ["B"]));

        Assert.Contains("LingarrTarget,Noto Sans CJK SC,64,&H00FFFFFF", output);
        Assert.Contains("LingarrSource,Noto Sans CJK SC,38,&H0066D9FF", output);
        Assert.Contains("&H00000000", output);
        Assert.Contains(",2,1,2,20,20,16,1", output);
    }

    [Fact]
    public async Task WriteAsync_DoesNotMutateTranslatedOrPlaintextLines()
    {
        var plaintext = new List<string> { "Hello" };
        var translated = new List<string> { "你好" };
        var item = new SubtitleItem
        {
            StartTime = 0,
            EndTime = 1000,
            Lines = ["Hello"],
            PlaintextLines = plaintext,
            TranslatedLines = translated
        };

        await Write(item);

        Assert.Same(plaintext, item.PlaintextLines);
        Assert.Same(translated, item.TranslatedLines);
        Assert.Equal(["Hello"], item.PlaintextLines);
        Assert.Equal(["你好"], item.TranslatedLines);
    }

    [Fact]
    public async Task WriteAsync_EmptyAndDrawingCues_AreSkipped()
    {
        var empty = Cue(0, 500, ["   "], ["{\\p1}m 0 0 l 10 10{\\p0}"]);
        var real = Cue(1000, 2000, ["Hello"], ["你好"]);

        var output = await Write(empty, real);

        Assert.Contains("你好\\N{\\rLingarrSource}Hello", output);
        Assert.DoesNotContain("0:00:00.00,0:00:00.50", output);
        Assert.Equal(1, CountDialogues(output));
    }

    [Fact]
    public async Task WriteAsync_IntroCredit_EmittedOnceInTargetStyle()
    {
        const string credit = "# Translated with Lingarr using Openai - gpt-4o translator #";
        var intro = Cue(0, 5000, [credit], [credit]);
        var line = Cue(6000, 8000, ["Hello"], ["你好"]);

        var output = await Write(intro, line);

        Assert.Contains($"LingarrTarget,,0,0,0,,{credit}", output);
        Assert.DoesNotContain($"{credit}\\N{{\\rLingarrSource}}{credit}", output);
        Assert.Contains("你好\\N{\\rLingarrSource}Hello", output);
    }

    [Fact]
    public async Task WriteAsync_EscapesBraces()
    {
        var item = Cue(0, 1000, ["{hello}"], ["{你好}"]);

        var output = await Write(item);

        Assert.Contains("\\{你好\\}\\N{\\rLingarrSource}\\{hello\\}", output);
    }

    [Fact]
    public async Task WriteAsync_EmptyTarget_FallsBackToSourceStyle()
    {
        var output = await Write(Cue(0, 1000, ["Hello"], [""]));

        Assert.Contains("LingarrSource,,0,0,0,,Hello", output);
        Assert.DoesNotContain("LingarrTarget,,0,0,0,,Hello", output);
    }

    [Fact]
    public async Task WriteAsync_EmptySource_IsTargetOnly()
    {
        var output = await Write(Cue(0, 1000, [""], ["你好"]));

        Assert.Contains("LingarrTarget,,0,0,0,,你好", output);
        Assert.DoesNotContain("{\\rLingarrSource}", output);
    }

    [Fact]
    public async Task WriteAsync_DuplicateCue_IsSkipped()
    {
        var first = Cue(1000, 2000, ["Hello"], ["你好"]);
        var duplicate = Cue(1000, 2000, ["Hello"], ["你好"]);
        var next = Cue(3000, 4000, ["Bye"], ["再见"]);

        var output = await Write(first, duplicate, next);

        Assert.Equal(2, CountDialogues(output));
    }

    [Fact]
    public async Task WriteAsync_CollapsesLanguageLinesToSingleLine()
    {
        var item = Cue(0, 1000, ["Hello", "there"], ["你好", "啊"]);

        var output = await Write(item);

        Assert.Contains("你好 啊\\N{\\rLingarrSource}Hello there", output);
    }

    [Fact]
    public async Task WriteAsync_IgnoresClearedSourceStyles_StripFormattingIndependent()
    {
        var item = Cue(0, 1000, ["Hi"], ["你好"]);
        item.SsaFormat = new SsaFormat { Styles = [] };

        var output = await Write(item);

        Assert.Contains("Style: LingarrTarget,", output);
        Assert.Contains("Style: LingarrSource,", output);
    }

    private static SubtitleItem Cue(int start, int end, List<string> source, List<string> target) => new()
    {
        StartTime = start,
        EndTime = end,
        Lines = [..source],
        PlaintextLines = [..source],
        TranslatedLines = [..target]
    };

    private static async Task<string> Write(params SubtitleItem[] items)
    {
        using var stream = new MemoryStream();
        await new BilingualAssWriter().WriteAsync(stream, items);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static int CountDialogues(string output) =>
        output.Split('\n').Count(line => line.StartsWith("Dialogue:", StringComparison.Ordinal));
}
