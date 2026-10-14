using Lingarr.Core;

namespace Lingarr.Server.Services.Plex;

public static class PlexHeaders
{
    public const string Product = "Lingarr";

    public static void Apply(HttpRequestMessage request, string clientIdentifier, string? accessToken = null)
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Product", Product);
        request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", clientIdentifier);
        request.Headers.TryAddWithoutValidation("X-Plex-Version", LingarrVersion.Number);
        request.Headers.TryAddWithoutValidation("X-Plex-Device", "Server");
        request.Headers.TryAddWithoutValidation("X-Plex-Platform", "Lingarr");

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.TryAddWithoutValidation("X-Plex-Token", accessToken);
        }
    }
}
