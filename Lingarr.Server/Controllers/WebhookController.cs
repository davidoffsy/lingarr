using System.Globalization;
using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Server.Attributes;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Plex;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ISettingService _settingService;
    private readonly IPlexWebhookParser _plexWebhookParser;
    private readonly IPlexWebhookInbox _plexWebhookInbox;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IBackgroundJobClient backgroundJobClient,
        ISettingService settingService,
        IPlexWebhookParser plexWebhookParser,
        IPlexWebhookInbox plexWebhookInbox,
        ILogger<WebhookController> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _settingService = settingService;
        _plexWebhookParser = plexWebhookParser;
        _plexWebhookInbox = plexWebhookInbox;
        _logger = logger;
    }

    /// <summary>
    /// Receives webhook events from Radarr
    /// </summary>
    /// <param name="payload">The webhook payload from Radarr</param>
    /// <returns>Returns 200 OK if webhook was queued, 400 if invalid payload</returns>
    [HttpPost("radarr")]
    public async Task<IActionResult> RadarrWebhook([FromBody] RadarrWebhookPayload payload)
    {
        if (payload.Movie == null || payload.Movie.Id <= 0)
        {
            _logger.LogWarning("Invalid Radarr webhook payload: missing or invalid movie data");
            return BadRequest(new { message = "Invalid webhook payload: missing movie data" });
        }

        _backgroundJobClient.Enqueue<WebhookJob>(job => job.ProcessRadarrWebhook(payload));
        _logger.LogInformation("Queued Radarr webhook processing job for movie ID {MovieId}", payload.Movie.Id);
        return Ok(new { message = "Webhook received and queued for processing" });
    }

    /// <summary>
    /// Receives webhook events from Sonarr
    /// </summary>
    /// <param name="payload">The webhook payload from Sonarr</param>
    /// <returns>Returns 200 OK if webhook was queued, 400 if invalid payload</returns>
    [HttpPost("sonarr")]
    public async Task<IActionResult> SonarrWebhook([FromBody] SonarrWebhookPayload payload)
    {
        if (payload.Series == null || payload.Series.Id <= 0 || payload.Episodes == null || !payload.Episodes.Any())
        {
            _logger.LogWarning("Invalid Sonarr webhook payload: missing series or episode data");
            return BadRequest(new { message = "Invalid webhook payload: missing series or episode data" });
        }

        _backgroundJobClient.Enqueue<WebhookJob>(job => job.ProcessSonarrWebhook(payload));
        _logger.LogInformation("Queued Sonarr webhook processing job for series ID {SeriesId}, episodes: {EpisodeIds}",
            payload.Series.Id, string.Join(", ", payload.Episodes.Select(e => e.Id)));
        return Ok(new { message = "Webhook received and queued for processing" });
    }

    /// <summary>
    /// Reachability probe. Plex Media Server only POSTs this URL; GET never enqueues a translation.
    /// </summary>
    [HttpGet("plex")]
    [AllowAnonymous]
    public IActionResult PlexWebhookProbe()
    {
        return Ok(new
        {
            reachable = true,
            accepts = "POST",
            message = "This URL is reachable, but Plex must POST multipart/form-data with a payload field. A GET/curl of this page does not count as a webhook."
        });
    }

    /// <summary>
    /// Receives webhook events from Plex. Returns immediately after enqueueing.
    /// Authenticated with the Lingarr-generated webhook secret query parameter, not the plex.tv token.
    /// </summary>
    [HttpPost("plex")]
    [AllowAnonymous]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(PlexWebhookParser.MaxRequestBytes)]
    public async Task<IActionResult> PlexWebhook([FromQuery] string? token)
    {
        var onboardingCompleted = await _settingService.GetSetting(SettingKeys.Authentication.OnboardingCompleted);
        if (!string.Equals(onboardingCompleted, "true", StringComparison.OrdinalIgnoreCase))
        {
            Remember("rejected", StatusCodes.Status403Forbidden, "Onboarding required");
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Onboarding required",
                onboardingRequired = true
            });
        }

        var secret = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret);
        if (string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("Plex webhook rejected: plex_webhook_secret is not configured");
            Remember("rejected", StatusCodes.Status401Unauthorized, "Plex webhook secret is not configured");
            return Unauthorized(new { message = "Plex webhook secret is not configured" });
        }

        if (!SecretComparer.Equals(token, secret))
        {
            _logger.LogWarning("Plex webhook rejected: invalid token");
            Remember("rejected", StatusCodes.Status401Unauthorized, "Invalid webhook token");
            return Unauthorized(new { message = "Invalid webhook token" });
        }

        var payloadJson = await _plexWebhookParser.ReadPayloadJsonAsync(Request);
        var payload = _plexWebhookParser.Parse(payloadJson);
        if (string.IsNullOrWhiteSpace(payloadJson) || payload == null)
        {
            var preview = string.IsNullOrWhiteSpace(payloadJson)
                ? "(empty)"
                : payloadJson.Length <= 240
                    ? payloadJson
                    : payloadJson[..240];
            _logger.LogWarning(
                "Invalid Plex webhook payload: missing or unreadable JSON (content-type {ContentType}, preview {Preview})",
                Request.ContentType,
                preview);
            Remember("invalid", StatusCodes.Status400BadRequest, "missing or unreadable JSON");
            return BadRequest(new { message = "Invalid webhook payload: missing payload data" });
        }

        var minRaw = await _settingService.GetSetting(SettingKeys.Integration.PlexMinRating);
        if (!decimal.TryParse(minRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var minRating))
        {
            minRating = 10;
        }

        if (PlexWebhookEventFilter.ShouldSkip(payload, minRating, out var reason))
        {
            _logger.LogInformation("Plex webhook ignored: {Reason}", reason);
            Remember("ignored", StatusCodes.Status200OK, reason, payload);
            return Ok(new { message = "Webhook ignored", reason });
        }

        _backgroundJobClient.Enqueue<WebhookJob>(job => job.ProcessPlexWebhook(payloadJson));
        _logger.LogInformation("Queued Plex webhook processing job");
        Remember("queued", StatusCodes.Status200OK, null, payload);
        return Ok(new { message = "Webhook received and queued for processing" });
    }

    private void Remember(string outcome, int status, string? reason, PlexWebhookPayload? payload = null)
    {
        decimal? rating = null;
        if (payload != null && PlexRatingParser.TryGetRating(payload, out var value))
        {
            rating = value;
        }

        _plexWebhookInbox.Record(new PlexWebhookInboxStatus
        {
            ReceivedAt = DateTimeOffset.UtcNow,
            Outcome = outcome,
            HttpStatus = status,
            Reason = reason,
            Event = payload?.Event,
            Title = payload?.Metadata?.Title,
            Rating = rating,
            AccountTitle = payload?.Account?.Title
        });
    }
}
