namespace Lingarr.Server.Models.Plex;

public class PlexWebhookInboxStatus
{
    public DateTimeOffset ReceivedAt { get; init; }

    public string Outcome { get; init; } = string.Empty;

    public int HttpStatus { get; init; }

    public string? Reason { get; init; }

    public string? Event { get; init; }

    public string? Title { get; init; }

    public decimal? Rating { get; init; }

    public string? AccountTitle { get; init; }
}
