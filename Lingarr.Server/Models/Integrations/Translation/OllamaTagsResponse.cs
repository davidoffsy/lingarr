using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Integrations.Translation;

public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaTagModel>? Models { get; set; }
}

public class OllamaTagModel
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}
