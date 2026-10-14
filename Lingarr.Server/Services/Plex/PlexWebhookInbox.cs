using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

public class PlexWebhookInbox : IPlexWebhookInbox
{
    private readonly object _gate = new();
    private PlexWebhookInboxStatus? _last;

    public void Record(PlexWebhookInboxStatus status)
    {
        lock (_gate)
        {
            _last = status;
        }
    }

    public PlexWebhookInboxStatus? GetLast()
    {
        lock (_gate)
        {
            return _last;
        }
    }
}
