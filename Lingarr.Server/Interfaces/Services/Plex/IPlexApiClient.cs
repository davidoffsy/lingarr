using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Interfaces.Services.Plex;

public interface IPlexApiClient
{
    Task<PlexIdentity?> GetIdentity(
        string baseUrl,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches PMS metadata for a rating key.
    /// Returns <c>null</c> on 404 or when the item has no metadata.
    /// Throws on 401, 5xx, or timeout so Hangfire can retry.
    /// </summary>
    Task<PlexMetadata?> GetMetadata(
        string baseUrl,
        string accessToken,
        string ratingKey,
        CancellationToken cancellationToken = default);
}
