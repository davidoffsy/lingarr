using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Plex;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexSourceSubtitleSelectorTests
{
    private const string MkvPath = "/movies/Foo/Foo.mkv";
    private static readonly IReadOnlyList<string> EnglishSources = ["en"];

    [Fact]
    public void Select_SidecarAndEmbeddedSameLanguage_PrefersSidecar()
    {
        var selected = Select(
            sidecars: [Sidecar("/movies/Foo/Foo.en.srt", "en")],
            tracks: [Track(2, "en", isDefault: true)]);

        Assert.NotNull(selected);
        Assert.False(selected.IsEmbedded);
        Assert.Equal("/movies/Foo/Foo.en.srt", selected.Path);
    }

    [Fact]
    public void Select_OnlyEmbeddedDefaultForcedAndSdh_PicksNonForcedNonSdhNotDefault()
    {
        var selected = Select(
            sidecars: [],
            tracks:
            [
                Track(2, "en", isDefault: true, forced: true, caption: "forced"),
                Track(3, "en", forced: true, caption: "forced"),
                Track(4, "en", hearingImpaired: true, caption: "sdh"),
                Track(5, "en")
            ]);

        Assert.NotNull(selected);
        Assert.True(selected.IsEmbedded);
        Assert.Equal(5, selected.TrackIndex);
        Assert.False(selected.IsForced);
        Assert.False(selected.IsHearingImpaired);
    }

    [Fact]
    public void Select_IgnoreCaptions_PrefersEmptyCaption()
    {
        var selected = Select(
            sidecars:
            [
                Sidecar("/movies/Foo/Foo.en.sdh.srt", "en", caption: "sdh"),
                Sidecar("/movies/Foo/Foo.en.srt", "en")
            ],
            tracks: [],
            ignoreCaptions: true);

        Assert.NotNull(selected);
        Assert.Equal("/movies/Foo/Foo.en.srt", selected.Path);
        Assert.Equal(string.Empty, selected.Caption);
    }

    [Fact]
    public void Select_UndeterminedLanguage_IsNotSelected()
    {
        var selected = Select(
            sidecars: [],
            tracks:
            [
                Track(2, EmbeddedSubtitleTrack.UndeterminedLanguage),
                Track(3, EmbeddedSubtitleTrack.UnknownLanguage)
            ],
            sourceLanguages: ["en", "und", "unknown"]);

        Assert.Null(selected);
    }

    [Fact]
    public void Select_LanguageNotInSourceLanguages_IsNotSelected()
    {
        var selected = Select(
            sidecars: [Sidecar("/movies/Foo/Foo.fr.srt", "fr")],
            tracks: [Track(2, "ja")]);

        Assert.Null(selected);
    }

    [Fact]
    public void Select_RespectsSourceLanguageListOrder()
    {
        var selected = Select(
            sidecars:
            [
                Sidecar("/movies/Foo/Foo.en.srt", "en"),
                Sidecar("/movies/Foo/Foo.ja.srt", "ja")
            ],
            tracks: [],
            sourceLanguages: ["ja", "en"]);

        Assert.NotNull(selected);
        Assert.Equal("ja", selected.Language);
        Assert.Equal("/movies/Foo/Foo.ja.srt", selected.Path);
    }

    private static Lingarr.Server.Models.Plex.PlexSourceSubtitleCandidate? Select(
        IReadOnlyList<Subtitles> sidecars,
        IReadOnlyList<EmbeddedSubtitleTrack> tracks,
        IReadOnlyList<string>? sourceLanguages = null,
        bool ignoreCaptions = false)
    {
        var candidates = PlexSubtitleCandidateMerger.Merge(sidecars, tracks, MkvPath);
        return PlexSourceSubtitleSelector.Select(
            candidates,
            sourceLanguages ?? EnglishSources,
            ignoreCaptions);
    }

    private static Subtitles Sidecar(string path, string language, string caption = "") => new()
    {
        Path = path,
        FileName = System.IO.Path.GetFileNameWithoutExtension(path),
        Language = language,
        Caption = caption,
        Format = System.IO.Path.GetExtension(path)
    };

    private static EmbeddedSubtitleTrack Track(
        int index,
        string language,
        string caption = "",
        bool isDefault = false,
        bool forced = false,
        bool hearingImpaired = false) => new()
    {
        Index = index,
        CodecName = "subrip",
        Language = language,
        Caption = caption,
        Extension = ".srt",
        IsDefault = isDefault,
        IsForced = forced,
        IsHearingImpaired = hearingImpaired
    };
}
