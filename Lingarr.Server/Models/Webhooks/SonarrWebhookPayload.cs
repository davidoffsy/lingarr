using System.Text.Json.Serialization;

namespace Lingarr.Server.Models.Webhooks;

public class SonarrWebhookPayload
{
    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("series")]
    public SonarrWebhookSeries? Series { get; set; }

    [JsonPropertyName("episodes")]
    public List<SonarrWebhookEpisode>? Episodes { get; set; }
}

public class SonarrWebhookSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public class SonarrWebhookEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
