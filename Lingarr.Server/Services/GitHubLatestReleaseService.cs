using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Core;
using Lingarr.Server.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services;

public class GitHubLatestReleaseService : ILatestReleaseService
{
    private const string CacheKey = "GitHub_LatestRelease";
    private static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{LingarrVersion.GitHubRepository}/releases/latest");

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GitHubLatestReleaseService> _logger;

    public GitHubLatestReleaseService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<GitHubLatestReleaseService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string?> GetLatestVersion()
    {
        if (_cache.TryGetValue(CacheKey, out string? cachedVersion) && !string.IsNullOrEmpty(cachedVersion))
        {
            _logger.LogDebug("Returning cached GitHub release version");
            return cachedVersion;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
            request.Headers.UserAgent.ParseAdd($"{LingarrVersion.Name}/{LingarrVersion.Number}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get latest GitHub release: {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubReleaseResponse>(content);
            var version = LingarrVersion.Normalize(release?.TagName);
            if (string.IsNullOrEmpty(version))
            {
                _logger.LogWarning("GitHub latest release returned an empty tag");
                return null;
            }

            _cache.Set(CacheKey, version, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromHours(24)));

            _logger.LogInformation("Retrieved latest GitHub release: {Version}", version);
            return version;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch latest GitHub release");
            return null;
        }
    }

    private sealed class GitHubReleaseResponse
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}
