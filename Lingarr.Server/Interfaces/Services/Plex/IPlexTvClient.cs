using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexTvClient
{
    Task<PlexPin> CreatePinAsync(string clientIdentifier, CancellationToken cancellationToken = default);

    Task<PlexPin> GetPinAsync(
        int pinId,
        string clientIdentifier,
        string? code,
        CancellationToken cancellationToken = default);

    Task<PlexTvUser?> GetUserAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlexTvResource>> GetResourcesAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlexHomeUser>> GetHomeUsersAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken = default);
}
