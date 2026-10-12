namespace Lingarr.Server.Services;

/// <summary>
/// Maps extract / translate percentages onto a single 0–100 request progress bar.
/// Extract occupies 0–20; translate occupies 20–100.
/// </summary>
public static class TranslationProgressMapping
{
    public const int ExtractStart = 1;
    public const int ExtractComplete = 20;
    public const int TranslateSpan = 80;

    public const string ExtractPhase = "extract";
    public const string TranslatePhase = "translate";

    public static int FromExtractPercent(int extractPercent)
    {
        extractPercent = Math.Clamp(extractPercent, 0, 100);
        if (extractPercent <= 0)
        {
            return ExtractStart;
        }

        return Math.Clamp(
            (int)Math.Round(extractPercent * ExtractComplete / 100.0),
            ExtractStart,
            ExtractComplete);
    }

    public static int FromTranslatePercent(int translatePercent)
    {
        translatePercent = Math.Clamp(translatePercent, 0, 100);
        return ExtractComplete + (int)Math.Round(translatePercent * TranslateSpan / 100.0);
    }
}
