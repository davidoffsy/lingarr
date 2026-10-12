namespace Lingarr.Server.Models.FileSystem;

/// <summary>
/// Result of probing an MKV for subtitle streams, including tracks that cannot be extracted.
/// </summary>
public class EmbeddedSubtitleProbeResult
{
    public const string ReasonNoMkv = "no_mkv";
    public const string ReasonNoFfprobe = "no_ffprobe";
    public const string ReasonTimeout = "timeout";
    public const string ReasonProbeFailed = "probe_failed";

    public const string SkipReasonImage = "image";
    public const string SkipReasonUnsupported = "unsupported";

    public bool Found { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<EmbeddedSubtitleTrack> Tracks { get; init; } = [];
    public IReadOnlyList<SkippedEmbeddedSubtitleTrack> Skipped { get; init; } = [];
    public double? DurationSeconds { get; init; }

    public static EmbeddedSubtitleProbeResult NotFound(string reason) => new()
    {
        Found = false,
        Reason = reason
    };

    public static EmbeddedSubtitleProbeResult Success(
        IReadOnlyList<EmbeddedSubtitleTrack> tracks,
        IReadOnlyList<SkippedEmbeddedSubtitleTrack> skipped,
        double? durationSeconds = null) => new()
    {
        Found = true,
        Tracks = tracks,
        Skipped = skipped,
        DurationSeconds = durationSeconds
    };
}

public class SkippedEmbeddedSubtitleTrack
{
    public int Index { get; init; }
    public string CodecName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
