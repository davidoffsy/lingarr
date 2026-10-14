using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexWebhookParser
{
    Task<string?> ReadPayloadJsonAsync(HttpRequest request, CancellationToken cancellationToken = default);

    PlexWebhookPayload? Parse(string? json);
}
