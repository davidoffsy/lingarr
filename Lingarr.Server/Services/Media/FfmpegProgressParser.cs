using System.Globalization;

namespace Lingarr.Server.Services.Media;

/// <summary>
/// Parses ffmpeg <c>-progress pipe:1</c> key=value lines.
/// </summary>
public static class FfmpegProgressParser
{
    public const string OutTimeUsPrefix = "out_time_us=";
    public const string ProgressPrefix = "progress=";

    public static bool TryParseOutTimeUs(string? line, out long microseconds)
    {
        microseconds = 0;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(OutTimeUsPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var value = trimmed[OutTimeUsPrefix.Length..];
        if (value.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("NA", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out microseconds) &&
               microseconds >= 0;
    }

    public static bool IsEnd(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        return line.Trim().Equals("progress=end", StringComparison.OrdinalIgnoreCase);
    }

    public static int? ToPercent(long outTimeUs, double durationSeconds)
    {
        if (durationSeconds <= 0 || outTimeUs < 0)
        {
            return null;
        }

        var percent = outTimeUs / 1_000_000.0 / durationSeconds * 100.0;
        if (double.IsNaN(percent) || double.IsInfinity(percent))
        {
            return null;
        }

        return Math.Clamp((int)Math.Round(percent), 0, 100);
    }
}
