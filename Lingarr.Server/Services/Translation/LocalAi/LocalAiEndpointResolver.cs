namespace Lingarr.Server.Services.Translation.LocalAi;

/// <summary>
/// Resolves LocalAI completion and model-catalogue URLs from a user-supplied endpoint and protocol.
/// Auto mode preserves full completion/generate paths, and completes a bare host
/// (optionally with /v1) to the generate protocol. Explicit protocols accept either
/// a base URL or a full API path.
/// </summary>
public static class LocalAiEndpointResolver
{
    private static readonly string[] KnownApiSuffixes =
    [
        "/v1/chat/completions",
        "/chat/completions",
        "/v1/completions",
        "/completions",
        "/api/generate",
        "/api/tags",
        "/v1/models",
        "/models"
    ];

    public static string TrimSlash(string url) => url.Trim().TrimEnd('/');

    public static bool LooksLikeChatCompletions(string endpoint)
    {
        return TrimSlash(endpoint).EndsWith("completions", StringComparison.OrdinalIgnoreCase);
    }

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

    public static LocalAiResolvedEndpoint ResolveCompletionEndpoint(string endpoint, LocalAiProtocol protocol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var trimmed = TrimSlash(endpoint);
        return protocol switch
        {
            LocalAiProtocol.OpenAiChat => new LocalAiResolvedEndpoint(BuildChatCompletionsUrl(trimmed), true),
            LocalAiProtocol.OllamaGenerate => new LocalAiResolvedEndpoint(BuildGenerateUrl(trimmed), false),
            _ => ResolveAutoCompletion(trimmed)
        };
    }

    public static string ResolveModelsUrl(string endpoint, LocalAiProtocol protocol)
    {
        var trimmed = TrimSlash(endpoint);
        var effective = protocol == LocalAiProtocol.Auto
            ? LooksLikeChatCompletions(trimmed)
                ? LocalAiProtocol.OpenAiChat
                : LocalAiProtocol.OllamaGenerate
            : protocol;

        return effective == LocalAiProtocol.OpenAiChat
            ? BuildModelsUrl(trimmed)
            : BuildTagsUrl(trimmed);
    }

    private static LocalAiResolvedEndpoint ResolveAutoCompletion(string trimmed)
    {
        if (LooksLikeChatCompletions(trimmed))
        {
            return new LocalAiResolvedEndpoint(trimmed, true);
        }

        // Bare host (and optional /v1) used to be POSTed as-is, so Test connection could
        // succeed against /api/tags while translation hit the origin and failed.
        if (LooksLikeBareBase(trimmed))
        {
            return new LocalAiResolvedEndpoint(BuildGenerateUrl(trimmed), false);
        }

        return new LocalAiResolvedEndpoint(trimmed, false);
    }

    private static bool LooksLikeBareBase(string endpoint)
    {
        var trimmed = TrimSlash(endpoint);
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return path.Length == 0 || string.Equals(path, "/v1", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildChatCompletionsUrl(string trimmed)
    {
        var root = StripKnownApiPaths(trimmed);
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return root + "/chat/completions";
        }

        return root + "/v1/chat/completions";
    }

    private static string BuildGenerateUrl(string trimmed)
    {
        return NativeOrigin(trimmed) + "/api/generate";
    }

    private static string BuildModelsUrl(string trimmed)
    {
        var root = StripKnownApiPaths(trimmed);
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return root + "/models";
        }

        return root + "/v1/models";
    }

    private static string BuildTagsUrl(string trimmed)
    {
        return NativeOrigin(trimmed) + "/api/tags";
    }

    private static string NativeOrigin(string trimmed)
    {
        var root = StripKnownApiPaths(trimmed);
        if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return root[..^3].TrimEnd('/');
        }

        return root;
    }
}
