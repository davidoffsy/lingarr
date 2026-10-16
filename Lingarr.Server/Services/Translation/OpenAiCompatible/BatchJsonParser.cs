using System.Text.Json;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation.Batch;
using Microsoft.Extensions.Logging;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Parses batch translation payloads the model returned as text. The structured wrapper
/// (<c>{"translations":[{"position":1,"line":"..."}]}</c>) is preferred; the plain-JSON fallback
/// extracts the first array from a chatty answer, because not every gateway enforces structured
/// output. Unparsable text raises <see cref="TranslationParseException"/> so the caller can retry.
/// </summary>
public static class BatchJsonParser
{
    /// <summary>
    /// Reads the structured wrapper object. Shared by the Chat Completions and Responses adapters.
    /// </summary>
    public static Dictionary<int, string> ParseStructuredWrapper(string modelText, ILogger logger)
    {
        try
        {
            var wrapper = JsonSerializer.Deserialize<JsonElement>(modelText);
            if (wrapper.ValueKind != JsonValueKind.Object
                || !wrapper.TryGetProperty("translations", out var translationsElement))
            {
                throw new TranslationParseException("Response does not contain 'translations' property");
            }

            var translatedItems =
                JsonSerializer.Deserialize<List<StructuredBatchResponse>>(translationsElement.GetRawText());
            if (translatedItems == null)
            {
                throw new TranslationParseException("Failed to deserialize translated subtitles");
            }

            return BatchResponseMerger.MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse structured JSON response: {Json}", modelText);
            throw new TranslationParseException("Failed to parse structured translated subtitles", ex);
        }
        catch (TranslationParseException ex)
        {
            logger.LogError(ex, "Failed to parse structured JSON response: {Json}", modelText);
            throw;
        }
    }

    /// <summary>
    /// Extracts the first JSON array from <paramref name="modelText"/> and reads it as batch items.
    /// </summary>
    public static Dictionary<int, string> ParseJsonArray(string modelText, ILogger logger)
    {
        try
        {
            var translatedItems = JsonSerializer.Deserialize<List<StructuredBatchResponse>>(
                ExtractJsonArray(modelText),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (translatedItems == null)
            {
                throw new TranslationParseException("Failed to deserialize translated subtitles from JSON parsing");
            }

            return BatchResponseMerger.MergeByPosition(translatedItems);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse JSON response: {Json}", modelText);
            throw new TranslationParseException("Failed to parse JSON translated subtitles", ex);
        }
        catch (TranslationParseException ex)
        {
            logger.LogError(ex, "Failed to parse JSON response: {Json}", modelText);
            throw;
        }
    }

    private static string ExtractJsonArray(string modelText)
    {
        var jsonStart = modelText.IndexOf('[');
        var jsonEnd = modelText.LastIndexOf(']');
        return jsonStart != -1 && jsonEnd != -1 && jsonEnd > jsonStart
            ? modelText.Substring(jsonStart, jsonEnd - jsonStart + 1)
            : modelText;
    }
}
