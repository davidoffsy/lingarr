using System.Text.RegularExpressions;

namespace Lingarr.Server.Services.Subtitle.Bilingual;

/// <summary>
/// Strips ASS override tags and drawings, then escapes braces for Dialogue text.
/// Does not mutate the source <see cref="Models.FileSystem.SubtitleItem"/> lists.
/// </summary>
internal static class BilingualAssText
{
    private static readonly Regex DrawingTagPattern = new(
        @"\{\\[pP][0-9]+\}.*?\{\\[pP][0-9]+\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // ASS overrides are `{\` ... `}`. Literal braces in dialogue are escaped later.
    private static readonly Regex OverrideTagPattern = new(
        @"\{\\[^}]*\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTagPattern = new(
        @"<.*?>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex VectorPrefixPattern = new(
        @"^[mlcbsnMLCBSN](?:\s+(?:[mlcbsnMLCBSN]|[+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)){2,}(?=\s|$)",
        RegexOptions.Compiled);

    private static readonly string[] LineBreaks = ["\\N", "\\n", "\r\n", "\n", "\r"];

    public static IReadOnlyList<string> CleanLines(IEnumerable<string>? lines)
    {
        var cleaned = new List<string>();
        if (lines == null)
        {
            return cleaned;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var stripped = StripTagsAndDrawings(line);
            foreach (var part in stripped.Split(LineBreaks, StringSplitOptions.None))
            {
                var trimmed = part.Trim();
                if (trimmed.Length == 0 || VectorPrefixPattern.IsMatch(trimmed))
                {
                    continue;
                }

                cleaned.Add(trimmed);
            }
        }

        return cleaned;
    }

    /// <summary>
    /// Collapses a language's lines into one ASS line. Intra-language <c>\N</c>
    /// would stack with the bilingual break and produce three-line cues.
    /// </summary>
    public static string Join(IReadOnlyList<string> lines) => string.Join(" ", lines);

    public static string Escape(string text) =>
        text.Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);

    public static string Normalize(IReadOnlyList<string> lines) =>
        string.Join(" ", lines).Trim();

    private static string StripTagsAndDrawings(string input)
    {
        var stripped = DrawingTagPattern.Replace(input, string.Empty);
        stripped = OverrideTagPattern.Replace(stripped, string.Empty);
        stripped = HtmlTagPattern.Replace(stripped, string.Empty);
        stripped = stripped.Replace("\\h", " ", StringComparison.Ordinal);
        return stripped;
    }
}
