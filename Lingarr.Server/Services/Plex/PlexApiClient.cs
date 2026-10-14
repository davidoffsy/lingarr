using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

public class PlexApiClient : IPlexApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlexApiClient> _logger;

    public PlexApiClient(IHttpClientFactory httpClientFactory, ILogger<PlexApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PlexIdentity?> GetIdentity(
        string baseUrl,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var parsedBase))
        {
            _logger.LogWarning("Plex identity check skipped: invalid URL {Url}", baseUrl);
            return null;
        }

        var identityUri = new Uri(parsedBase, "/identity");
        var client = _httpClientFactory.CreateClient("Plex");
        using var request = new HttpRequestMessage(HttpMethod.Get, identityUri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Token", accessToken);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", PlexHeaders.Product);

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Plex identity check failed with {StatusCode} for {Url}",
                    (int)response.StatusCode, identityUri);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<PlexIdentityApiResponse>(
                PlexJson.Options, cancellationToken);
            return body?.MediaContainer;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Plex identity check failed for {Url}", identityUri);
            return null;
        }
    }

    public async Task<PlexMetadata?> GetMetadata(
        string baseUrl,
        string accessToken,
        string ratingKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(ratingKey))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var parsedBase))
        {
            _logger.LogWarning("Plex metadata fetch skipped: invalid URL {Url}", baseUrl);
            return null;
        }

        var metadataUri = new Uri(parsedBase, $"/library/metadata/{Uri.EscapeDataString(ratingKey)}");
        var client = _httpClientFactory.CreateClient("Plex");
        using var request = new HttpRequestMessage(HttpMethod.Get, metadataUri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Plex-Token", accessToken);
        request.Headers.TryAddWithoutValidation("X-Plex-Product", PlexHeaders.Product);

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Plex metadata not found for ratingKey {RatingKey}", ratingKey);
            return null;
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var item = PlexMetadataMapper.FirstMetadataItem(document.RootElement);
        return item == null ? null : PlexMetadataMapper.FromJson(item.Value);
    }
}
