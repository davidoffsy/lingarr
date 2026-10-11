namespace Lingarr.Server.Models.FileSystem;

/// <summary>
/// An extractable text subtitle stream inside an MKV container.
/// </summary>
public class EmbeddedSubtitleTrack
{
    public const string UnknownLanguage = "unknown";
    public const string UndeterminedLanguage = "und";

    public int Index { get; init; }
    public string CodecName { get; init; } = string.Empty;

    /// <summary>
    /// Normalized language used in sidecar file names (<c>en</c>, <c>zh</c>, <c>und</c>, <c>unknown</c>).
    /// </summary>
    public string Language { get; init; } = UnknownLanguage;

    /// <summary>
    /// Caption flag aligned with existing sidecar names: <c>forced</c>, <c>sdh</c>, or empty.
    /// </summary>
    public string Caption { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Sidecar extension including the leading dot (<c>.srt</c>, <c>.ass</c>, <c>.ssa</c>).
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public bool IsHearingImpaired { get; init; }

    /// <summary>
    /// Automatic jobs should not extract or translate undetermined / unmapped languages.
    /// </summary>
    public bool CanAutoTranslate =>
        !string.Equals(Language, UnknownLanguage, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Language, UndeterminedLanguage, StringComparison.OrdinalIgnoreCase);

    public string BuildSidecarFileName(string videoStem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoStem);

        var name = $"{videoStem}.{Language}";
        if (!string.IsNullOrEmpty(Caption))
        {
            name += $".{Caption}";
        }

        return name + Extension;
    }
}
