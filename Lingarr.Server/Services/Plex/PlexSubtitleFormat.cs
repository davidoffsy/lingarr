namespace Lingarr.Server.Services.Plex;

internal static class PlexSubtitleFormat
{
    public static bool TryGetExtension(string? codec, string? format, out string extension)
    {
        var token = FirstNonEmpty(codec, format)?.Trim().ToLowerInvariant();
        extension = token switch
        {
            "srt" or "subrip" => ".srt",
            "ass" => ".ass",
            "ssa" => ".ssa",
            _ => string.Empty
        };

        return extension.Length > 0;
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
