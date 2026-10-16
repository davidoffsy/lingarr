namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// OpenCode Go requires a stable <c>x-opencode-session</c> header for routing.
/// Only <c>*.opencode.ai</c> hosts with a <c>/zen/go</c> path qualify; other
/// OpenAI-compatible gateways must not receive this vendor header.
/// </summary>
public static class OpenCodeGoHeaders
{
    public const string SessionHeaderName = "x-opencode-session";

    public static bool Matches(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && Matches(uri);

    public static bool Matches(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        return IsOpenCodeHost(uri.Host) && HasZenGoPath(uri.AbsolutePath);
    }

    public static string NewSessionId() => Guid.NewGuid().ToString("N");

    private static bool IsOpenCodeHost(string host) =>
        host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".opencode.ai", StringComparison.OrdinalIgnoreCase);

    private static bool HasZenGoPath(string absolutePath)
    {
        var path = absolutePath.TrimEnd('/');
        return path.Equals("/zen/go", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/zen/go/", StringComparison.OrdinalIgnoreCase);
    }
}
