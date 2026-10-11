using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Probes MKV containers for extractable text subtitle tracks and copies them to sidecar files.
/// Image-based tracks and WebVTT are ignored in phase 1.
/// </summary>
public interface IEmbeddedSubtitleService
{
    /// <summary>
    /// Lists extractable text subtitle tracks in an MKV file.
    /// Returns an empty list when ffprobe is missing, times out, or the file cannot be read.
    /// </summary>
    Task<IReadOnlyList<EmbeddedSubtitleTrack>> ProbeAsync(
        string mkvPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a probed text track to a Plex/Emby-compatible sidecar next to the video.
    /// Does not overwrite a non-empty existing sidecar. Returns the sidecar path, or <c>null</c> on failure.
    /// </summary>
    Task<string?> ExtractAsync(
        string mkvPath,
        EmbeddedSubtitleTrack track,
        CancellationToken cancellationToken = default);
}
