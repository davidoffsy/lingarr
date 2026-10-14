using Lingarr.Core.Enum;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Interfaces.Services.Plex;

/// <summary>
/// Turns Plex-only text subtitles (OpenSubtitles sideloads stored in PMS metadata)
/// into sidecar files Lingarr can translate.
/// </summary>
public interface IPlexSubtitleImporter
{
    Task<IReadOnlyList<PlexSourceSubtitleCandidate>> ListAsync(
        string? ratingKey,
        MediaType mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the selected Plex stream next to the video as
    /// <c>{fileName}.{lang}[.caption]{ext}</c>. Returns the sidecar path, or
    /// <c>null</c> if the file could not be written.
    /// </summary>
    Task<string?> MaterializeAsync(
        PlexSourceSubtitleCandidate candidate,
        string mediaDirectory,
        string mediaFileName,
        CancellationToken cancellationToken = default);
}
