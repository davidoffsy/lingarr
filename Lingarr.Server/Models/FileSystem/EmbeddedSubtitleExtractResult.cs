namespace Lingarr.Server.Models.FileSystem;

/// <summary>
/// Result of copying an embedded text track to a sidecar. Failures include a user-visible message
/// so manual extract-and-translate requests can be marked Failed instead of looking like "no subtitles".
/// </summary>
public class EmbeddedSubtitleExtractResult
{
    public const string MissingMkv = "MKV file is missing or unreadable.";
    public const string UnsupportedTrack = "Subtitle track cannot be extracted.";
    public const string MissingFfmpeg = "ffmpeg was not found; cannot extract embedded subtitles.";
    public const string EmptyOutput = "ffmpeg produced an empty subtitle file.";
    public const string FfmpegFailed = "ffmpeg failed while extracting embedded subtitles.";

    public string? SidecarPath { get; init; }
    public string? ErrorMessage { get; init; }
    public bool Succeeded => !string.IsNullOrEmpty(SidecarPath);

    public static EmbeddedSubtitleExtractResult Ok(string sidecarPath) => new()
    {
        SidecarPath = sidecarPath
    };

    public static EmbeddedSubtitleExtractResult Fail(string errorMessage) => new()
    {
        ErrorMessage = errorMessage
    };

    public static string TimedOut(TimeSpan timeout)
    {
        var minutes = Math.Max(1, (int)Math.Round(timeout.TotalMinutes));
        var unit = minutes == 1 ? "minute" : "minutes";
        return $"Extracting embedded subtitles timed out after {minutes} {unit}.";
    }
}
