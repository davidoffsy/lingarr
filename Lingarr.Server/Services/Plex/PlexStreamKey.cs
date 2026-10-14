namespace Lingarr.Server.Services.Plex;

internal static class PlexStreamKey
{
    public const int MaxBytes = 8 * 1024 * 1024;
    private const string Prefix = "/library/streams/";

    public static bool IsSafe(string? streamKey)
    {
        if (string.IsNullOrWhiteSpace(streamKey) ||
            !streamKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var id = streamKey[Prefix.Length..];
        return id.Length > 0 && id.All(char.IsDigit);
    }
}
