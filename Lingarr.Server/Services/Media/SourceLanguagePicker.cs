using Lingarr.Contracts.Interfaces;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Media;

/// <summary>
/// Configured source-language priority. Settings list order wins; undetermined
/// codes are skipped so untagged sidecars stay a separate fallback.
/// </summary>
public static class SourceLanguagePicker
{
    public static bool IsUndetermined(string? language) =>
        string.Equals(language, EmbeddedSubtitleTrack.UnknownLanguage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, EmbeddedSubtitleTrack.UndeterminedLanguage, StringComparison.OrdinalIgnoreCase);

    public static string? PickConfigured(
        IEnumerable<string>? availableLanguages,
        IReadOnlyList<string>? sourceLanguages)
    {
        if (availableLanguages == null || sourceLanguages == null || sourceLanguages.Count == 0)
        {
            return null;
        }

        var canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in availableLanguages)
        {
            if (string.IsNullOrWhiteSpace(language) || IsUndetermined(language))
            {
                continue;
            }

            canonical.TryAdd(language, language);
        }

        foreach (var language in DistinctConfigured(sourceLanguages))
        {
            if (canonical.TryGetValue(language, out var matched))
            {
                return matched;
            }
        }

        return null;
    }

    /// <summary>
    /// Settings codes in list order, skipping blanks and case-insensitive duplicates.
    /// Does not drop <c>und</c> / <c>unknown</c> so target-language lists stay intact.
    /// </summary>
    public static List<string> CodesInOrder<T>(IEnumerable<T>? languages) where T : class, ILanguage
    {
        var codes = new List<string>();
        if (languages == null)
        {
            return codes;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in languages)
        {
            if (language == null || string.IsNullOrWhiteSpace(language.Code) || !seen.Add(language.Code))
            {
                continue;
            }

            codes.Add(language.Code);
        }

        return codes;
    }

    private static IEnumerable<string> DistinctConfigured(IReadOnlyList<string> sourceLanguages)
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
}
