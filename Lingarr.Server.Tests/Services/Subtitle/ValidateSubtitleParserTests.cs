using System;
using System.IO;
using Lingarr.Server.Models;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class ValidateSubtitleParserTests : IDisposable
{
    private readonly string _directory;
    private readonly SubtitleService _service = new(
        NullLogger<SubtitleService>.Instance,
        new LanguageCodeService());

    public ValidateSubtitleParserTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lingarr-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void ValidateSubtitle_AssFile_UsesSsaParserInsteadOfSrt()
    {
        var path = Path.Combine(_directory, "movie.en.ass");
        File.WriteAllText(path, """
            [Script Info]
            Title: Test
            ScriptType: v4.00+

            [V4+ Styles]
            Format: Name, Fontname, Fontsize
            Style: Default,Arial,20

            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,Hello there
            Dialogue: 0,0:00:04.00,0:00:06.00,Default,,0,0,0,,Second line
            """);

        Assert.True(_service.ValidateSubtitle(path, PermissiveOptions()));
    }

    [Fact]
    public void ValidateSubtitle_SrtFile_StillValidates()
    {
        var path = Path.Combine(_directory, "movie.en.srt");
        File.WriteAllText(path, """
            1
            00:00:01,000 --> 00:00:03,000
            Hello there

            2
            00:00:04,000 --> 00:00:06,000
            Second line
            """);

        Assert.True(_service.ValidateSubtitle(path, PermissiveOptions()));
    }

    [Fact]
    public void ValidateSubtitle_UnsupportedExtension_ReturnsFalse()
    {
        var path = Path.Combine(_directory, "movie.en.vtt");
        File.WriteAllText(path, "WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHi\n");

        Assert.False(_service.ValidateSubtitle(path, PermissiveOptions()));
    }

    private static SubtitleValidationOptions PermissiveOptions() => new()
    {
        MaxFileSizeBytes = 1024 * 1024,
        MaxSubtitleLength = 500,
        MinSubtitleLength = 1,
        MinDurationMs = 1,
        MaxDurationSecs = 60,
        StripSubtitleFormatting = false
    };
}
