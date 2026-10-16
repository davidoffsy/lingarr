using System.Text.Json;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Models.Integrations.Translation;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Reads the assistant text out of a non-streaming OpenAI Responses payload
/// (<c>output[].content[].text</c>, or the optional top-level <c>output_text</c>).
/// A response is only treated as success when it completed with text: refusals,
/// <c>incomplete</c>/<c>failed</c>/non-final states, gateway errors and empty output raise
/// <see cref="TranslationUnusableResponseException"/> instead, so none of them can be written
/// back as a translation, retried like a rate limit, or counted as a successful line.
/// </summary>
public static class OpenAiResponsesPayloadParser
{
    private const string CompletedStatus = "completed";
    private const string OutputTextBlock = "output_text";

    /// <summary>
    /// Returns the assistant text of <paramref name="responseBody"/> or throws a
    /// <see cref="TranslationUnusableResponseException"/> describing why it is unusable.
    /// The exception is terminal: the same request would fail again, so callers must not retry it.
    /// </summary>
    /// <param name="responseBody">Raw JSON body of a successful HTTP response.</param>
    /// <param name="serviceLabel">Provider label used in error messages.</param>
    public static string ExtractText(string responseBody, string serviceLabel)
    {
        OpenAiResponsesResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<OpenAiResponsesResponse>(responseBody);
        }
        catch (JsonException ex)
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} returned a response that is not valid Responses JSON.", ex);
        }

        if (response == null)
        {
            throw new TranslationUnusableResponseException($"{serviceLabel} returned an empty Responses payload.");
        }

        if (HasError(response.Error))
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} returned an error response instead of a translation ({Describe(response.Error)}).");
        }

        var status = response.Status?.Trim();
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslationUnusableResponseException($"{serviceLabel} reported a failed response.");
        }

        if (string.Equals(status, "incomplete", StringComparison.OrdinalIgnoreCase))
        {
            var reason = response.IncompleteDetails?.Reason;
            throw new TranslationUnusableResponseException(string.IsNullOrWhiteSpace(reason)
                ? $"{serviceLabel} returned an incomplete response."
                : $"{serviceLabel} returned an incomplete response ({reason}).");
        }

        if (!string.IsNullOrEmpty(status)
            && !string.Equals(status, CompletedStatus, StringComparison.OrdinalIgnoreCase))
        {
            // A non-streaming call is expected to come back terminal; queued/in_progress and
            // cancelled payloads must not be parsed as a translation.
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} returned a non-final response (status '{status}').");
        }

        if (FindRefusal(response) is not null)
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} refused the request instead of returning a translation.");
        }

        var text = CollectOutputText(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = response.OutputText;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new TranslationUnusableResponseException($"{serviceLabel} returned an empty response.");
        }

        return text;
    }

    private static bool HasError(ResponsesError? error) =>
        error is not null
        && (!string.IsNullOrWhiteSpace(error.Message) || !string.IsNullOrWhiteSpace(error.Code));

    private static string Describe(ResponsesError? error)
    {
        if (error is null)
        {
            return "no error details";
        }

        var message = string.IsNullOrWhiteSpace(error.Message) ? null : error.Message.Trim();
        var code = string.IsNullOrWhiteSpace(error.Code) ? null : error.Code.Trim();
        return (message, code) switch
        {
            (null, null) => "no error details",
            (not null, null) => message!,
            (null, not null) => $"code {code}",
            _ => $"{code}: {message}"
        };
    }

    private static string? FindRefusal(OpenAiResponsesResponse response)
    {
        foreach (var item in response.Output)
        {
            if (item.Content is null)
            {
                continue;
            }

            foreach (var content in item.Content)
            {
                if (string.Equals(content.Type, "refusal", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(content.Refusal))
                {
                    return content.Refusal ?? content.Type;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Joins the text of every <c>output_text</c> block of the message items; reasoning and tool
    /// call items never contribute text even when a gateway puts some into their content.
    /// </summary>
    private static string CollectOutputText(OpenAiResponsesResponse response)
    {
        var parts = new List<string>();
        foreach (var item in response.Output)
        {
            if (!IsMessageItem(item) || item.Content is null)
            {
                continue;
            }

            foreach (var content in item.Content)
            {
                if (!IsTextBlock(content))
                {
                    continue;
                }

                parts.Add(content.Text!);
            }
        }

        return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool IsMessageItem(ResponsesOutputItem item) =>
        string.IsNullOrEmpty(item.Type)
        || string.Equals(item.Type, "message", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextBlock(ResponsesOutputContent content) =>
        !string.IsNullOrWhiteSpace(content.Text)
        && (string.IsNullOrEmpty(content.Type)
            || string.Equals(content.Type, OutputTextBlock, StringComparison.OrdinalIgnoreCase));
}
