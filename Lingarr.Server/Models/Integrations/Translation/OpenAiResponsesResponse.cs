using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations.Translation;

/// <summary>
/// Terminal payload of the OpenAI Responses API. Only the fields needed to decide between
/// "usable text" and "no usable text" are modelled; unknown fields are ignored on purpose so a
/// gateway that adds new ones does not break parsing.
/// </summary>
public class OpenAiResponsesResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("incomplete_details")]
    public ResponsesIncompleteDetails? IncompleteDetails { get; set; }

    [JsonPropertyName("error")]
    public ResponsesError? Error { get; set; }

    [JsonPropertyName("output")]
    public List<ResponsesOutputItem> Output { get; set; } = [];

    /// <summary>
    /// Convenience field some gateways and SDKs add next to <c>output</c>. Used only when the
    /// standard output blocks carry no text.
    /// </summary>
    [JsonPropertyName("output_text")]
    public string? OutputText { get; set; }
}

public class ResponsesIncompleteDetails
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public class ResponsesError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class ResponsesOutputItem
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("content")]
    public List<ResponsesOutputContent>? Content { get; set; }
}

public class ResponsesOutputContent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("refusal")]
    public string? Refusal { get; set; }
}
