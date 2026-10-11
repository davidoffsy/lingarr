namespace Lingarr.Server.Models.FileSystem;

public class Subtitles
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// True when this entry is still inside the MKV and has not been extracted to a sidecar.
    /// </summary>
    public bool IsEmbedded { get; set; }

    /// <summary>
    /// Container stream index used with <c>ffmpeg -map 0:{index}</c>.
    /// </summary>
    public int? TrackIndex { get; set; }

    /// <summary>
    /// ffprobe <c>codec_name</c> for embedded tracks; used in the media hash.
    /// </summary>
    public string CodecName { get; set; } = string.Empty;
}