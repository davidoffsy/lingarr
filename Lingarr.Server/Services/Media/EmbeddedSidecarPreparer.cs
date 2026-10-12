using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Media;

/// <summary>
/// Resolves an <see cref="EmbeddedSubtitlePlaceholder"/> into a real sidecar path via ffmpeg extract.
/// </summary>
public static class EmbeddedSidecarPreparer
{
    public static async Task<string?> EnsureAsync(
        IEmbeddedSubtitleService embeddedSubtitleService,
        string? subtitleToTranslate,
        CancellationToken cancellationToken,
        IProgress<FfmpegExtractProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(embeddedSubtitleService);

        if (!EmbeddedSubtitlePlaceholder.TryParse(subtitleToTranslate, out var mkvPath, out var trackIndex))
        {
            return subtitleToTranslate;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var probe = await embeddedSubtitleService.ProbeDetailedAsync(mkvPath, cancellationToken);
        var track = probe.Tracks.FirstOrDefault(candidate => candidate.Index == trackIndex);
        if (track == null)
        {
            throw new InvalidOperationException(
                $"Embedded subtitle track {trackIndex} is no longer extractable in {Path.GetFileName(mkvPath)}.");
        }

        var extract = await embeddedSubtitleService.ExtractDetailedAsync(
            mkvPath,
            track,
            cancellationToken,
            progress,
            probe.DurationSeconds);
        if (!extract.Succeeded)
        {
            throw new InvalidOperationException(
                extract.ErrorMessage ??
                $"Failed to extract embedded subtitle track {trackIndex} from {Path.GetFileName(mkvPath)}.");
        }

        return extract.SidecarPath;
    }
}
