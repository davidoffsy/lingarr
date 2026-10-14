using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Plex;

public class PlexPin
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? AuthToken { get; set; }
}

public class PlexTvUser
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public string? Username { get; set; }
}

public class PlexTvResource
{
    public string? Name { get; set; }
    public string? Provides { get; set; }
    public bool Owned { get; set; }
    public string? ClientIdentifier { get; set; }
    public List<PlexTvConnection> Connections { get; set; } = [];
}

public class PlexTvConnection
{
    public string? Uri { get; set; }
    public bool Local { get; set; }
    public bool Relay { get; set; }
    public string? Protocol { get; set; }
    public string? Address { get; set; }
    public int Port { get; set; }
}

public class PlexHomeUser
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public string? Username { get; set; }
    public string? FriendlyName { get; set; }
    public string? Uuid { get; set; }
}

internal class PlexPinApiResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("authToken")]
    public string? AuthToken { get; set; }
}

internal class PlexIdentityApiResponse
{
    public PlexIdentity? MediaContainer { get; set; }
}

public class PlexIdentity
{
    public string? MachineIdentifier { get; set; }
    public string? Version { get; set; }
}
