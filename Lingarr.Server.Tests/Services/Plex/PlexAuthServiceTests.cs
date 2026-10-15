using System;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Plex;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexAuthServiceTests
{
    private readonly Mock<IPlexTvClient> _plexTv = new();
    private readonly Mock<IPlexApiClient> _plexApi = new();
    private readonly Mock<ISettingService> _settings = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly PlexAuthService _service;

    public PlexAuthServiceTests()
    {
        _settings.Setup(s => s.SetSetting(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _settings.Setup(s => s.SetEncryptedSetting(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        _settings.Setup(s => s.GetSetting(It.IsAny<string>())).ReturnsAsync(string.Empty);
        _settings.Setup(s => s.GetEncryptedSetting(It.IsAny<string>())).ReturnsAsync(string.Empty);
        _service = new PlexAuthService(
            _plexTv.Object,
            _plexApi.Object,
            _settings.Object,
            _cache,
            NullLogger<PlexAuthService>.Instance);
    }

    [Fact]
    public async Task CreatePin_ReturnsAuthUrlWithClientIdAndCode()
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexClientIdentifier))
            .ReturnsAsync("client-uuid-1");
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("existing-secret");
        _plexTv.Setup(c => c.CreatePinAsync("client-uuid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexPin { Id = 42, Code = "ABCD" });

        var result = await _service.CreatePinAsync();

        Assert.Equal(42, result.PinId);
        Assert.Equal("ABCD", result.Code);
        Assert.Equal(PlexAuthUrl.Build("client-uuid-1", "ABCD"), result.AuthUrl);
    }

    [Fact]
    public async Task PollPin_WhenAuthTokenPresent_WritesEncryptedToken()
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexClientIdentifier))
            .ReturnsAsync("client-uuid-1");
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("existing-secret");
        _plexTv.Setup(c => c.CreatePinAsync("client-uuid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexPin { Id = 7, Code = "CODE" });
        await _service.CreatePinAsync();

        _plexTv.Setup(c => c.GetPinAsync(7, "client-uuid-1", "CODE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexPin { Id = 7, Code = "CODE", AuthToken = "plex-access-token" });
        _plexTv.Setup(c => c.GetUserAsync("plex-access-token", "client-uuid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexTvUser { Title = "OwnerName" });

        var status = await _service.PollPinAsync(7);

        Assert.True(status.Linked);
        Assert.Equal("OwnerName", status.AccountTitle);
        _settings.Verify(
            s => s.SetEncryptedSetting(SettingKeys.Integration.PlexToken, "plex-access-token"),
            Times.Once);
        _settings.Verify(
            s => s.SetSetting(SettingKeys.Integration.PlexAccountTitle, "OwnerName"),
            Times.Once);
    }

    [Fact]
    public async Task PollPin_WhenNotClaimed_DoesNotWriteToken()
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexClientIdentifier))
            .ReturnsAsync("client-uuid-1");
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("existing-secret");
        _plexTv.Setup(c => c.CreatePinAsync("client-uuid-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexPin { Id = 8, Code = "WAIT" });
        await _service.CreatePinAsync();

        _plexTv.Setup(c => c.GetPinAsync(8, "client-uuid-1", "WAIT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexPin { Id = 8, Code = "WAIT", AuthToken = null });

        var status = await _service.PollPinAsync(8);

        Assert.False(status.Linked);
        _settings.Verify(
            s => s.SetEncryptedSetting(SettingKeys.Integration.PlexToken, It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task PollPin_WhenSessionExpired_DoesNotWriteToken()
    {
        var status = await _service.PollPinAsync(999);

        Assert.False(status.Linked);
        _plexTv.Verify(
            c => c.GetPinAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _settings.Verify(
            s => s.SetEncryptedSetting(SettingKeys.Integration.PlexToken, It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task Disconnect_ClearsTokenAndKeepsWebhookSecret()
    {
        await _service.DisconnectAsync();

        _settings.Verify(s => s.SetEncryptedSetting(SettingKeys.Integration.PlexToken, string.Empty), Times.Once);
        _settings.Verify(s => s.SetSetting(SettingKeys.Integration.PlexMachineId, string.Empty), Times.Once);
        _settings.Verify(s => s.SetSetting(SettingKeys.Integration.PlexAccountTitle, string.Empty), Times.Once);
        _settings.Verify(s => s.SetSetting(SettingKeys.Integration.PlexSettingsCompleted, "false"), Times.Once);
        _settings.Verify(
            s => s.SetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret, It.IsAny<string>()),
            Times.Never);
        _settings.Verify(
            s => s.SetSetting(SettingKeys.Integration.PlexClientIdentifier, It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RegenerateWebhookSecret_WhenPersistFails_Throws()
    {
        _settings.Setup(s => s.SetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret, It.IsAny<string>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegenerateWebhookSecretAsync());
    }
}
