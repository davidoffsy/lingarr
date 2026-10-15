using Lingarr.Server.Models.Webhooks;

namespace Lingarr.Server.Interfaces.Services;

public interface IArrWebhookSecretService
{
    Task<string> GetSecretAsync(CancellationToken cancellationToken = default);

    Task<string> RegenerateSecretAsync(CancellationToken cancellationToken = default);

    Task<ArrWebhookAuthResult> AuthenticateAsync(string? token, CancellationToken cancellationToken = default);
}
