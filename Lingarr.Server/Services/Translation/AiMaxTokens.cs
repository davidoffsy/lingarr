using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Parses the shared <c>ai_max_tokens</c> setting and maps it onto chat / Gemini
/// request bodies. Missing, empty, negative, and non-integer values default to
/// zero, which omits the field so the provider or template default is kept.
/// </summary>
public static class AiMaxTokens
{
    public const int Default = 0;

    public static int Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        if (!int.TryParse(value.Trim(), out var parsed) || parsed < 0)
        {
            return 0;
        }

        return parsed;
    }

    public static string ApplyChat(IRequestTemplateService templates, string bodyJson, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return bodyJson;
        }

        return templates.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["max_tokens"] = maxTokens
        });
    }

    /// <summary>
    /// Official OpenAI chat completions: reasoning models reject <c>max_tokens</c>
    /// and require <c>max_completion_tokens</c>.
    /// </summary>
    public static string ApplyOpenAi(IRequestTemplateService templates, string bodyJson, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return bodyJson;
        }

        return templates.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["max_completion_tokens"] = maxTokens
        });
    }

    /// <summary>
    /// OpenAI Responses: the output budget is <c>max_output_tokens</c>, not <c>max_tokens</c>.
    /// </summary>
    public static string ApplyResponses(IRequestTemplateService templates, string bodyJson, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return bodyJson;
        }

        return templates.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["max_output_tokens"] = maxTokens
        });
    }

    public static string ApplyGemini(IRequestTemplateService templates, string bodyJson, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return bodyJson;
        }

        return templates.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["generationConfig"] = new Dictionary<string, object?>
            {
                ["maxOutputTokens"] = maxTokens
            }
        });
    }
}
