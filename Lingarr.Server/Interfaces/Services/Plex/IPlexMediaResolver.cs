using Lingarr.Server.Models.Plex;
using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexMediaResolver
{
    Task<PlexMediaResolveResult> ResolveAsync(
        PlexWebhookPayload payload,
        CancellationToken cancellationToken = default);
}
