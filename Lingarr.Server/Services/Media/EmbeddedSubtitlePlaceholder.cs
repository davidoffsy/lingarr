namespace Lingarr.Server.Services.Media;

/// <summary>
/// Encodes an MKV path + stream index into <see cref="Lingarr.Core.Entities.TranslationRequest.SubtitleToTranslate"/>
/// so Hangfire can extract the sidecar before the existing translation pipeline runs.
/// Format: <c>embedded:{mkvPath}:{trackIndex}</c>.
/// </summary>
public static class EmbeddedSubtitlePlaceholder
{
    public const string Prefix = "embedded:";

    public static string Create(string mkvPath, int trackIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mkvPath);
        ArgumentOutOfRangeException.ThrowIfNegative(trackIndex);

        return $"{Prefix}{mkvPath}:{trackIndex}";
    }

    public static bool TryParse(string? value, out string mkvPath, out int trackIndex)
    {
        mkvPath = string.Empty;
        trackIndex = -1;

        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = value[Prefix.Length..];
        var lastColon = remainder.LastIndexOf(':');
        if (lastColon <= 0 || lastColon == remainder.Length - 1)
        {
            return false;
        }

        var path = remainder[..lastColon];
        if (string.IsNullOrWhiteSpace(path) ||
            !int.TryParse(remainder[(lastColon + 1)..], out var parsedIndex) ||
            parsedIndex < 0)
        {
            return false;
        }

        mkvPath = path;
        trackIndex = parsedIndex;
        return true;
    }
}
