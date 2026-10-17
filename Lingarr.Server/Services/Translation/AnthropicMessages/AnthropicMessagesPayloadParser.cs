using System.Text.Json;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation.Batch;

namespace Lingarr.Server.Services.Translation.AnthropicMessages;

/// <summary>
/// Reads a non-streaming Anthropic Messages payload. Text lives in <c>content[]</c> blocks of
/// type <c>text</c>; a batch may instead come back as a <c>tool_use</c> block whose
/// <c>input.translations</c> carries the position map. Thinking blocks never contribute text.
/// </summary>
public static class AnthropicMessagesPayloadParser
{
    public const string ToolParseFailureMessage = "Failed to parse translated subtitles";

    /// <summary>
    /// Strict read used by Custom. A refusal, a <c>max_tokens</c> truncation, an empty body and
    /// invalid JSON are terminal: none of them may be written back as a translation or retried
    /// like a rate limit.
    /// </summary>
    public static string ExtractText(string responseBody, string serviceLabel)
    {
        var root = ParseStrict(responseBody, serviceLabel);
        RejectUnusableStop(root, serviceLabel);

        var text = CollectText(root);
        if (string.IsNullOrWhiteSpace(text))
        {
            var stop = StopReason(root) ?? "unknown";
            throw new TranslationUnusableResponseException(HasContent(root)
                ? $"{serviceLabel} returned an empty response (stop_reason: {stop})."
                : $"{serviceLabel} returned no content (stop_reason: {stop}).");
        }

        return text;
    }

    /// <summary>
    /// Lenient read used by the official Anthropic service. Partial text is returned even when
    /// <c>stop_reason</c> is <c>max_tokens</c> or <c>refusal</c>, matching the historical behaviour
    /// of that provider. Invalid JSON is left to the caller.
    /// </summary>
    public static string ExtractTextLenient(string responseBody, string serviceLabel)
    {
        var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
        var stopReason = StopReason(jsonResponse);

        if (!jsonResponse.TryGetProperty("content", out var contentArray) ||
            contentArray.GetArrayLength() == 0)
        {
            throw new TranslationException(
                $"{serviceLabel} returned no content (stop_reason: {stopReason ?? "unknown"}).");
        }

        JsonElement? textContent = null;
        foreach (var contentItem in contentArray.EnumerateArray())
        {
            if (!contentItem.TryGetProperty("type", out var typeProperty) ||
                typeProperty.GetString() != "text")
            {
                continue;
            }

            textContent = contentItem;
            break;
        }

        if (!textContent.HasValue ||
            !textContent.Value.TryGetProperty("text", out var textProperty))
        {
            throw new TranslationException(
                $"{serviceLabel} response contained no text block (stop_reason: {stopReason ?? "unknown"}).");
        }

        return textProperty.GetString() ?? throw new InvalidOperationException();
    }

    /// <summary>
    /// Reads <c>content[].tool_use.input.translations</c>. A missing tool block raises
    /// <see cref="TranslationException"/> so a strict caller can fall back to plain text; a refusal
    /// or truncation raises <see cref="TranslationUnusableResponseException"/> when
    /// <paramref name="strict"/> is true and must not be retried as a different request shape.
    /// </summary>
    public static Dictionary<int, string> ExtractToolTranslations(
        string responseBody,
        string serviceLabel,
        bool strict)
    {
        JsonElement jsonResponse;
        try
        {
            jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);
        }
        catch (JsonException ex)
        {
            if (strict)
            {
                throw new TranslationUnusableResponseException(
                    $"{serviceLabel} returned a response that is not valid Messages JSON.", ex);
            }

            throw;
        }

        if (strict)
        {
            RejectUnusableStop(jsonResponse, serviceLabel);
        }

        var stopReason = StopReason(jsonResponse);
        if (!jsonResponse.TryGetProperty("content", out var contentArray) ||
            contentArray.GetArrayLength() == 0)
        {
            throw new TranslationException(
                $"{serviceLabel} returned no content (stop_reason: {stopReason ?? "unknown"}).");
        }

        JsonElement? toolUseContent = null;
        foreach (var contentItem in contentArray.EnumerateArray())
        {
            if (contentItem.TryGetProperty("type", out var typeProperty) &&
                typeProperty.GetString() == "tool_use")
            {
                toolUseContent = contentItem;
                break;
            }
        }

        if (!toolUseContent.HasValue ||
            !toolUseContent.Value.TryGetProperty("input", out var inputProperty) ||
            !inputProperty.TryGetProperty("translations", out var translationsProperty))
        {
            throw new TranslationException(
                $"{serviceLabel} response contained no tool_use block (stop_reason: {stopReason ?? "unknown"}).");
        }

        try
        {
            var translatedItems = translationsProperty.EnumerateArray()
                .Select(item => new StructuredBatchResponse
                {
                    Position = item.GetProperty("position").GetInt32(),
                    Line = item.GetProperty("line").GetString() ?? string.Empty
                })
                .ToList();

            return BatchResponseMerger.MergeByPosition(translatedItems);
        }
        catch (Exception ex) when (ex is not TranslationException)
        {
            throw new TranslationException(ToolParseFailureMessage, ex);
        }
    }

    private static JsonElement ParseStrict(string responseBody, string serviceLabel)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new TranslationUnusableResponseException(
                    $"{serviceLabel} returned a response that is not a Messages JSON object.");
            }

            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} returned a response that is not valid Messages JSON.", ex);
        }
    }

    private static void RejectUnusableStop(JsonElement root, string serviceLabel)
    {
        if (IsRefusal(root))
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} refused the request instead of returning a translation.");
        }

        if (string.Equals(StopReason(root), "max_tokens", StringComparison.OrdinalIgnoreCase))
        {
            throw new TranslationUnusableResponseException(
                $"{serviceLabel} returned a truncated response (stop_reason: max_tokens).");
        }
    }

    private static bool IsRefusal(JsonElement root)
    {
        if (string.Equals(StopReason(root), "refusal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "refusal", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CollectText(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.TryGetProperty("text", out var textElement))
            {
                continue;
            }

            var text = textElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                parts.Add(text);
            }
        }

        // Concatenate without a separator: Claude may split one reply across several text
        // blocks, and a subtitle line must not gain newlines that the model did not emit.
        return string.Concat(parts);
    }

    private static bool HasContent(JsonElement root) =>
        root.TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.Array
        && content.GetArrayLength() > 0;

    private static string? StopReason(JsonElement root)
    {
        if (!root.TryGetProperty("stop_reason", out var stop) || stop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return stop.GetString();
    }
}
