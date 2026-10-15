using System.Globalization;
using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Plex;
using Lingarr.Server.Services.Webhooks;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Jobs;

public class WebhookJob
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMediaService _mediaService;
    private readonly IMediaSubtitleProcessor _mediaSubtitleProcessor;
    private readonly IPlexWebhookParser _plexWebhookParser;
    private readonly IPlexRaterFilter _plexRaterFilter;
    private readonly IPlexMediaResolver _plexMediaResolver;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ISettingService _settingService;
    private readonly ILogger<WebhookJob> _logger;

    public WebhookJob(
        LingarrDbContext dbContext,
        IMediaService mediaService,
        IMediaSubtitleProcessor mediaSubtitleProcessor,
        IPlexWebhookParser plexWebhookParser,
        IPlexRaterFilter plexRaterFilter,
        IPlexMediaResolver plexMediaResolver,
        IBackgroundJobClient backgroundJobClient,
        ISettingService settingService,
        ILogger<WebhookJob> logger)
    {
        _dbContext = dbContext;
        _mediaService = mediaService;
        _mediaSubtitleProcessor = mediaSubtitleProcessor;
        _plexWebhookParser = plexWebhookParser;
        _plexRaterFilter = plexRaterFilter;
        _plexMediaResolver = plexMediaResolver;
        _backgroundJobClient = backgroundJobClient;
        _settingService = settingService;
        _logger = logger;
    }

    private async Task<bool> AutoTranslateEnabled() =>
        !string.Equals(await _settingService.GetSetting(SettingKeys.Integration.ArrWebhookAutoTranslate),
            "false", StringComparison.OrdinalIgnoreCase);

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessRadarrWebhook(RadarrWebhookPayload payload)
    {
        if (ArrWebhookPayloadFilter.ShouldIgnoreRadarr(payload, out var reason))
        {
            _logger.LogInformation("Radarr webhook skipped: {Reason}", reason);
            return;
        }

        try
        {
            var movieId = await _mediaService.GetMovieIdOrSyncFromRadarrMovieId(payload.Movie!.Id);
            if (movieId == 0)
            {
                _logger.LogWarning("Failed to sync or find movie with Radarr ID {RadarrId}. Movie may not have a file yet.",
                    payload.Movie.Id);
                return;
            }
            
            var movie = await _dbContext.Movies.FirstOrDefaultAsync(m => m.Id == movieId);
            if (movie == null)
            {
                _logger.LogError("Movie with ID {MovieId} not found in database after sync", movieId);
                return;
            }

            if (!await AutoTranslateEnabled()) return;
            await _mediaSubtitleProcessor.ProcessMedia(movie, MediaType.Movie);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Radarr webhook for movie {MovieTitle} (Radarr ID: {RadarrId})",
                payload.Movie!.Title, payload.Movie.Id);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessSonarrWebhook(SonarrWebhookPayload payload)
    {
        if (ArrWebhookPayloadFilter.ShouldIgnoreSonarr(payload, out var reason))
        {
            _logger.LogInformation("Sonarr webhook skipped: {Reason}", reason);
            return;
        }

        try
        {
            var autoTranslate = await AutoTranslateEnabled();
            foreach (var episode in payload.Episodes!)
            {
                var episodeId = await _mediaService.GetEpisodeIdOrSyncFromSonarrEpisodeId(episode.Id);
                if (episodeId == 0)
                {
                    _logger.LogWarning("Failed to sync or find episode with Sonarr ID {SonarrEpisodeId}. Episode may not have a file yet.",
                        episode.Id);
                    continue;
                }
                
                var episodeEntity = await _dbContext.Episodes.FirstOrDefaultAsync(e => e.Id == episodeId);
                if (episodeEntity == null)
                {
                    _logger.LogError("Episode with ID {EpisodeId} not found in database after sync", episodeId);
                    continue;
                }

                if (autoTranslate)
                {
                    await _mediaSubtitleProcessor.ProcessMedia(episodeEntity, MediaType.Episode);
                }
            }

            _logger.LogInformation("Completed processing Sonarr webhook for series: {SeriesTitle}", payload.Series!.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Sonarr webhook for series {SeriesTitle} (Sonarr ID: {SonarrId})",
                payload.Series!.Title, payload.Series.Id);
            throw;
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 2 * 60)]
    [AutomaticRetry(Attempts = 3)]
    [Queue("webhook")]
    public async Task ProcessPlexWebhook(string payloadJson)
    {
        var payload = _plexWebhookParser.Parse(payloadJson);
        if (payload == null)
        {
            _logger.LogInformation("Plex webhook skipped: invalid payload");
            return;
        }

        var minRaw = await _settingService.GetSetting(SettingKeys.Integration.PlexMinRating);
        if (!decimal.TryParse(minRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var minRating))
        {
            minRating = 10;
        }

        if (PlexWebhookEventFilter.ShouldSkip(payload, minRating, out var reason))
        {
            _logger.LogInformation("Plex webhook skipped: {Reason}", reason);
            return;
        }

        if (!await _plexRaterFilter.IsAllowedAsync(payload.Account))
        {
            _logger.LogInformation(
                "Plex webhook skipped: account {AccountId} ({AccountTitle}) is not allowed to trigger translations",
                payload.Account?.Id,
                payload.Account?.Title);
            return;
        }

        var resolved = await _plexMediaResolver.ResolveAsync(payload);
        if (!resolved.Found || resolved.MediaType is not MediaType mediaType)
        {
            _logger.LogInformation(
                "Plex webhook skipped: {Reason} (ratingKey {RatingKey}, title {Title})",
                resolved.SkipReason,
                payload.Metadata?.RatingKey,
                payload.Metadata?.Title);
            return;
        }

        _logger.LogInformation(
            "Plex media resolved as {MediaType} id {EntityId} (ratingKey {RatingKey}, path {MappedPath})",
            mediaType,
            resolved.EntityId,
            payload.Metadata?.RatingKey,
            resolved.MappedPath);

        _backgroundJobClient.Enqueue<PlexRatingTriggerJob>(job =>
            job.Execute(mediaType, resolved.EntityId, resolved.RatingKey, CancellationToken.None));
    }
}
