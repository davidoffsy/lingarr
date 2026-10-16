using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.RequestTemplates;

/// <summary>
/// Default request body for the non-streaming OpenAI Responses protocol: the system and user
/// prompts travel as <c>input</c> messages instead of chat <c>messages</c>.
/// </summary>
public class OpenAiResponsesTemplate
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "{model}";

    [JsonPropertyName("input")]
    public List<ResponsesInputMessage> Input { get; set; } =
    [
        new()
        {
            Role = "system",
            Content = "{systemPrompt}"
        },
        new()
        {
            Role = "user",
            Content = "{userMessage}"
        }
    ];
}

public class ResponsesInputMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
