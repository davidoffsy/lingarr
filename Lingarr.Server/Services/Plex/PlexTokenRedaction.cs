using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plex;

public static class PlexTokenRedaction
{
    public static bool IsPlexTokenKey(string? key) =>
        string.Equals(key, SettingKeys.Integration.PlexToken, StringComparison.OrdinalIgnoreCase);

    public static Dictionary<string, string> Redact(Dictionary<string, string> settings)
    {
        foreach (var key in settings.Keys.Where(IsPlexTokenKey).ToList())
        {
            settings[key] = string.Empty;
        }

        return settings;
    }
}
