using Hangfire;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Jobs;

public class PlexRatingTriggerJob
{
    private readonly IPlexRatingTrigger _plexRatingTrigger;

    public PlexRatingTriggerJob(IPlexRatingTrigger plexRatingTrigger)
    {
        _plexRatingTrigger = plexRatingTrigger;
    }

    /// <summary>
    /// Probes sidecars/MKV and enqueues translation on the translation queue,
    /// so ffprobe cannot stall the webhook worker.
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    [Queue("translation")]
    public Task Execute(
        MediaType mediaType,
        int entityId,
        string? ratingKey,
        CancellationToken cancellationToken = default)
    {
        return _plexRatingTrigger.EnqueueAsync(
            PlexMediaResolveResult.Hit(mediaType, entityId, null, ratingKey, null),
            cancellationToken);
    }
}
