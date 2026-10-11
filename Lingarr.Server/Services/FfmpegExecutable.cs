namespace Lingarr.Server.Services;

/// <summary>
/// Resolves ffmpeg/ffprobe executable names, honoring optional path overrides.
/// </summary>
public static class FfmpegExecutable
{
    public const string FfmpegPathVariable = "FFMPEG_PATH";
    public const string FfprobePathVariable = "FFPROBE_PATH";

    public static string Ffmpeg => GetPath(FfmpegPathVariable, "ffmpeg");
    public static string Ffprobe => GetPath(FfprobePathVariable, "ffprobe");

    private static string GetPath(string variable, string fallback)
    {
        var configured = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
    }
}
