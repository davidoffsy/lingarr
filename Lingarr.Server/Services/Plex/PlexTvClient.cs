using System.Net.Http.Json;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

public class PlexTvClient : IPlexTvClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlexTvClient> _logger;

    public PlexTvClient(IHttpClientFactory httpClientFactory, ILogger<PlexTvClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PlexPin> CreatePinAsync(string clientIdentifier, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PlexTv");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/pins?strong=true");
        PlexHeaders.Apply(request, clientIdentifier);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["strong"] = "true"
        });

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PlexPinApiResponse>(PlexJson.Options, cancellationToken)
                   ?? throw new InvalidOperationException("Plex PIN response was empty.");

        return new PlexPin
        {
            Id = body.Id,
            Code = body.Code ?? string.Empty,
            AuthToken = body.AuthToken
        };
    }

    public async Task<PlexPin> GetPinAsync(
        int pinId,
        string clientIdentifier,
        string? code,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PlexTv");
        var path = $"/api/v2/pins/{pinId}";
        if (!string.IsNullOrEmpty(code))
        {
            path += $"?code={Uri.EscapeDataString(code)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        PlexHeaders.Apply(request, clientIdentifier);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Plex PIN {PinId} poll returned {StatusCode}", pinId, (int)response.StatusCode);
            return new PlexPin { Id = pinId, Code = code ?? string.Empty };
        }

        var body = await response.Content.ReadFromJsonAsync<PlexPinApiResponse>(PlexJson.Options, cancellationToken);
        return new PlexPin
        {
            Id = body?.Id ?? pinId,
            Code = body?.Code ?? code ?? string.Empty,
            AuthToken = body?.AuthToken
        };
    }

    public async Task<PlexTvUser?> GetUserAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PlexTv");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/user");
        PlexHeaders.Apply(request, clientIdentifier, accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlexTvUser>(PlexJson.Options, cancellationToken);
    }

    public async Task<IReadOnlyList<PlexTvResource>> GetResourcesAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PlexTv");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/resources?includeHttps=1");
        PlexHeaders.Apply(request, clientIdentifier, accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var resources = JsonSerializer.Deserialize<List<PlexTvResource>>(json, PlexJson.Options);
        return resources ?? [];
    }

    public async Task<IReadOnlyList<PlexHomeUser>> GetHomeUsersAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("PlexTv");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/home/users");
        PlexHeaders.Apply(request, clientIdentifier, accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        JsonElement usersElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            usersElement = root;
        }
        else if (root.TryGetProperty("users", out var users))
        {
            usersElement = users;
        }
        else
        {
            return [];
        }

        if (usersElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<PlexHomeUser>>(usersElement.GetRawText(), PlexJson.Options) ?? [];
    }
}
