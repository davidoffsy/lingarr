namespace Lingarr.Server.Services.Subtitle.Bilingual;

/// <summary>
/// Sidecar path rules for bilingual ASS export. The marker is a dedicated filename
/// segment and must not be fed through <c>CreateFilePath</c> as a subtitle tag.
/// </summary>
public static class BilingualAssPath
{
    public const string Marker = "bilingual";
    public const string Extension = ".ass";
    public const string TempSuffix = ".tmp";

    /// <summary>
    /// Derives the sidecar path from the monolingual output:
    /// <c>{dir}/{nameWithoutExt}.bilingual.ass</c>.
    /// </summary>
    public static string FromMonolingual(string monolingualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(monolingualPath);

        var directory = Path.GetDirectoryName(monolingualPath);
        var stem = Path.GetFileNameWithoutExtension(monolingualPath);
        var fileName = $"{stem}.{Marker}{Extension}";
        return string.IsNullOrEmpty(directory)
            ? fileName
            : Path.Combine(directory, fileName);
    }

    public static string TempPath(string sidecarPath) => sidecarPath + TempSuffix;

    /// <summary>
    /// True when any dotted filename part equals <see cref="Marker"/> (case-insensitive).
    /// Used to keep bilingual sidecars out of subtitle discovery.
    /// </summary>
    public static bool IsBilingualSidecar(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.Split('.')
            .Any(part => part.Equals(Marker, StringComparison.OrdinalIgnoreCase));
    }
}
