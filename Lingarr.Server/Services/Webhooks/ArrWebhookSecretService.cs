using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Services.Webhooks;

public class ArrWebhookSecretService : IArrWebhookSecretService
{
    private readonly ISettingService _settingService;

    public ArrWebhookSecretService(ISettingService settingService)
    {
        _settingService = settingService;
    }

    public async Task<string> GetSecretAsync(CancellationToken cancellationToken = default)
    {
        return await _settingService.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret) ?? string.Empty;
    }

    public Task<string> RegenerateSecretAsync(CancellationToken cancellationToken = default)
    {
        return PersistSecretAsync();
    }

    public async Task<ArrWebhookAuthResult> AuthenticateAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        var onboardingCompleted = await _settingService.GetSetting(SettingKeys.Authentication.OnboardingCompleted);
        if (!string.Equals(onboardingCompleted, "true", StringComparison.OrdinalIgnoreCase))
        {
            return ArrWebhookAuthResult.RequireOnboarding();
        }

        var secret = await _settingService.GetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret);
        if (string.IsNullOrEmpty(secret))
        {
            var authEnabled = await _settingService.GetSetting(SettingKeys.Authentication.AuthEnabled);
            if (authEnabled == "false")
            {
                return ArrWebhookAuthResult.Allowed();
            }

            return ArrWebhookAuthResult.Unauthorized("Arr webhook secret is not configured");
        }

        if (!SecretComparer.Equals(token, secret))
        {
            return ArrWebhookAuthResult.Unauthorized("Invalid webhook token");
        }

        return ArrWebhookAuthResult.Allowed();
    }

    private async Task<string> PersistSecretAsync()
    {
        var secret = WebhookSecretGenerator.Generate();
        var saved = await _settingService.SetEncryptedSetting(SettingKeys.Integration.ArrWebhookSecret, secret);
        if (!saved)
        {
            throw new InvalidOperationException(
                "Failed to persist arr_webhook_secret. Was the database migrated?");
        }

        return secret;
    }
}
