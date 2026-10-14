using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lingarr.Server.Services.Plex;

public static class PlexJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}
