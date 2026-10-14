using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

/// <summary>
/// Plex-only source ranking. Does not read player <c>Stream.selected</c> and ignores MKV Default.
/// </summary>
public static class PlexSourceSubtitleSelector
{
    public static PlexSourceSubtitleCandidate? Select(
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
            var ofLanguage = candidates
                .Where(candidate =>
                    !IsUndetermined(candidate.Language)
                    && string.Equals(candidate.Language, language, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (ofLanguage.Count == 0)
            {
                continue;
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

        return null;
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
        candidates
            .OrderBy(candidate => candidate.IsEmbedded)
            .ThenBy(candidate => candidate.IsForced)
            .ThenBy(candidate => candidate.IsHearingImpaired)
            .ThenBy(candidate => candidate.IsEmbedded ? candidate.TrackIndex : candidate.SidecarOrder);

    private static bool IsUndetermined(string language) =>
        string.Equals(language, EmbeddedSubtitleTrack.UnknownLanguage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, EmbeddedSubtitleTrack.UndeterminedLanguage, StringComparison.OrdinalIgnoreCase);
}
