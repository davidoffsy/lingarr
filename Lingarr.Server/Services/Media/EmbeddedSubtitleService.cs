using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Media;

public class EmbeddedSubtitleService : IEmbeddedSubtitleService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ExtractTimeout = TimeSpan.FromMinutes(2);

    private static readonly HashSet<string> ExtractableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa"
    };

    private static readonly JsonSerializerOptions ProbeJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IExternalProcessRunner _processRunner;
    private readonly LanguageCodeService _languageCodeService;
    private readonly ILogger<EmbeddedSubtitleService> _logger;

    public EmbeddedSubtitleService(
        IExternalProcessRunner processRunner,
        LanguageCodeService languageCodeService,
        ILogger<EmbeddedSubtitleService> logger)
    {
        _processRunner = processRunner;
        _languageCodeService = languageCodeService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmbeddedSubtitleTrack>> ProbeAsync(
        string mkvPath,
        CancellationToken cancellationToken = default)
    {
        if (!IsReadableMkv(mkvPath))
        {
            return [];
        }

        var arguments = new[]
        {
            "-v", "error",
            "-select_streams", "s",
            "-show_entries",
            "stream=index,codec_name:stream_tags=language,title:stream_disposition=default,forced,hearing_impaired",
            "-of", "json",
            mkvPath
        };

        ExternalProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                FfmpegExecutable.Ffprobe,
                arguments,
                ProbeTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe failed while listing subtitle tracks in {Path}.", mkvPath);
            return [];
        }

        if (!result.ExecutableFound)
        {
            _logger.LogWarning(
                "ffprobe was not found; embedded text subtitles in {Path} will be skipped.",
                mkvPath);
            return [];
        }

        if (result.TimedOut)
        {
            _logger.LogWarning("ffprobe timed out while listing subtitle tracks in {Path}.", mkvPath);
            return [];
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ffprobe exited {ExitCode} while listing subtitle tracks in {Path}: {Error}",
                result.ExitCode,
                mkvPath,
                Truncate(result.StandardError));
            return [];
        }

        return ParseTracks(result.StandardOutput, mkvPath);
    }

    /// <inheritdoc />
    public async Task<string?> ExtractAsync(
        string mkvPath,
        EmbeddedSubtitleTrack track,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (!IsReadableMkv(mkvPath))
        {
            return null;
        }

        if (track.Index < 0 || !ExtractableExtensions.Contains(track.Extension))
        {
            _logger.LogWarning(
                "Refusing to extract subtitle stream {Index} ({Codec}) from {Path}.",
                track.Index,
                track.CodecName,
                mkvPath);
            return null;
        }

        string outputPath;
        try
        {
            outputPath = BuildSidecarPath(mkvPath, track);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build sidecar path for stream {Index} of {Path}.", track.Index, mkvPath);
            return null;
        }

        if (HasNonEmptyFile(outputPath))
        {
            _logger.LogDebug("Sidecar {OutputPath} already exists; skipping extract.", outputPath);
            return outputPath;
        }

        var arguments = new[]
        {
            "-y",
            "-i", mkvPath,
            "-map", $"0:{track.Index}",
            "-c:s", "copy",
            outputPath
        };

        ExternalProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                FfmpegExecutable.Ffmpeg,
                arguments,
                ExtractTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryDeleteIncomplete(outputPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteIncomplete(outputPath);
            _logger.LogWarning(ex, "ffmpeg failed while extracting stream {Index} from {Path}.", track.Index, mkvPath);
            return null;
        }

        if (!result.ExecutableFound)
        {
            _logger.LogWarning(
                "ffmpeg was not found; embedded text subtitles in {Path} will be skipped.",
                mkvPath);
            return null;
        }

        if (result.TimedOut)
        {
            TryDeleteIncomplete(outputPath);
            _logger.LogWarning(
                "ffmpeg timed out while extracting stream {Index} from {Path}.",
                track.Index,
                mkvPath);
            return null;
        }

        if (!result.Succeeded)
        {
            TryDeleteIncomplete(outputPath);
            _logger.LogWarning(
                "ffmpeg exited {ExitCode} while extracting stream {Index} from {Path}: {Error}",
                result.ExitCode,
                track.Index,
                mkvPath,
                Truncate(result.StandardError));
            return null;
        }

        if (!HasNonEmptyFile(outputPath))
        {
            TryDeleteIncomplete(outputPath);
            _logger.LogWarning(
                "Extracted sidecar {OutputPath} from {Path} stream {Index} was missing or empty.",
                outputPath,
                mkvPath,
                track.Index);
            return null;
        }

        _logger.LogInformation(
            "Extracted embedded subtitle stream {Index} ({Language}{Caption}) from {Path} to {OutputPath}.",
            track.Index,
            track.Language,
            string.IsNullOrEmpty(track.Caption) ? string.Empty : "." + track.Caption,
            mkvPath,
            outputPath);

        return outputPath;
    }

    private IReadOnlyList<EmbeddedSubtitleTrack> ParseTracks(string json, string mkvPath)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        FfprobeOutput? probe;
        try
        {
            probe = JsonSerializer.Deserialize<FfprobeOutput>(json, ProbeJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse ffprobe JSON for {Path}.", mkvPath);
            return [];
        }

        if (probe?.Streams == null || probe.Streams.Count == 0)
        {
            return [];
        }

        var tracks = new List<EmbeddedSubtitleTrack>();
        foreach (var stream in probe.Streams)
        {
            if (!TryGetSubtitleExtension(stream.CodecName, out var extension))
            {
                _logger.LogDebug(
                    "Skipping subtitle stream {Index} in {Path} with unsupported codec {Codec}.",
                    stream.Index,
                    mkvPath,
                    stream.CodecName);
                continue;
            }

            var isForced = IsFlagSet(stream.Disposition?.Forced);
            var isHearingImpaired = IsFlagSet(stream.Disposition?.HearingImpaired);
            var caption = isForced
                ? "forced"
                : isHearingImpaired
                    ? "sdh"
                    : string.Empty;

            tracks.Add(new EmbeddedSubtitleTrack
            {
                Index = stream.Index,
                CodecName = stream.CodecName ?? string.Empty,
                Language = MapLanguage(GetTag(stream.Tags, "language")),
                Caption = caption,
                Title = GetTag(stream.Tags, "title") ?? string.Empty,
                Extension = extension,
                IsDefault = IsFlagSet(stream.Disposition?.Default),
                IsForced = isForced,
                IsHearingImpaired = isHearingImpaired
            });
        }

        return tracks;
    }

    private string MapLanguage(string? mkvLanguageTag)
    {
        if (string.IsNullOrWhiteSpace(mkvLanguageTag))
        {
            return EmbeddedSubtitleTrack.UnknownLanguage;
        }

        var tag = mkvLanguageTag.Trim();
        if (tag.Equals(EmbeddedSubtitleTrack.UndeterminedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return EmbeddedSubtitleTrack.UndeterminedLanguage;
        }

        // ISO 639-2/B Chinese is not indexed by CultureInfo (which uses 639-2/T "zho").
        if (tag.Equals("chi", StringComparison.OrdinalIgnoreCase))
        {
            tag = "zho";
        }

        if (!_languageCodeService.Validate(tag))
        {
            return EmbeddedSubtitleTrack.UnknownLanguage;
        }

        try
        {
            var normalized = LanguageCodeService.GetNormalizedCode(tag);
            if (tag.Contains('-', StringComparison.Ordinal))
            {
                return normalized;
            }

            var culture = CultureInfo.GetCultureInfo(normalized);
            var twoLetter = culture.TwoLetterISOLanguageName;
            if (!string.IsNullOrEmpty(twoLetter) &&
                twoLetter.Length == 2 &&
                !twoLetter.Equals("iv", StringComparison.OrdinalIgnoreCase))
            {
                return twoLetter.ToLowerInvariant();
            }

            return normalized;
        }
        catch (Exception ex) when (ex is ArgumentException or CultureNotFoundException)
        {
            return EmbeddedSubtitleTrack.UnknownLanguage;
        }
    }

    private static string BuildSidecarPath(string mkvPath, EmbeddedSubtitleTrack track)
    {
        var directory = Path.GetDirectoryName(mkvPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(mkvPath);
        return Path.Combine(directory, track.BuildSidecarFileName(stem));
    }

    private bool IsReadableMkv(string mkvPath)
    {
        if (string.IsNullOrWhiteSpace(mkvPath))
        {
            return false;
        }

        if (!string.Equals(Path.GetExtension(mkvPath), MediaFileLocator.MkvExtension, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping embedded subtitle work because {Path} is not an MKV.", mkvPath);
            return false;
        }

        if (!File.Exists(mkvPath))
        {
            _logger.LogDebug("MKV {Path} does not exist; skipping embedded subtitle work.", mkvPath);
            return false;
        }

        return true;
    }

    private static bool TryGetSubtitleExtension(string? codecName, out string extension)
    {
        extension = codecName?.Trim().ToLowerInvariant() switch
        {
            "subrip" or "srt" => ".srt",
            "ass" => ".ass",
            "ssa" => ".ssa",
            _ => string.Empty
        };

        return extension.Length > 0;
    }

    private static bool IsFlagSet(int? value) => value is > 0;

    private static string? GetTag(Dictionary<string, string>? tags, string key)
    {
        if (tags == null)
        {
            return null;
        }

        foreach (var pair in tags)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool HasNonEmptyFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDeleteIncomplete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a failed extract.
        }
    }

    private static string Truncate(string? value, int maxLength = 500)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class FfprobeOutput
    {
        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; }
    }

    private sealed class FfprobeStream
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; }

        [JsonPropertyName("disposition")]
        public FfprobeDisposition? Disposition { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class FfprobeDisposition
    {
        [JsonPropertyName("default")]
        public int Default { get; set; }

        [JsonPropertyName("forced")]
        public int Forced { get; set; }

        [JsonPropertyName("hearing_impaired")]
        public int HearingImpaired { get; set; }
    }
}
