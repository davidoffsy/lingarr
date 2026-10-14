namespace Lingarr.Server.Services.Plex;

public static class PlexCacheKeys
{
    public const string HomeUsers = "plex:home-users";

    public static string Pin(int pinId) => $"plex:pin:{pinId}";
}
