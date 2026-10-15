using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;
using Microsoft.Extensions.Caching.Memory;

namespace Lingarr.Server.Services.Plex;

public class PlexAuthService : IPlexAuthService
{
    private static readonly TimeSpan PinSessionTtl = TimeSpan.FromMinutes(15);

    private readonly IPlexTvClient _plexTvClient;
    private readonly IPlexApiClient _plexApiClient;
    private readonly ISettingService _settingService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PlexAuthService> _logger;

    public PlexAuthService(
        IPlexTvClient plexTvClient,
        IPlexApiClient plexApiClient,
        ISettingService settingService,
        IMemoryCache cache,
        ILogger<PlexAuthService> logger)
    {
        _plexTvClient = plexTvClient;
        _plexApiClient = plexApiClient;
        _settingService = settingService;
        _cache = cache;
        _logger = logger;
    }

    public async Task<PlexPinCreatedResponse> CreatePinAsync(CancellationToken cancellationToken = default)
    {
        var clientIdentifier = await EnsureClientIdentifierAsync();
        await EnsureWebhookSecretAsync(cancellationToken);

        var pin = await _plexTvClient.CreatePinAsync(clientIdentifier, cancellationToken);
        _cache.Set(
            PlexCacheKeys.Pin(pin.Id),
            new PlexPinSession(pin.Id, pin.Code, clientIdentifier),
            PinSessionTtl);

        return new PlexPinCreatedResponse
        {
            PinId = pin.Id,
            Code = pin.Code,
            AuthUrl = PlexAuthUrl.Build(clientIdentifier, pin.Code)
        };
    }

    public async Task<PlexPinStatusResponse> PollPinAsync(int pinId, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(PlexCacheKeys.Pin(pinId), out PlexPinSession? session) || session == null)
        {
            return new PlexPinStatusResponse { Linked = false };
        }

        var pin = await _plexTvClient.GetPinAsync(pinId, session.ClientIdentifier, session.Code, cancellationToken);
        if (string.IsNullOrEmpty(pin.AuthToken))
        {
            return new PlexPinStatusResponse { Linked = false };
        }

        await _settingService.SetEncryptedSetting(SettingKeys.Integration.PlexToken, pin.AuthToken);

        var accountTitle = await TryGetAccountTitleAsync(pin.AuthToken, session.ClientIdentifier, cancellationToken);
        await _settingService.SetSetting(SettingKeys.Integration.PlexAccountTitle, accountTitle);
        await RefreshSettingsCompletedAsync();

        _cache.Remove(PlexCacheKeys.Pin(pinId));
        _cache.Remove(PlexCacheKeys.HomeUsers);

        return new PlexPinStatusResponse
        {
            Linked = true,
            AccountTitle = accountTitle
        };
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _settingService.SetEncryptedSetting(SettingKeys.Integration.PlexToken, string.Empty);
        await _settingService.SetSetting(SettingKeys.Integration.PlexMachineId, string.Empty);
        await _settingService.SetSetting(SettingKeys.Integration.PlexAccountTitle, string.Empty);
        await _settingService.SetSetting(SettingKeys.Integration.PlexSettingsCompleted, "false");
        _cache.Remove(PlexCacheKeys.HomeUsers);
    }

    public async Task<IReadOnlyList<PlexServerDto>> GetServersAsync(CancellationToken cancellationToken = default)
    {
        var token = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Sign in with Plex first.");
        }

        var clientIdentifier = await EnsureClientIdentifierAsync();
        var resources = await _plexTvClient.GetResourcesAsync(token, clientIdentifier, cancellationToken);

        return resources
            .Where(IsOwnedServer)
            .Select(ToServerDto)
            .ToList();
    }

    public async Task<PlexConnectionTestResult> TestConnectionAsync(
        string? urlOverride,
        CancellationToken cancellationToken = default)
    {
        var token = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexToken);
        var url = string.IsNullOrWhiteSpace(urlOverride)
            ? await _settingService.GetSetting(SettingKeys.Integration.PlexUrl)
            : urlOverride.TrimEnd('/');

        if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(url))
        {
            return new PlexConnectionTestResult
            {
                Success = false,
                Message = "Sign in with Plex and set a server address first."
            };
        }

        var identity = await _plexApiClient.GetIdentity(url.TrimEnd('/'), token, cancellationToken);
        if (identity == null)
        {
            return new PlexConnectionTestResult
            {
                Success = false,
                Message = "Could not reach Plex Media Server. Check the address and that Lingarr can access it."
            };
        }

        await RefreshSettingsCompletedAsync();
        return new PlexConnectionTestResult
        {
            Success = true,
            MachineIdentifier = identity.MachineIdentifier,
            Version = identity.Version,
            Message = "Connected"
        };
    }

    public async Task<string> EnsureWebhookSecretAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        return await PersistWebhookSecretAsync();
    }

    public async Task<string> RegenerateWebhookSecretAsync(CancellationToken cancellationToken = default)
    {
        return await PersistWebhookSecretAsync();
    }

    private async Task<string> PersistWebhookSecretAsync()
    {
        var secret = WebhookSecretGenerator.Generate();
        var saved = await _settingService.SetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret, secret);
        if (!saved)
        {
            throw new InvalidOperationException(
                "Failed to persist plex_webhook_secret. Was the database migrated?");
        }

        return secret;
    }

    private async Task<string> EnsureClientIdentifierAsync()
    {
        var existing = await _settingService.GetSetting(SettingKeys.Integration.PlexClientIdentifier);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var created = Guid.NewGuid().ToString();
        await _settingService.SetSetting(SettingKeys.Integration.PlexClientIdentifier, created);
        return created;
    }

    private async Task RefreshSettingsCompletedAsync()
    {
        var token = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexToken);
        var url = await _settingService.GetSetting(SettingKeys.Integration.PlexUrl);
        var completed = !string.IsNullOrEmpty(token) && !string.IsNullOrWhiteSpace(url);
        await _settingService.SetSetting(
            SettingKeys.Integration.PlexSettingsCompleted,
            completed ? "true" : "false");
    }

    private async Task<string> TryGetAccountTitleAsync(
        string accessToken,
        string clientIdentifier,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await _plexTvClient.GetUserAsync(accessToken, clientIdentifier, cancellationToken);
            if (!string.IsNullOrWhiteSpace(user?.Title))
            {
                return user.Title;
            }

            if (!string.IsNullOrWhiteSpace(user?.Username))
            {
                return user.Username;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Plex account title after PIN claim");
        }

        return "Plex account";
    }

    private static bool IsOwnedServer(PlexTvResource resource)
    {
        if (!resource.Owned)
        {
            return false;
        }

        var provides = resource.Provides ?? string.Empty;
        return provides
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains("server", StringComparer.OrdinalIgnoreCase);
    }

    private static PlexServerDto ToServerDto(PlexTvResource resource)
    {
        var connections = (resource.Connections ?? [])
            .Where(connection => !string.IsNullOrWhiteSpace(connection.Uri))
            .Select(connection => new PlexConnectionDto
            {
                Uri = connection.Uri!.TrimEnd('/'),
                Local = connection.Local,
                Relay = connection.Relay
            })
            .ToList();

        var preferred = connections.FirstOrDefault(c => c.Local && !c.Relay)
                        ?? connections.FirstOrDefault(c => !c.Relay)
                        ?? connections.FirstOrDefault();

        return new PlexServerDto
        {
            Name = resource.Name ?? "Plex Media Server",
            MachineId = resource.ClientIdentifier ?? string.Empty,
            PreferredUri = preferred?.Uri,
            Connections = connections
        };
    }
}
