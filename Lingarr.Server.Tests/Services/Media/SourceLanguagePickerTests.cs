using System.Collections.Generic;
using Lingarr.Contracts.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class SourceLanguagePickerTests
{
    [Fact]
    public void PickConfigured_RespectsSourceLanguageListOrder()
    {
        var selected = SourceLanguagePicker.PickConfigured(
            ["en", "ja"],
            ["ja", "en"]);

        Assert.Equal("ja", selected);
    }

    [Fact]
    public void PickConfigured_SkipsUndeterminedConfiguredAndAvailableLanguages()
    {
        var selected = SourceLanguagePicker.PickConfigured(
            [EmbeddedSubtitleTrack.UndeterminedLanguage, "en"],
            [EmbeddedSubtitleTrack.UnknownLanguage, "EN"]);

        Assert.Equal("en", selected);
    }

    [Fact]
    public void PickConfigured_ReturnsNullWhenOnlyUndeterminedIsAvailable()
    {
        var selected = SourceLanguagePicker.PickConfigured(
            [EmbeddedSubtitleTrack.UnknownLanguage],
            ["en"]);

        Assert.Null(selected);
    }

    [Fact]
    public void CodesInOrder_PreservesFirstOccurrenceAndSkipsBlanks()
    {
        var codes = SourceLanguagePicker.CodesInOrder(
        [
            new SourceLanguage { Code = "ja", Name = "Japanese" },
            new SourceLanguage { Code = "JA", Name = "Japanese" },
            new SourceLanguage { Code = " ", Name = "blank" },
            new SourceLanguage { Code = "en", Name = "English" }
        ]);

        Assert.Equal(new List<string> { "ja", "en" }, codes);
    }
}
