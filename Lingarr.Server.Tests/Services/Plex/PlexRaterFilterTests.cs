using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Plex;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexRaterFilterTests
{
    private readonly Mock<IPlexTvClient> _plexTv = new();
    private readonly Mock<ISettingService> _settings = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly PlexRaterFilter _filter;

    public PlexRaterFilterTests()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexToken))
            .ReturnsAsync("access-token");
        _settings.Setup(s => s.GetSetting(It.IsAny<string>()))
            .ReturnsAsync(string.Empty);
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexClientIdentifier))
            .ReturnsAsync("client-id");
        _filter = new PlexRaterFilter(
            _plexTv.Object,
            _settings.Object,
            _cache,
            NullLogger<PlexRaterFilter>.Instance);
    }

    [Fact]
    public async Task Home_AccountId1_AllowsOwner()
    {
        SetupMode("home");

        var allowed = await _filter.IsAllowedAsync(new PlexWebhookAccount { Id = 1, Title = "Owner" });

        Assert.True(allowed);
        _plexTv.Verify(
            c => c.GetHomeUsersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Home_ManagedUserMatchingHomeList_AllowsEvenIfOwnerFalse()
    {
        SetupMode("home");
        SetupHomeUsers(new PlexHomeUser { Id = 50, Title = "Kids" });

        var allowed = await _filter.IsAllowedAsync(new PlexWebhookAccount { Id = 50, Title = "Kids" });

        Assert.True(allowed);
    }

    [Fact]
    public async Task Home_FriendNotInList_Skips()
    {
        SetupMode("home");
        SetupHomeUsers(new PlexHomeUser { Id = 50, Title = "Kids" });

        var allowed = await _filter.IsAllowedAsync(new PlexWebhookAccount { Id = 99, Title = "Remote Friend" });

        Assert.False(allowed);
    }

    [Fact]
    public async Task Anyone_AnyAccount_Allows()
    {
        SetupMode("anyone");

        var allowed = await _filter.IsAllowedAsync(new PlexWebhookAccount { Id = 99, Title = "Remote Friend" });

        Assert.True(allowed);
        _plexTv.Verify(
            c => c.GetHomeUsersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Home_OwnerFlagTrueButFriendAccount_StillSkips()
    {
        SetupMode("home");
        SetupHomeUsers(new PlexHomeUser { Id = 50, Title = "Kids" });

        var payload = new PlexWebhookParser().Parse("""
            {
              "event": "media.rate",
              "owner": true,
              "Account": { "id": 99, "title": "Remote Friend" },
              "Metadata": { "type": "movie" }
            }
            """);

        Assert.NotNull(payload);
        Assert.True(payload!.Owner);

        var allowed = await _filter.IsAllowedAsync(payload.Account);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Home_SignedInAccountTitle_AllowsEvenIfIdIsNotOne()
    {
        SetupMode("home");
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexAccountTitle))
            .ReturnsAsync("wf2022");

        var allowed = await _filter.IsAllowedAsync(new PlexWebhookAccount { Id = 123456, Title = "wf2022" });

        Assert.True(allowed);
        _plexTv.Verify(
            c => c.GetHomeUsersAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Home_HomeUserFetchFails_Allows()
    {
        SetupMode("home");
        _plexTv.Setup(c => c.GetHomeUsersAsync("access-token", "client-id", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("plex.tv unavailable"));

        var allowed = await _filter.IsAllowedAsync(new PlexWebhookAccount { Id = 77, Title = "TV" });

        Assert.True(allowed);
    }

    private void SetupMode(string mode)
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexAllowedRaters))
            .ReturnsAsync(mode);
    }

    private void SetupHomeUsers(params PlexHomeUser[] users)
    {
        _plexTv.Setup(c => c.GetHomeUsersAsync("access-token", "client-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlexHomeUser>(users));
    }
}
