namespace Lingarr.Server.Models.Plex;

public class PlexPinCreatedResponse
{
    public int PinId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string AuthUrl { get; set; } = string.Empty;
}

public class PlexPinStatusResponse
{
    public bool Linked { get; set; }
    public string? AccountTitle { get; set; }
}

public class PlexTestRequest
{
    public string? Url { get; set; }
}

public class PlexConnectionTestResult
{
    public bool Success { get; set; }
    public string? MachineIdentifier { get; set; }
    public string? Version { get; set; }
    public string? Message { get; set; }
}

public class PlexWebhookSecretResponse
{
    public string Secret { get; set; } = string.Empty;
}

public class PlexServerDto
{
    public string Name { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public string? PreferredUri { get; set; }
    public List<PlexConnectionDto> Connections { get; set; } = [];
}

public class PlexConnectionDto
{
    public string Uri { get; set; } = string.Empty;
    public bool Local { get; set; }
    public bool Relay { get; set; }
}

internal sealed record PlexPinSession(int PinId, string Code, string ClientIdentifier);
