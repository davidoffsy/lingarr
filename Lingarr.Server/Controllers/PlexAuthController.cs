using Lingarr.Server.Attributes;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;
using Microsoft.AspNetCore.Mvc;

namespace Lingarr.Server.Controllers;

[ApiController]
[LingarrAuthorize]
[Route("api/plex")]
public class PlexAuthController : ControllerBase
{
    private readonly IPlexAuthService _plexAuthService;
    private readonly IPlexWebhookInbox _plexWebhookInbox;
    private readonly ILogger<PlexAuthController> _logger;

    public PlexAuthController(
        IPlexAuthService plexAuthService,
        IPlexWebhookInbox plexWebhookInbox,
        ILogger<PlexAuthController> logger)
    {
        _plexAuthService = plexAuthService;
        _plexWebhookInbox = plexWebhookInbox;
        _logger = logger;
    }

    [HttpPost("auth/pin")]
    public async Task<ActionResult<PlexPinCreatedResponse>> CreatePin(CancellationToken cancellationToken)
    {
        try
        {
            var pin = await _plexAuthService.CreatePinAsync(cancellationToken);
            return Ok(pin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Plex PIN");
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Failed to start Sign in with Plex" });
        }
    }

    [HttpGet("auth/pin/{id:int}")]
    public async Task<ActionResult<PlexPinStatusResponse>> PollPin(int id, CancellationToken cancellationToken)
    {
        try
        {
            var status = await _plexAuthService.PollPinAsync(id, cancellationToken);
            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll Plex PIN {PinId}", id);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Failed to poll Plex PIN" });
        }
    }

    [HttpDelete("auth")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await _plexAuthService.DisconnectAsync(cancellationToken);
        return Ok();
    }

    [HttpGet("servers")]
    public async Task<ActionResult<IReadOnlyList<PlexServerDto>>> GetServers(CancellationToken cancellationToken)
    {
        try
        {
            var servers = await _plexAuthService.GetServersAsync(cancellationToken);
            return Ok(servers);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list Plex servers");
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Failed to list Plex servers" });
        }
    }

    [HttpPost("test")]
    public async Task<ActionResult<PlexConnectionTestResult>> TestConnection(
        [FromBody] PlexTestRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _plexAuthService.TestConnectionAsync(request?.Url, cancellationToken);
        return Ok(result);
    }

    [HttpGet("webhook-secret")]
    public async Task<ActionResult<PlexWebhookSecretResponse>> GetWebhookSecret(CancellationToken cancellationToken)
    {
        var secret = await _plexAuthService.EnsureWebhookSecretAsync(cancellationToken);
        return Ok(new PlexWebhookSecretResponse { Secret = secret });
    }

    [HttpPost("webhook-secret")]
    public async Task<ActionResult<PlexWebhookSecretResponse>> RegenerateWebhookSecret(
        CancellationToken cancellationToken)
    {
        var secret = await _plexAuthService.RegenerateWebhookSecretAsync(cancellationToken);
        return Ok(new PlexWebhookSecretResponse { Secret = secret });
    }

    [HttpGet("webhook-status")]
    public IActionResult GetWebhookStatus()
    {
        return new JsonResult(_plexWebhookInbox.GetLast());
    }
}
