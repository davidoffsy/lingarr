using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Media;

namespace Lingarr.Server.Services.Plex;

/// <summary>
/// Plex source selection. Does not read player <c>Stream.selected</c> and ignores MKV Default.
/// <see cref="Select"/> is local-only (configured language, then untagged sidecar, then unique
/// <c>und</c> embedded). <see cref="PlexRatingTrigger"/> inserts tagged Plex streams between
/// configured local sources and the untagged fallbacks.
/// </summary>
public static class PlexSourceSubtitleSelector
{
    public static PlexSourceSubtitleCandidate? Select(
        IReadOnlyList<PlexSourceSubtitleCandidate> candidates,
        IReadOnlyList<string> sourceLanguages,
        bool ignoreCaptions) =>
        SelectConfigured(candidates, sourceLanguages, ignoreCaptions)
        ?? SelectUntaggedSidecar(candidates, ignoreCaptions)
        ?? SelectUniqueUndeterminedEmbedded(candidates, ignoreCaptions);

    /// <summary>
    /// A candidate whose language is in the configured source list.
    /// Untagged sidecars and <c>und</c> tracks are skipped so tagged Plex OpenSubtitles
    /// can run before language detection.
    /// </summary>
    public static PlexSourceSubtitleCandidate? SelectConfigured(
        IReadOnlyList<PlexSourceSubtitleCandidate> candidates,
        IReadOnlyList<string> sourceLanguages,
        bool ignoreCaptions)
    {
        if (candidates == null || candidates.Count == 0 || sourceLanguages == null || sourceLanguages.Count == 0)
        {
            return null;
        }

        foreach (var language in DistinctSourceLanguages(sourceLanguages))
        {
            var selected = SelectFrom(
                candidates.Where(candidate =>
                    !IsUndetermined(candidate.Language)
                    && string.Equals(candidate.Language, language, StringComparison.OrdinalIgnoreCase)),
                ignoreCaptions);
            if (selected != null)
            {
                return selected;
            }
        }

        return null;
    }

    /// <summary>
    /// Untagged sidecars (<c>Movie.srt</c> next to <c>Movie.mp4</c>) are labeled unknown.
    /// </summary>
    public static PlexSourceSubtitleCandidate? SelectUntaggedSidecar(
        IReadOnlyList<PlexSourceSubtitleCandidate> candidates,
        bool ignoreCaptions)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        return SelectFrom(
            candidates.Where(candidate =>
                !candidate.IsEmbedded && IsUndetermined(candidate.Language)),
            ignoreCaptions);
    }

    private static PlexSourceSubtitleCandidate? SelectFrom(
        IEnumerable<PlexSourceSubtitleCandidate> matching,
        bool ignoreCaptions)
    {
        var ofLanguage = matching.ToList();
        if (ofLanguage.Count == 0)
        {
            return null;
        }

        if (ignoreCaptions)
        {
            var emptyCaption = Rank(ofLanguage.Where(candidate => string.IsNullOrEmpty(candidate.Caption)))
                .FirstOrDefault();
            if (emptyCaption != null)
            {
                return emptyCaption;
            }
        }

        return Rank(ofLanguage).FirstOrDefault();
    }

    /// <summary>
    /// Last-resort local fallback: a unique non-forced undetermined text track.
    /// Multiple full <c>und</c> tracks are skipped so we do not guess English vs Chinese.
    /// </summary>
    public static PlexSourceSubtitleCandidate? SelectUniqueUndeterminedEmbedded(
        IReadOnlyList<PlexSourceSubtitleCandidate> candidates,
        bool ignoreCaptions)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        var nonForced = candidates
            .Where(candidate =>
                candidate.IsEmbedded
                && IsUndetermined(candidate.Language)
                && !candidate.IsForced)
            .ToList();

        var fullDialogue = nonForced.Where(IsFullDialogue).ToList();
        if (fullDialogue.Count == 1)
        {
            return fullDialogue[0];
        }

        if (fullDialogue.Count > 1 || ignoreCaptions)
        {
            return null;
        }

        return nonForced.Count == 1 ? nonForced[0] : null;
    }

    private static IEnumerable<string> DistinctSourceLanguages(IReadOnlyList<string> sourceLanguages)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in sourceLanguages)
        {
            if (string.IsNullOrWhiteSpace(code) || IsUndetermined(code) || !seen.Add(code))
            {
                continue;
            }

            yield return code;
        }
    }

    private static IOrderedEnumerable<PlexSourceSubtitleCandidate> Rank(
        IEnumerable<PlexSourceSubtitleCandidate> candidates) =>
        SourceSubtitleRanker.Rank(candidates, candidate => new SourceSubtitleRank(
            candidate.IsEmbedded,
            candidate.IsForced,
            candidate.IsHearingImpaired,
            candidate.IsEmbedded ? candidate.TrackIndex : candidate.SidecarOrder));

    private static bool IsFullDialogue(PlexSourceSubtitleCandidate candidate) =>
        !candidate.IsForced
        && !candidate.IsHearingImpaired
        && string.IsNullOrEmpty(candidate.Caption);

    private static bool IsUndetermined(string language) =>
        string.Equals(language, EmbeddedSubtitleTrack.UnknownLanguage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, EmbeddedSubtitleTrack.UndeterminedLanguage, StringComparison.OrdinalIgnoreCase);
}
