using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

public static class PlexMediaFileSelector
{
    public static string? SelectFile(IReadOnlyList<PlexMedia>? media)
    {
        if (media == null || media.Count == 0)
        {
            return null;
        }

        var selectedMedia = media.FirstOrDefault(item => item.Selected) ?? media[0];
        var parts = selectedMedia.Part;
        if (parts == null || parts.Count == 0)
        {
            return null;
        }

        var selectedPart = parts.FirstOrDefault(part => part.Selected) ?? parts[0];
        return string.IsNullOrWhiteSpace(selectedPart.File) ? null : selectedPart.File;
    }
}
