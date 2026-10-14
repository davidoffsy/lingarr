using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexWebhookInbox
{
    void Record(PlexWebhookInboxStatus status);

    PlexWebhookInboxStatus? GetLast();
}
