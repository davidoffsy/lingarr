using System;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Webhooks;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Webhooks;

public class ArrWebhookSecretServiceTests
{
    private readonly Mock<ISettingService> _settings = new();
    private readonly ArrWebhookSecretService _service;

    public ArrWebhookSecretServiceTests()
    {
        _service = new ArrWebhookSecretService(_settings.Object);
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.OnboardingCompleted))
            .ReturnsAsync("true");
    }

    [Fact]
    public async Task Authenticate_OnboardingIncomplete_IsRejected()
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.OnboardingCompleted))
            .ReturnsAsync("false");

        var result = await _service.AuthenticateAsync("anything");

        Assert.False(result.IsAllowed);
        Assert.True(result.OnboardingRequired);
    }

    [Fact]
    public async Task Authenticate_EmptySecret_AuthDisabled_IsAllowed()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync(string.Empty);
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.AuthEnabled))
            .ReturnsAsync("false");

        var result = await _service.AuthenticateAsync(null);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Authenticate_EmptySecret_AuthEnabled_IsUnauthorized()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync(string.Empty);
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.AuthEnabled))
            .ReturnsAsync("true");

        var result = await _service.AuthenticateAsync("token");

        Assert.False(result.IsAllowed);
        Assert.False(result.OnboardingRequired);
        Assert.Contains("not configured", result.Message);
    }

    [Fact]
    public async Task Authenticate_WrongToken_IsUnauthorized()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync("expected-secret");

        var result = await _service.AuthenticateAsync("wrong");

        Assert.False(result.IsAllowed);
        Assert.Contains("Invalid webhook token", result.Message);
    }

    [Fact]
    public async Task Authenticate_MatchingToken_IsAllowed()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync("expected-secret");

        var result = await _service.AuthenticateAsync("expected-secret");

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Authenticate_MatchingToken_AuthDisabled_StillRequiresToken()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync("expected-secret");
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.AuthEnabled))
            .ReturnsAsync("false");

        var result = await _service.AuthenticateAsync(null);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task GetSecret_ReturnsExisting()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync("already-there");

        var secret = await _service.GetSecretAsync();

        Assert.Equal("already-there", secret);
        _settings.Verify(
            s => s.SetEncryptedSetting(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSecret_Missing_ReturnsEmptyWithoutWriting()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret))
            .ReturnsAsync(string.Empty);

        var secret = await _service.GetSecretAsync();

        Assert.Equal(string.Empty, secret);
        _settings.Verify(
            s => s.SetEncryptedSetting(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RegenerateSecret_WritesNewSecret()
    {
        _settings.Setup(s => s.SetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret, It.IsAny<string>()))
            .ReturnsAsync(true);

        var secret = await _service.RegenerateSecretAsync();

        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.Equal(43, secret.Length);
        _settings.Verify(
            s => s.SetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret, secret),
            Times.Once);
    }

    [Fact]
    public async Task RegenerateSecret_WhenPersistFails_Throws()
    {
        _settings.Setup(s => s.SetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret, It.IsAny<string>()))
            .ReturnsAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.RegenerateSecretAsync());
    }
}
