using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Entities;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class SettingControllerPlexRedactionTests
{
    private readonly Mock<ISettingService> _settings = new();
    private readonly SettingController _controller;

    public SettingControllerPlexRedactionTests()
    {
        _controller = new SettingController(_settings.Object);
    }

    [Fact]
    public async Task GetEncryptedSettings_RedactsPlexToken_ButReturnsWebhookSecret()
    {
        _settings.Setup(s => s.GetEncryptedSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, string>
            {
                [SettingKeys.Integration.PlexToken] = "super-secret-token",
                [SettingKeys.Integration.PlexWebhookSecret] = "webhook-secret"
            });

        var result = await _controller.GetEncryptedSettings(new[]
        {
            SettingKeys.Integration.PlexToken,
            SettingKeys.Integration.PlexWebhookSecret
        });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<Dictionary<string, string>>(ok.Value);
        Assert.Equal(string.Empty, body[SettingKeys.Integration.PlexToken]);
        Assert.Equal("webhook-secret", body[SettingKeys.Integration.PlexWebhookSecret]);
    }

    [Fact]
    public async Task GetSetting_RedactsPlexToken()
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexToken))
            .ReturnsAsync("encrypted-blob");

        var result = await _controller.GetSetting(SettingKeys.Integration.PlexToken);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(string.Empty, ok.Value);
        _settings.Verify(s => s.GetSetting(SettingKeys.Integration.PlexToken), Times.Never);
    }

    [Fact]
    public async Task SetEncryptedSetting_RejectsPlexToken()
    {
        var result = await _controller.SetEncryptedSetting(new Setting
        {
            Key = SettingKeys.Integration.PlexToken,
            Value = "should-not-write"
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Sign in with Plex", badRequest.Value?.ToString());
        _settings.Verify(
            s => s.SetEncryptedSetting(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
