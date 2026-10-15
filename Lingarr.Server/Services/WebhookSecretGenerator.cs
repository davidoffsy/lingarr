using System.Security.Cryptography;

namespace Lingarr.Server.Services;

public static class WebhookSecretGenerator
{
    public static string Generate()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-")[..43];
    }
}
