using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Parses the shared <c>ai_max_tokens</c> setting and maps it onto chat / Gemini
/// request bodies. Missing or empty values default to 8192 (the seed). Zero
/// omits the field so the provider or template default is kept. Negative and
/// non-integer values are treated as zero.
/// </summary>
public static class AiMaxTokens
{
    public const int Default = 8192;

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
    /// OpenAI-compatible gateways (LocalAI, OpenCode Go). Writes both names so a
    /// server that only honours one of them still gets a cap. A 400 on the extra
    /// field is retried without it by LocalAI.
    /// </summary>
    public static string ApplyOpenAiCompatible(IRequestTemplateService templates, string bodyJson, int maxTokens)
    {
        if (maxTokens <= 0)
        {
            return bodyJson;
        }

        return templates.SetRequestFields(bodyJson, new Dictionary<string, object?>
        {
            ["max_tokens"] = maxTokens,
            ["max_completion_tokens"] = maxTokens
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
