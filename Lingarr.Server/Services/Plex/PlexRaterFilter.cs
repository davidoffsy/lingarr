using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Models.Webhooks;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Plex;

public class PlexRaterFilter : IPlexRaterFilter
{
    private static readonly TimeSpan HomeUsersCacheDuration = TimeSpan.FromMinutes(45);

    private readonly IPlexTvClient _plexTvClient;
    private readonly ISettingService _settingService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlexRaterFilter> _logger;

    public PlexRaterFilter(
        IPlexTvClient plexTvClient,
        ISettingService settingService,
        IMemoryCache cache,
        ILogger<PlexRaterFilter> logger)
    {
        _plexTvClient = plexTvClient;
        _settingService = settingService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsAllowedAsync(
        PlexWebhookAccount? account,
        CancellationToken cancellationToken = default)
    {
        var mode = await _settingService.GetSetting(SettingKeys.Integration.PlexAllowedRaters);
        if (string.Equals(mode, "anyone", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (account == null)
        {
            _logger.LogInformation("Plex webhook skipped: missing Account in home rater mode");
            return false;
        }

        if (account.Id == 1)
        {
            return true;
        }

        var signedInTitle = await _settingService.GetSetting(SettingKeys.Integration.PlexAccountTitle);
        if (NamesEqual(signedInTitle, account.Title))
        {
            return true;
        }

        try
        {
            var homeUsers = await GetHomeUsersAsync(cancellationToken);
            if (homeUsers.Any(user => Matches(user, account)))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch Plex Home users; allowing rater {AccountId} ({AccountTitle})",
                account.Id,
                account.Title);
            return true;
        }
    }

    private async Task<IReadOnlyList<PlexHomeUser>> GetHomeUsersAsync(CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(PlexCacheKeys.HomeUsers, out IReadOnlyList<PlexHomeUser>? cached) && cached != null)
        {
            return cached;
        }

        var token = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexToken);
        var clientIdentifier = await _settingService.GetSetting(SettingKeys.Integration.PlexClientIdentifier);
        if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(clientIdentifier))
        {
            throw new InvalidOperationException("Plex token or client identifier is not configured.");
        }

        var users = await _plexTvClient.GetHomeUsersAsync(token, clientIdentifier, cancellationToken);
        _cache.Set(PlexCacheKeys.HomeUsers, users, HomeUsersCacheDuration);
        return users;
    }

    private static bool Matches(PlexHomeUser user, PlexWebhookAccount account)
    {
        if (user.Id != 0 && user.Id == account.Id)
        {
            return true;
        }

        return NamesEqual(user.Title, account.Title)
               || NamesEqual(user.Username, account.Title)
               || NamesEqual(user.FriendlyName, account.Title);
    }

    private static bool NamesEqual(string? left, string? right)
    {
        return !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
