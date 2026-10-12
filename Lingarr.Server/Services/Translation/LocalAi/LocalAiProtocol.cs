using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Translation.LocalAi;

public enum LocalAiProtocol
{
    Auto,
    OpenAiChat,
    OllamaGenerate
}

public static class LocalAiProtocolParser
{
    public static LocalAiProtocol Parse(string? value)
    {
        if (string.Equals(value, SettingKeys.Translation.LocalAi.Protocols.OpenAiChat, StringComparison.OrdinalIgnoreCase))
        {
            return LocalAiProtocol.OpenAiChat;
        }

        if (string.Equals(value, SettingKeys.Translation.LocalAi.Protocols.OllamaGenerate, StringComparison.OrdinalIgnoreCase))
        {
            return LocalAiProtocol.OllamaGenerate;
        }

        return LocalAiProtocol.Auto;
    }
}
