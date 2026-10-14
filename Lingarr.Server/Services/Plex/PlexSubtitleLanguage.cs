using System.Globalization;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Plex;

internal static class PlexSubtitleLanguage
{
    public static string Normalize(LanguageCodeService languages, params string?[] candidates)
    {
        foreach (var raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var tag = raw.Trim();
            if (tag.Equals(EmbeddedSubtitleTrack.UndeterminedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return EmbeddedSubtitleTrack.UndeterminedLanguage;
            }

            // ISO 639-2/B Chinese is not indexed by CultureInfo (which uses 639-2/T "zho").
            if (tag.Equals("chi", StringComparison.OrdinalIgnoreCase))
            {
                tag = "zho";
            }

            if (!languages.Validate(tag))
            {
                continue;
            }

            try
            {
                var normalized = LanguageCodeService.GetNormalizedCode(tag);
                if (tag.Contains('-', StringComparison.Ordinal))
                {
                    return normalized;
                }

                var culture = CultureInfo.GetCultureInfo(normalized);
                var twoLetter = culture.TwoLetterISOLanguageName;
                if (!string.IsNullOrEmpty(twoLetter) &&
                    twoLetter.Length == 2 &&
                    !twoLetter.Equals("iv", StringComparison.OrdinalIgnoreCase))
                {
                    return twoLetter.ToLowerInvariant();
                }

                return normalized;
            }
            catch (Exception ex) when (ex is ArgumentException or CultureNotFoundException)
            {
                // Try the next candidate.
            }
        }

        return EmbeddedSubtitleTrack.UnknownLanguage;
    }
}
