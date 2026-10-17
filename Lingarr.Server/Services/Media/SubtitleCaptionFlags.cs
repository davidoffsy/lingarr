namespace Lingarr.Server.Services.Media;

/// <summary>
/// Forced / SDH signals for source ranking. Sidecar files only expose these via
/// the filename caption token; embedded tracks also have MKV disposition flags.
/// </summary>
public static class SubtitleCaptionFlags
{
    public static bool IsForced(string? caption, bool embeddedFlag = false) =>
        embeddedFlag || ContainsFlag(caption, "forced");

    public static bool IsHearingImpaired(string? caption, bool embeddedFlag = false) =>
        embeddedFlag
        || ContainsFlag(caption, "sdh")
        || ContainsFlag(caption, "hi")
        || ContainsFlag(caption, "cc");

    private static bool ContainsFlag(string? caption, string flag) =>
        !string.IsNullOrEmpty(caption)
        && caption.Contains(flag, StringComparison.OrdinalIgnoreCase);
}
