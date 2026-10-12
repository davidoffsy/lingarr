namespace Lingarr.Server.Models;

/// <summary>
/// Progress of an ffmpeg sidecar extract. <see cref="Percent"/> is 0–100 of the extract itself
/// (later mapped onto the translation request's 0–20 range). Null means duration is unknown.
/// </summary>
public readonly record struct FfmpegExtractProgress(int? Percent, bool Ended);
