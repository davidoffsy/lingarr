using System.Text.Json;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

public static class PlexMetadataMapper
{
    public static PlexMetadata FromJson(JsonElement item)
    {
        var media = PlexJsonElement.ReadMedia(item);
        return new PlexMetadata
        {
            Type = PlexJsonElement.GetString(item, "type"),
            Title = PlexJsonElement.GetString(item, "title"),
            Year = PlexJsonElement.GetInt32(item, "year"),
            GrandparentRatingKey = PlexJsonElement.GetString(item, "grandparentRatingKey"),
            GrandparentTitle = PlexJsonElement.GetString(item, "grandparentTitle"),
            ParentIndex = PlexJsonElement.GetInt32(item, "parentIndex"),
            Index = PlexJsonElement.GetInt32(item, "index"),
            File = PlexMediaFileSelector.SelectFile(media),
            Guids = PlexJsonElement.CollectGuidIds(item),
            SubtitleStreams = PlexMediaFileSelector.SelectSubtitleStreams(media)
        };
    }

    public static JsonElement? FirstMetadataItem(JsonElement root)
    {
        var container = PlexJsonElement.GetProperty(root, "MediaContainer");
        var metadata = PlexJsonElement.GetProperty(container, "Metadata");
        if (metadata.ValueKind == JsonValueKind.Array)
        {
            return metadata.GetArrayLength() == 0 ? null : metadata[0];
        }

        return metadata.ValueKind == JsonValueKind.Object ? metadata : null;
    }
}
