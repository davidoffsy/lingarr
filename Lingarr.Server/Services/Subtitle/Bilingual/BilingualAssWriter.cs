using System.Globalization;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle.Bilingual;

/// <summary>
/// Writes a standard ASS v4.00+ bilingual sidecar: one Dialogue per cue, target
/// style on top of source style via <c>\N{\rLingarrSource}</c>. Independent of
/// <see cref="SsaWriter"/> and of any source <c>SsaFormat</c>.
/// </summary>
public class BilingualAssWriter
{
    public async Task WriteAsync(
        Stream stream,
        IEnumerable<SubtitleItem> subtitleItems,
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
        await WriteAsync(writer, subtitleItems, cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    public async Task WriteAsync(
        TextWriter writer,
        IEnumerable<SubtitleItem> subtitleItems,
        CancellationToken cancellationToken = default)
    {
        var events = BuildEvents(subtitleItems);

        await writer.WriteLineAsync("[Script Info]");
        await writer.WriteLineAsync($"ScriptType: {BilingualAssOptions.ScriptType}");
        await writer.WriteLineAsync($"PlayResX: {BilingualAssOptions.PlayResX}");
        await writer.WriteLineAsync($"PlayResY: {BilingualAssOptions.PlayResY}");
        await writer.WriteLineAsync($"WrapStyle: {BilingualAssOptions.WrapStyle}");
        await writer.WriteLineAsync("ScaledBorderAndShadow: yes");
        await writer.WriteLineAsync();

        await writer.WriteLineAsync("[V4+ Styles]");
        await writer.WriteLineAsync(BilingualAssOptions.StylesFormat);
        await writer.WriteLineAsync(BilingualAssOptions.TargetStyleLine());
        await writer.WriteLineAsync(BilingualAssOptions.SourceStyleLine());
        await writer.WriteLineAsync();

        await writer.WriteLineAsync("[Events]");
        await writer.WriteLineAsync(BilingualAssOptions.EventsFormat);
        foreach (var dialogue in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(dialogue);
        }
    }

    /// <summary>
    /// Builds Dialogue lines without mutating <paramref name="subtitleItems"/>.
    /// Exposed for tests that assert filtering without writing a stream.
    /// </summary>
    private static IReadOnlyList<string> BuildEvents(IEnumerable<SubtitleItem> subtitleItems)
    {
        var dialogues = new List<string>();
        string? previousKey = null;

        foreach (var item in subtitleItems)
        {
            var sourceLines = BilingualAssText.CleanLines(SourceLines(item));
            var targetLines = BilingualAssText.CleanLines(item.TranslatedLines);

            if (sourceLines.Count == 0 && targetLines.Count == 0)
            {
                continue;
            }

            var sourceJoined = BilingualAssText.Join(sourceLines);
            var targetJoined = BilingualAssText.Join(targetLines);
            var duplicateKey = $"{item.StartTime}|{item.EndTime}|{BilingualAssText.Normalize(sourceLines)}|{BilingualAssText.Normalize(targetLines)}";
            if (duplicateKey == previousKey)
            {
                continue;
            }

            previousKey = duplicateKey;

            var (style, text) = ComposeText(sourceJoined, targetJoined);
            dialogues.Add(
                "Dialogue: 0," +
                $"{FormatAssTime(item.StartTime)}," +
                $"{FormatAssTime(item.EndTime)}," +
                $"{style},,0,0,0,," +
                text);
        }

        return dialogues;
    }

    private static IEnumerable<string> SourceLines(SubtitleItem item) =>
        item.PlaintextLines.Count > 0 ? item.PlaintextLines : item.Lines;

    private static (string Style, string Text) ComposeText(string sourceJoined, string targetJoined)
    {
        var sourceEmpty = string.IsNullOrEmpty(sourceJoined);
        var targetEmpty = string.IsNullOrEmpty(targetJoined);

        if (targetEmpty)
        {
            return (BilingualAssOptions.SourceStyleName, BilingualAssText.Escape(sourceJoined));
        }

        // Intro credits (and any cue whose translation equals the source) are
        // emitted once in the target style so the pair is never duplicated.
        if (sourceEmpty || string.Equals(sourceJoined, targetJoined, StringComparison.Ordinal))
        {
            return (BilingualAssOptions.TargetStyleName, BilingualAssText.Escape(targetJoined));
        }

        var text =
            BilingualAssText.Escape(targetJoined) +
            "\\N{\\r" + BilingualAssOptions.SourceStyleName + "}" +
            BilingualAssText.Escape(sourceJoined);
        return (BilingualAssOptions.TargetStyleName, text);
    }

    private static string FormatAssTime(int milliseconds)
    {
        if (milliseconds < 0)
        {
            milliseconds = 0;
        }

        var time = TimeSpan.FromMilliseconds(milliseconds);
        var hours = (int)time.TotalHours;
        var centiseconds = time.Milliseconds / 10;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{time.Minutes:00}:{time.Seconds:00}.{centiseconds:00}");
    }
}
