using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle;

/// <summary>
/// Removes a single trailing ideographic full stop (U+3002 <c>。</c>) from each cue.
/// Periods in the middle of the cue, runs of consecutive full stops, and other
/// punctuation are left unchanged. Markup is ignored when locating the last
/// visible character so ASS/HTML tags stay in place.
/// </summary>
public static class TrailingIdeographicFullStopStripper
{
    public const char IdeographicFullStop = '\u3002';

    public static void Apply(IEnumerable<SubtitleItem>? subtitles)
    {
        if (subtitles is null)
        {
            return;
        }

        foreach (var item in subtitles)
        {
            Apply(item.TranslatedLines);
        }
    }

    public static void Apply(IList<string>? lines)
    {
        if (lines is null || lines.Count == 0)
        {
            return;
        }

        var visible = new List<(int Line, int Index)>();
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            CollectVisible(lines[lineIndex], lineIndex, visible);
        }

        // A cue that is only "。" would become empty; leave it alone.
        if (visible.Count < 2)
        {
            return;
        }

        var last = visible[^1];
        if (lines[last.Line][last.Index] != IdeographicFullStop)
        {
            return;
        }

        var previous = visible[^2];
        if (lines[previous.Line][previous.Index] == IdeographicFullStop)
        {
            return;
        }

        lines[last.Line] = lines[last.Line].Remove(last.Index, 1);
    }

    private static void CollectVisible(string? line, int lineIndex, List<(int Line, int Index)> visible)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        var index = 0;
        while (index < line.Length)
        {
            var current = line[index];
            if (TrySkipDelimited(line, ref index, '{', '}'))
            {
                continue;
            }

            if (TrySkipDelimited(line, ref index, '<', '>'))
            {
                continue;
            }

            if (current == '\\' && index + 1 < line.Length && IsAssWhitespaceEscape(line[index + 1]))
            {
                index += 2;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            visible.Add((lineIndex, index));
            index++;
        }
    }

    private static bool TrySkipDelimited(string line, ref int index, char open, char close)
    {
        if (line[index] != open)
        {
            return false;
        }

        var closeIndex = line.IndexOf(close, index + 1);
        if (closeIndex < 0)
        {
            return false;
        }

        index = closeIndex + 1;
        return true;
    }

    private static bool IsAssWhitespaceEscape(char next) =>
        next is 'N' or 'n' or 'h' or 't';
}
