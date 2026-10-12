using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Lingarr.Core;

namespace Lingarr.Server.Services.Translation.LocalAi;

internal static class LocalAiHttp
{
    public static string UserAgent => $"{LingarrVersion.Name}/{LingarrVersion.Number}";

    public static HttpRequestMessage CreateJsonPost(
        string url,
        string jsonBody,
        string? apiKey,
        string? sessionId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyBearer(request, apiKey);
        ApplyClientHeaders(request, sessionId);
        return request;
    }

    public static void ApplyBearer(HttpRequestMessage request, string? apiKey)
    {
        var token = apiKey?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        // TryAddWithoutValidation keeps keys with RFC-invalid token characters (common in
        // gateway tokens) instead of dropping the header during send.
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
    }

    public static void ApplyClientHeaders(HttpRequestMessage request, string? sessionId = null)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (!OpenCodeGoHeaders.Matches(request.RequestUri))
        {
            return;
        }

        var value = string.IsNullOrWhiteSpace(sessionId)
            ? OpenCodeGoHeaders.NewSessionId()
            : sessionId.Trim();
        request.Headers.TryAddWithoutValidation(OpenCodeGoHeaders.SessionHeaderName, value);
    }

    public static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
}
