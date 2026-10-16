using System.Text.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Server.Exceptions;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Enforces the non-streaming contract of the Custom protocols. A request template that asks for
/// SSE (<c>"stream": true</c>) is rejected before it is sent, and a gateway that answers with an
/// event stream is rejected instead of being parsed as a half-finished JSON body — a streamed
/// fragment would otherwise surface as an empty or missing translation.
/// </summary>
public static class NonStreamingPolicy
{
    /// <summary>
    /// Throws <see cref="TranslationConfigurationException"/> when the built request body asks for
    /// streaming. Streaming cannot be downgraded silently: the template has to be fixed, so the
    /// translation chain can skip this provider instead of retrying an unsupported request.
    /// </summary>
    public static void EnsureRequestIsNotStreaming(string bodyJson, string serviceLabel)
    {
        using var document = JsonDocument.Parse(bodyJson);
        if (!document.RootElement.TryGetProperty("stream", out var stream))
        {
            return;
        }

        var streams = stream.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(stream.GetString(), out var parsed) && parsed,
            _ => false
        };

        if (streams)
        {
            throw new TranslationConfigurationException(
                $"{serviceLabel} request template sets \"stream\": true, but only non-streaming responses are supported. Remove the field or set it to false.");
        }
    }

    /// <summary>
    /// Throws <see cref="TranslationUnusableResponseException"/> when a successful response is an
    /// event stream instead of one JSON document. The failure is terminal: retrying it, or sending
    /// the same template again as a JSON fallback, would only fetch another event stream.
    /// </summary>
    /// <param name="mediaType">Response <c>Content-Type</c> media type, when present.</param>
    /// <param name="body">Body that was read from the response.</param>
    /// <param name="serviceLabel">Provider label used in the error message.</param>
    public static void EnsureResponseIsNotStreaming(string? mediaType, string body, string serviceLabel)
    {
        var isEventStream =
            (mediaType?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false)
            || body.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase);

        if (isEventStream)
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} returned a streaming (SSE) response, but only non-streaming responses are supported. Disable streaming on the gateway or in the request template.");
        }
    }
}
