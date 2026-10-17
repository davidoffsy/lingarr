using Lingarr.Contracts.Exceptions;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Resolves OpenAI-compatible completion and model-catalogue URLs from a user-supplied
/// endpoint. Accepts a bare host, an endpoint with a <c>/v1</c> suffix, or a full API
/// path; known API suffixes are stripped from the URI path before the protocol path is
/// appended so switching between "base URL" and "full endpoint" input keeps working.
/// Query strings are preserved. Azure OpenAI deployment paths are left untouched.
/// </summary>
public static class OpenAiCompatibleEndpoint
{
    private static readonly string[] KnownApiSuffixes =
    [
        "/v1/chat/completions",
        "/chat/completions",
        "/v1/responses",
        "/responses",
        "/v1/messages",
        "/messages",
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
        if (TryParseHttpUrl(endpoint, out var uri))
        {
            return uri.AbsolutePath.TrimEnd('/').EndsWith("completions", StringComparison.OrdinalIgnoreCase);
        }

        return TrimSlash(endpoint).EndsWith("completions", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses <paramref name="endpoint"/> as an absolute http or https URL.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="endpoint"/> is null or blank.
    /// </exception>
    /// <exception cref="TranslationConfigurationException">
    /// Thrown when the value is not an absolute http(s) URL.
    /// </exception>
    public static Uri RequireHttpUrl(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!TryParseHttpUrl(endpoint, out var uri))
        {
            throw new TranslationConfigurationException(
                $"Endpoint '{endpoint}' is not a usable http(s) URL.");
        }

        return uri;
    }

    public static string StripKnownApiPaths(string endpoint)
    {
        var uri = RequireHttpUrl(endpoint);
        if (IsAzureDeployment(uri.AbsolutePath))
        {
            return OriginAndPath(uri);
        }

        return Combine(uri, StripSuffixes(uri.AbsolutePath), query: null);
    }

    public static string ResolveChatCompletionsUrl(string endpoint) =>
        ResolveApiUrl(endpoint, "/chat/completions");

    public static string ResolveResponsesUrl(string endpoint) =>
        ResolveApiUrl(endpoint, "/responses");

    public static string ResolveMessagesUrl(string endpoint) =>
        ResolveApiUrl(endpoint, "/messages");

    public static string ResolveModelsUrl(string endpoint) =>
        ResolveApiUrl(endpoint, "/models");

    private static string ResolveApiUrl(string endpoint, string apiPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var uri = RequireHttpUrl(endpoint);
        if (IsAzureDeployment(uri.AbsolutePath))
        {
            // Rewriting would insert /v1 after the deployment id and drop api-version.
            return OriginAndPath(uri) + uri.Query;
        }

        var path = StripSuffixes(uri.AbsolutePath);
        var resolvedPath = path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? path + apiPath
            : path + "/v1" + apiPath;
        return Combine(uri, resolvedPath, uri.Query);
    }

    private static bool TryParseHttpUrl(string endpoint, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static bool IsAzureDeployment(string absolutePath) =>
        absolutePath.Contains("/deployments/", StringComparison.OrdinalIgnoreCase);

    private static string StripSuffixes(string absolutePath)
    {
        var path = absolutePath.TrimEnd('/');
        foreach (var suffix in KnownApiSuffixes)
        {
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^suffix.Length].TrimEnd('/');
                break;
            }
        }

        return path;
    }

    private static string OriginAndPath(Uri uri) =>
        uri.GetLeftPart(UriPartial.Path).TrimEnd('/');

    private static string Combine(Uri uri, string absolutePath, string? query)
    {
        var builder = new UriBuilder(uri)
        {
            Path = string.IsNullOrEmpty(absolutePath) ? "/" : absolutePath,
            Query = string.IsNullOrEmpty(query) ? string.Empty : query.TrimStart('?'),
            Fragment = string.Empty
        };
        var pathPart = builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (string.IsNullOrEmpty(query))
        {
            return pathPart;
        }

        return pathPart + (query.StartsWith('?') ? query : "?" + query);
    }
}
