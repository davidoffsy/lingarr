namespace Lingarr.Server.Services.Media;

/// <summary>
/// Builds the ffmpeg extract command. The temp sidecar uses a <c>.extracting</c> suffix so
/// scanners ignore incomplete files; <c>-f</c> must be set because ffmpeg cannot guess the muxer
/// from that suffix.
/// </summary>
public static class FfmpegExtractCommand
{
    public static bool TryGetMuxer(string? extension, out string muxer)
    {
        muxer = extension?.Trim().ToLowerInvariant() switch
        {
            ".srt" => "srt",
            ".ass" => "ass",
            ".ssa" => "ssa",
            _ => string.Empty
        };

        return muxer.Length > 0;
    }

    public static IReadOnlyList<string> BuildArguments(
        string mkvPath,
        int trackIndex,
        string muxer,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mkvPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(muxer);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegative(trackIndex);

        return
        [
            "-y",
            "-i", mkvPath,
            "-map", $"0:{trackIndex}",
            "-c:s", "copy",
            "-progress", "pipe:1",
            "-nostats",
            "-f", muxer,
            outputPath
        ];
    }
}
