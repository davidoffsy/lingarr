using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Media;

/// <summary>
/// Merges sidecar files with probed MKV text tracks. Matching language + caption
/// prefers the sidecar so the same dialogue is not translated twice.
/// </summary>
public static class EmbeddedSubtitleMerger
{
    public static List<Subtitles> Merge(
        IReadOnlyList<Subtitles> sidecars,
        IReadOnlyList<EmbeddedSubtitleTrack> tracks,
        string mkvPath)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentException.ThrowIfNullOrWhiteSpace(mkvPath);

        var merged = new List<Subtitles>(sidecars);
        var claimed = sidecars
            .Select(LanguageCaptionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks
                     .Where(track => track.CanAutoTranslate)
                     .OrderByDescending(track => track.IsDefault)
                     .ThenBy(track => track.IsForced)
                     .ThenBy(track => track.IsHearingImpaired)
                     .ThenBy(track => track.Index))
        {
            var key = LanguageCaptionKey(track);
            if (!claimed.Add(key))
            {
                continue;
            }

            merged.Add(ToSubtitles(track, mkvPath));
        }

        return merged;
    }

    public static Subtitles ToSubtitles(EmbeddedSubtitleTrack track, string mkvPath)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new Subtitles
        {
            Path = mkvPath,
            FileName = Path.GetFileNameWithoutExtension(mkvPath),
            Language = track.Language,
            Caption = track.Caption,
            Format = track.Extension,
            IsEmbedded = true,
            TrackIndex = track.Index,
            CodecName = track.CodecName
        };
    }

    public static EmbeddedSubtitleTrack ToTrack(Subtitles subtitle)
    {
        ArgumentNullException.ThrowIfNull(subtitle);

        return new EmbeddedSubtitleTrack
        {
            Index = subtitle.TrackIndex ?? -1,
            CodecName = subtitle.CodecName,
            Language = subtitle.Language,
            Caption = subtitle.Caption,
            Extension = subtitle.Format
        };
    }

    public static string Identity(Subtitles subtitle)
    {
        ArgumentNullException.ThrowIfNull(subtitle);

        if (subtitle.IsEmbedded)
        {
            return $"embedded:{subtitle.TrackIndex}:{subtitle.CodecName}:{subtitle.Language}:{subtitle.Caption}";
        }

        return subtitle.Path;
    }

    private static string LanguageCaptionKey(Subtitles subtitle) =>
        LanguageCaptionKey(subtitle.Language, subtitle.Caption);

    private static string LanguageCaptionKey(EmbeddedSubtitleTrack track) =>
        LanguageCaptionKey(track.Language, track.Caption);

    private static string LanguageCaptionKey(string language, string caption) =>
        $"{language.ToLowerInvariant()}\0{(caption ?? string.Empty).ToLowerInvariant()}";
}
