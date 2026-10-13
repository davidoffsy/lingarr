using Lingarr.Server.Interfaces.Services;

namespace Lingarr.Server.Services.Translation;

public enum AiThinkingMode
{
    Auto,
    Off,
    On
}

/// <summary>
/// Parses the shared <c>ai_thinking</c> setting and maps it to OpenAI-compatible chat
/// request fields. Unknown values fall back to <see cref="AiThinkingMode.Auto"/> so
/// providers that reject a <c>thinking</c> key are not sent one.
/// </summary>
public static class AiThinking
{
    public const string Auto = "auto";
    public const string Off = "off";
    public const string On = "on";

    public static AiThinkingMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AiThinkingMode.Off;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, Off, StringComparison.OrdinalIgnoreCase))
        {
            return AiThinkingMode.Off;
        }

        if (string.Equals(trimmed, On, StringComparison.OrdinalIgnoreCase))
        {
            return AiThinkingMode.On;
        }

        if (string.Equals(trimmed, Auto, StringComparison.OrdinalIgnoreCase))
        {
            return AiThinkingMode.Auto;
        }

        return AiThinkingMode.Auto;
    }

    public static Dictionary<string, object?>? ToRequestFields(AiThinkingMode mode)
    {
        return mode switch
        {
            AiThinkingMode.Off => new Dictionary<string, object?>
            {
                ["thinking"] = new { type = "disabled" }
            },
            AiThinkingMode.On => new Dictionary<string, object?>
            {
                ["thinking"] = new { type = "enabled" }
            },
            _ => null
        };
    }

    public static string Apply(IRequestTemplateService templates, string bodyJson, AiThinkingMode mode)
    {
        var fields = ToRequestFields(mode);
        return fields is null ? bodyJson : templates.SetRequestFields(bodyJson, fields);
    }
}
