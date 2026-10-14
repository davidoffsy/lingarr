using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexRaterFilter
{
    /// <summary>
    /// Returns true when the webhook Account is allowed to trigger translation.
    /// Does not use the webhook <c>owner</c> flag.
    /// </summary>
    Task<bool> IsAllowedAsync(PlexWebhookAccount? account, CancellationToken cancellationToken = default);
}
