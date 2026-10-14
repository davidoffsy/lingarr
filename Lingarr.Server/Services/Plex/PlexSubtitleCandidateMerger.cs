using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Media;

namespace Lingarr.Server.Services.Plex;

/// <summary>
/// Combines sidecar files with extractable MKV text tracks for Plex ranking.
/// Same language + caption prefers the sidecar. Does not apply MKV Default-first order.
/// </summary>
public static class PlexSubtitleCandidateMerger
{
    public static List<PlexSourceSubtitleCandidate> Merge(
        IReadOnlyList<Subtitles> sidecars,
        IReadOnlyList<EmbeddedSubtitleTrack> tracks,
        string? mkvPath)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        ArgumentNullException.ThrowIfNull(tracks);

        var candidates = new List<PlexSourceSubtitleCandidate>(sidecars.Count + tracks.Count);
        var claimedBySidecar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < sidecars.Count; index++)
        {
            var sidecar = sidecars[index];
            claimedBySidecar.Add(EmbeddedSubtitleMerger.LanguageCaptionKey(sidecar.Language, sidecar.Caption));
            candidates.Add(FromSidecar(sidecar, index));
        }

        if (string.IsNullOrWhiteSpace(mkvPath))
        {
            return candidates;
        }

        foreach (var track in tracks.Where(track => track.CanAutoTranslate))
        {
            var key = EmbeddedSubtitleMerger.LanguageCaptionKey(track.Language, track.Caption);
            if (claimedBySidecar.Contains(key))
            {
                continue;
            }

            candidates.Add(FromTrack(track, mkvPath));
        }

        return candidates;
    }

    private static PlexSourceSubtitleCandidate FromSidecar(Subtitles sidecar, int order) => new()
    {
        Path = sidecar.Path,
        Language = sidecar.Language,
        Caption = sidecar.Caption ?? string.Empty,
        Format = sidecar.Format,
        IsEmbedded = false,
        TrackIndex = sidecar.TrackIndex ?? -1,
        SidecarOrder = order,
        IsForced = PlexSubtitleCaptionFlags.IsForced(sidecar.Caption),
        IsHearingImpaired = PlexSubtitleCaptionFlags.IsHearingImpaired(sidecar.Caption)
    };

    private static PlexSourceSubtitleCandidate FromTrack(EmbeddedSubtitleTrack track, string mkvPath) => new()
    {
        Path = mkvPath,
        Language = track.Language,
        Caption = track.Caption ?? string.Empty,
        Format = track.Extension,
        IsEmbedded = true,
        TrackIndex = track.Index,
        SidecarOrder = int.MaxValue,
        IsForced = PlexSubtitleCaptionFlags.IsForced(track.Caption, track.IsForced),
        IsHearingImpaired = PlexSubtitleCaptionFlags.IsHearingImpaired(track.Caption, track.IsHearingImpaired)
    };
}
