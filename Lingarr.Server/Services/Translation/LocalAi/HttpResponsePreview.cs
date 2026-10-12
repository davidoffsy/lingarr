using System.Net.Http.Headers;

namespace Lingarr.Server.Services.Translation.LocalAi;

internal static class HttpResponsePreview
{
    public static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Format(response.Content.Headers.ContentType, body);
    }

    public static string Format(MediaTypeHeaderValue? contentType, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var mediaType = contentType?.MediaType ?? string.Empty;
        var trimmed = body.TrimStart();
        var looksLikeHtml = mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith('<')
            || trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase);

        if (looksLikeHtml)
        {
            return "HTML page, not a JSON API response. If this is an OpenAI-compatible gateway, set Protocol to OpenAI Chat Completions and use the base URL (for example https://opencode.ai/zen/go/v1).";
        }

        return string.Empty;
    }
}
