namespace Lingarr.Server.Models.Webhooks;

public sealed class ArrWebhookAuthResult
{
    public bool IsAllowed { get; private init; }
    public bool OnboardingRequired { get; private init; }
    public string Message { get; private init; } = string.Empty;

    public static ArrWebhookAuthResult Allowed() => new() { IsAllowed = true };

    public static ArrWebhookAuthResult RequireOnboarding() => new()
    {
        OnboardingRequired = true,
        Message = "Onboarding required"
    };

    public static ArrWebhookAuthResult Unauthorized(string message) => new() { Message = message };
}
