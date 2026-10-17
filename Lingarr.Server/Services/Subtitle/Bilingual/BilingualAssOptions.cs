namespace Lingarr.Server.Services.Subtitle.Bilingual;

/// <summary>
/// Hard-coded v1 canvas and style values for bilingual ASS sidecars.
/// Sizes are relative to a fixed 1920×1080 PlayRes; players scale with the script
/// resolution, so video resolution is never probed.
/// </summary>
public static class BilingualAssOptions
{
    public const int PlayResX = 1920;
    public const int PlayResY = 1080;
    public const int WrapStyle = 0;
    public const string ScriptType = "v4.00+";

    public const string FontName = "Noto Sans CJK SC";
    public const string TargetStyleName = "LingarrTarget";
    public const string SourceStyleName = "LingarrSource";

    public const int TargetFontSize = 64;
    public const int SourceFontSize = 38;

    /// <summary>White, ASS <c>&amp;HAABBGGRR</c>.</summary>
    public const string TargetPrimaryColour = "&H00FFFFFF";

    /// <summary>Light yellow <c>#FFD966</c> as ASS BGR <c>&amp;H0066D9FF</c>.</summary>
    public const string SourcePrimaryColour = "&H0066D9FF";

    public const string SecondaryColour = "&H000000FF";
    public const string OutlineColour = "&H00000000";
    public const string BackColour = "&H00000000";

    public const int Outline = 2;
    public const int Shadow = 1;
    public const int Alignment = 2;
    public const int MarginL = 20;
    public const int MarginR = 20;
    public const int MarginV = 16;
    public const int Encoding = 1;

    public const string StylesFormat =
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, " +
        "Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, " +
        "Alignment, MarginL, MarginR, MarginV, Encoding";

    public const string EventsFormat =
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text";

    public static string TargetStyleLine() => StyleLine(TargetStyleName, TargetFontSize, TargetPrimaryColour);

    public static string SourceStyleLine() => StyleLine(SourceStyleName, SourceFontSize, SourcePrimaryColour);

    private static string StyleLine(string name, int fontSize, string primaryColour) =>
        $"Style: {name},{FontName},{fontSize},{primaryColour},{SecondaryColour},{OutlineColour},{BackColour}," +
        $"0,0,0,0,100,100,0,0,1,{Outline},{Shadow},{Alignment},{MarginL},{MarginR},{MarginV},{Encoding}";
}
