namespace Lingarr.Server.Models.Plex;

/// <summary>
/// A Plex <c>streamType=3</c> subtitle. Agent downloads such as OpenSubtitles
/// usually have a <see cref="Key"/> under <c>/library/streams/{id}</c> and no
/// sidecar next to the video file.
/// </summary>
public class PlexSubtitleStream
{
    public const int SubtitleStreamType = 3;

    public int Id { get; init; }
    public int StreamType { get; init; }
    public string? Key { get; init; }
    public string? File { get; init; }
    public string? Codec { get; init; }
    public string? Format { get; init; }
    public string? LanguageTag { get; init; }
    public string? LanguageCode { get; init; }
    public string? Title { get; init; }
    public bool Forced { get; init; }
    public bool HearingImpaired { get; init; }
}
