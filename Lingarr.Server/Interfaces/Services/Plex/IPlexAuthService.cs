using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexAuthService
{
    Task<PlexPinCreatedResponse> CreatePinAsync(CancellationToken cancellationToken = default);

    Task<PlexPinStatusResponse> PollPinAsync(int pinId, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlexServerDto>> GetServersAsync(CancellationToken cancellationToken = default);

    Task<PlexConnectionTestResult> TestConnectionAsync(
        string? urlOverride,
        CancellationToken cancellationToken = default);

    Task<string> EnsureWebhookSecretAsync(CancellationToken cancellationToken = default);

    Task<string> RegenerateWebhookSecretAsync(CancellationToken cancellationToken = default);
}
