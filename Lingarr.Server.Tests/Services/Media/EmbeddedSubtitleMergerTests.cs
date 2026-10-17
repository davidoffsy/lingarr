using System.Collections.Generic;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class EmbeddedSubtitleMergerTests
{
    private const string MkvPath = "/movies/Dune.2021.mkv";

    [Fact]
    public void Merge_PrefersSidecarOverEmbeddedWithSameLanguageAndCaption()
    {
        var sidecars = new List<Subtitles>
        {
            Sidecar("/movies/Dune.2021.en.srt", "en")
        };
        var tracks = new[]
        {
            Track(2, "en", "subrip", ".srt")
        };

        var merged = EmbeddedSubtitleMerger.Merge(sidecars, tracks, MkvPath);

        var subtitle = Assert.Single(merged);
        Assert.False(subtitle.IsEmbedded);
        Assert.Equal("/movies/Dune.2021.en.srt", subtitle.Path);
    }

    [Fact]
    public void Merge_KeepsEmbeddedWhenCaptionDiffers()
    {
        var sidecars = new List<Subtitles>
        {
            Sidecar("/movies/Dune.2021.en.srt", "en")
        };
        var tracks = new[]
        {
            Track(3, "en", "subrip", ".srt", caption: "forced", forced: true)
        };

        var merged = EmbeddedSubtitleMerger.Merge(sidecars, tracks, MkvPath);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, s => !s.IsEmbedded && s.Caption == "");
        Assert.Contains(merged, s => s.IsEmbedded && s.Caption == "forced" && s.TrackIndex == 3);
    }

    [Fact]
    public void Merge_SkipsUndeterminedAndUnknownLanguages()
    {
        var tracks = new[]
        {
            Track(2, EmbeddedSubtitleTrack.UndeterminedLanguage, "subrip", ".srt"),
            Track(3, EmbeddedSubtitleTrack.UnknownLanguage, "ass", ".ass"),
            Track(4, "en", "subrip", ".srt")
        };

        var merged = EmbeddedSubtitleMerger.Merge([], tracks, MkvPath);

        var subtitle = Assert.Single(merged);
        Assert.Equal("en", subtitle.Language);
        Assert.True(subtitle.IsEmbedded);
        Assert.Equal(4, subtitle.TrackIndex);
    }

    [Fact]
    public void Merge_KeepsLowerIndexWhenLanguageAndCaptionMatch()
    {
        var tracks = new[]
        {
            Track(5, "en", "subrip", ".srt", isDefault: true),
            Track(2, "en", "subrip", ".srt")
        };

        var merged = EmbeddedSubtitleMerger.Merge([], tracks, MkvPath);

        var subtitle = Assert.Single(merged);
        Assert.Equal(2, subtitle.TrackIndex);
        Assert.True(subtitle.IsEmbedded);
        Assert.Equal("subrip", subtitle.CodecName);
        Assert.Equal(MkvPath, subtitle.Path);
    }

    [Fact]
    public void Merge_KeepsCleanAndSdhWhenDefaultSdhHasADifferentCaption()
    {
        var tracks = new[]
        {
            Track(2, "en", "subrip", ".srt", caption: "sdh", isDefault: true),
            Track(3, "en", "subrip", ".srt")
        };

        var merged = EmbeddedSubtitleMerger.Merge([], tracks, MkvPath);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, subtitle => subtitle.TrackIndex == 2 && subtitle.Caption == "sdh");
        Assert.Contains(merged, subtitle => subtitle.TrackIndex == 3 && subtitle.Caption == "");
    }

    [Fact]
    public void Identity_UsesIndexCodecAndLanguageForEmbeddedTracks()
    {
        var embedded = EmbeddedSubtitleMerger.ToSubtitles(Track(2, "en", "subrip", ".srt"), MkvPath);
        var sidecar = Sidecar("/movies/Dune.2021.en.srt", "en");

        Assert.Equal("embedded:2:subrip:en:", EmbeddedSubtitleMerger.Identity(embedded));
        Assert.Equal("/movies/Dune.2021.en.srt", EmbeddedSubtitleMerger.Identity(sidecar));
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
        string codec,
        string extension,
        string caption = "",
        bool isDefault = false,
        bool forced = false) => new()
    {
        Index = index,
        CodecName = codec,
        Language = language,
        Caption = caption,
        Extension = extension,
        IsDefault = isDefault,
        IsForced = forced
    };
}
