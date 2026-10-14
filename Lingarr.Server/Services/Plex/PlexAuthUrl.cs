namespace Lingarr.Server.Services.Plex;

public static class PlexAuthUrl
{
    public const string Prefix = "https://app.plex.tv/auth#?";

    public static string Build(string clientIdentifier, string code)
    {
        return Prefix +
               "clientID=" + Uri.EscapeDataString(clientIdentifier) +
               "&code=" + Uri.EscapeDataString(code) +
               "&context%5Bdevice%5D%5Bproduct%5D=" + Uri.EscapeDataString(PlexHeaders.Product);
    }
}
