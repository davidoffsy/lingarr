namespace Lingarr.Server.Models.Plex;

/// <summary>
/// A sidecar or extractable embedded track considered as a Plex translation source.
/// Forced / SDH flags are mapped here so ranking does not mutate <c>Subtitles</c>.
/// </summary>
public class PlexSourceSubtitleCandidate
{
    public required string Path { get; init; }
    public required string Language { get; init; }
    public string Caption { get; init; } = string.Empty;
    public required string Format { get; init; }
    public bool IsEmbedded { get; init; }
    public int TrackIndex { get; init; } = -1;
    public int SidecarOrder { get; init; }
    public bool IsForced { get; init; }
    public bool IsHearingImpaired { get; init; }

    /// <summary>
    /// True when the bytes still live in Plex (OpenSubtitles sideload, etc.)
    /// and must be downloaded before <see cref="Path"/> is a real sidecar.
    /// </summary>
    public bool IsPlexStream { get; init; }

    /// <summary>
    /// PMS path such as <c>/library/streams/12345</c>.
    /// </summary>
    public string? StreamKey { get; init; }
}
