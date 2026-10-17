using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Protocol a Custom provider speaks. Every value maps to one non-streaming adapter and one
/// independently stored request template, so switching protocols never rewrites the other
/// protocol's template.
/// </summary>
public enum CustomProtocol
{
    OpenAiChat,
    OpenAiResponses,
    AnthropicMessages
}

public static class CustomProtocols
{
    /// <summary>
    /// Parses the <c>custom_protocol</c> setting. Empty and missing values keep the historical
    /// Chat Completions behaviour of pre-protocol records; an unknown value returns false so the
    /// caller can report a configuration error instead of guessing a protocol.
    /// </summary>
    public static bool TryParse(string? value, out CustomProtocol protocol)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            protocol = CustomProtocol.OpenAiChat;
            return true;
        }

        // Trim and ignore case so an environment value such as "OPENAI_RESPONSES" selects the same
        // protocol the request-template editor displays.
        var normalized = value.Trim();
        if (string.Equals(normalized, SettingKeys.Translation.Custom.Protocols.OpenAiChat, StringComparison.OrdinalIgnoreCase))
        {
            protocol = CustomProtocol.OpenAiChat;
            return true;
        }

        if (string.Equals(normalized, SettingKeys.Translation.Custom.Protocols.OpenAiResponses, StringComparison.OrdinalIgnoreCase))
        {
            protocol = CustomProtocol.OpenAiResponses;
            return true;
        }

        if (string.Equals(normalized, SettingKeys.Translation.Custom.Protocols.AnthropicMessages, StringComparison.OrdinalIgnoreCase))
        {
            protocol = CustomProtocol.AnthropicMessages;
            return true;
        }

        protocol = CustomProtocol.OpenAiChat;
        return false;
    }

    /// <summary>The supported setting values, used in configuration error messages.</summary>
    public static string SupportedValues =>
        $"'{SettingKeys.Translation.Custom.Protocols.OpenAiChat}', '{SettingKeys.Translation.Custom.Protocols.OpenAiResponses}' or '{SettingKeys.Translation.Custom.Protocols.AnthropicMessages}'";
}
