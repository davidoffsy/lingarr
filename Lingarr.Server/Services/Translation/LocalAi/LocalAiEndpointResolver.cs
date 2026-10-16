using Lingarr.Server.Services.Translation.OpenAiCompatible;

namespace Lingarr.Server.Services.Translation.LocalAi;

/// <summary>
/// Resolves LocalAI completion and model-catalogue URLs from a user-supplied endpoint and protocol.
/// Auto mode preserves full completion/generate paths, and completes a bare host
/// (optionally with /v1) to the generate protocol. Explicit protocols accept either
/// a base URL or a full API path. The OpenAI-compatible chat/models paths are shared with
/// the Custom provider through <see cref="OpenAiCompatibleEndpoint"/>.
/// </summary>
public static class LocalAiEndpointResolver
{
    public static string TrimSlash(string url) => OpenAiCompatibleEndpoint.TrimSlash(url);

    public static bool LooksLikeChatCompletions(string endpoint) =>
        OpenAiCompatibleEndpoint.LooksLikeChatCompletions(endpoint);

    public static string StripKnownApiPaths(string endpoint) =>
        OpenAiCompatibleEndpoint.StripKnownApiPaths(endpoint);

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

    private static string BuildChatCompletionsUrl(string trimmed) =>
        OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(trimmed);

    private static string BuildGenerateUrl(string trimmed)
    {
        return NativeOrigin(trimmed) + "/api/generate";
    }

    private static string BuildModelsUrl(string trimmed) =>
        OpenAiCompatibleEndpoint.ResolveModelsUrl(trimmed);

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
