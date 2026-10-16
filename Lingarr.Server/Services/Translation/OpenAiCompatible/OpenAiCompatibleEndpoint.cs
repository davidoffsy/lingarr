namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Resolves OpenAI-compatible completion and model-catalogue URLs from a user-supplied
/// endpoint. Accepts a bare host, an endpoint with a <c>/v1</c> suffix, or a full API
/// path; known API suffixes are stripped before the protocol path is appended so
/// switching between "base URL" and "full endpoint" input keeps working.
/// </summary>
public static class OpenAiCompatibleEndpoint
{
    private static readonly string[] KnownApiSuffixes =
    [
        "/v1/chat/completions",
        "/chat/completions",
        "/v1/responses",
        "/responses",
        "/v1/completions",
        "/completions",
        "/api/generate",
        "/api/tags",
        "/v1/models",
        "/models"
    ];

    public static string TrimSlash(string url) => url.Trim().TrimEnd('/');

    public static bool LooksLikeChatCompletions(string endpoint) =>
        TrimSlash(endpoint).EndsWith("completions", StringComparison.OrdinalIgnoreCase);

    public static string StripKnownApiPaths(string endpoint)
    {
        var trimmed = TrimSlash(endpoint);
        foreach (var suffix in KnownApiSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[..^suffix.Length].TrimEnd('/');
            }
        }

        return trimmed;
    }

    public static string ResolveChatCompletionsUrl(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var root = StripKnownApiPaths(endpoint);
        return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? root + "/chat/completions"
            : root + "/v1/chat/completions";
    }

    public static string ResolveResponsesUrl(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var root = StripKnownApiPaths(endpoint);
        return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? root + "/responses"
            : root + "/v1/responses";
    }

    public static string ResolveModelsUrl(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var root = StripKnownApiPaths(endpoint);
        return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? root + "/models"
            : root + "/v1/models";
    }
}
