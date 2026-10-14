using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

public class PlexSubtitleImporter : IPlexSubtitleImporter
{
    public const string DownloadingSuffix = ".plex-download";

    private readonly ISettingService _settingService;
    private readonly IPlexApiClient _plexApiClient;
    private readonly LanguageCodeService _languageCodeService;
    private readonly ILogger<PlexSubtitleImporter> _logger;

    public PlexSubtitleImporter(
        ISettingService settingService,
        IPlexApiClient plexApiClient,
        LanguageCodeService languageCodeService,
        ILogger<PlexSubtitleImporter> logger)
    {
        _settingService = settingService;
        _plexApiClient = plexApiClient;
        _languageCodeService = languageCodeService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlexSourceSubtitleCandidate>> ListAsync(
        string? ratingKey,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ratingKey))
        {
            return [];
        }

        var plexUrl = await _settingService.GetSetting(SettingKeys.Integration.PlexUrl);
        var plexToken = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexToken);
        if (string.IsNullOrWhiteSpace(plexUrl) || string.IsNullOrWhiteSpace(plexToken))
        {
            return [];
        }

        var metadata = await _plexApiClient.GetMetadata(plexUrl, plexToken, ratingKey, cancellationToken);
        if (metadata == null || metadata.SubtitleStreams.Count == 0)
        {
            return [];
        }

        var candidates = new List<PlexSourceSubtitleCandidate>();
        foreach (var stream in metadata.SubtitleStreams)
        {
            if (!PlexSubtitleFormat.TryGetExtension(stream.Codec, stream.Format, out var extension) ||
                !PlexStreamKey.IsSafe(stream.Key))
            {
                continue;
            }

            var language = PlexSubtitleLanguage.Normalize(
                _languageCodeService,
                stream.LanguageTag,
                stream.LanguageCode);
            if (string.Equals(language, EmbeddedSubtitleTrack.UnknownLanguage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(language, EmbeddedSubtitleTrack.UndeterminedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var caption = stream.Forced
                ? "forced"
                : stream.HearingImpaired
                    ? "sdh"
                    : string.Empty;

            candidates.Add(new PlexSourceSubtitleCandidate
            {
                Path = string.Empty,
                Language = language,
                Caption = caption,
                Format = extension,
                IsEmbedded = false,
                SidecarOrder = candidates.Count,
                IsForced = stream.Forced,
                IsHearingImpaired = stream.HearingImpaired,
                IsPlexStream = true,
                StreamKey = stream.Key
            });
        }

        _logger.LogInformation(
            "Plex metadata for ratingKey {RatingKey} has {StreamCount} text subtitle stream(s) Lingarr can import",
            ratingKey,
            candidates.Count);

        return candidates;
    }

    public async Task<string?> MaterializeAsync(
        PlexSourceSubtitleCandidate candidate,
        string mediaDirectory,
        string mediaFileName,
        CancellationToken cancellationToken = default)
    {
        if (!candidate.IsPlexStream ||
            string.IsNullOrWhiteSpace(mediaDirectory) ||
            string.IsNullOrWhiteSpace(mediaFileName) ||
            !PlexStreamKey.IsSafe(candidate.StreamKey))
        {
            return null;
        }

        if (!Directory.Exists(mediaDirectory))
        {
            _logger.LogInformation(
                "Plex subtitle import skipped: media directory {Directory} does not exist",
                mediaDirectory);
            return null;
        }

        var outputPath = CanonicalSidecarPath(
            mediaDirectory,
            mediaFileName,
            candidate.Language,
            candidate.Caption,
            candidate.Format);
        if (HasNonEmptyFile(outputPath))
        {
            _logger.LogDebug("Sidecar {OutputPath} already exists; skipping Plex download.", outputPath);
            return outputPath;
        }

        var plexUrl = await _settingService.GetSetting(SettingKeys.Integration.PlexUrl);
        var plexToken = await _settingService.GetEncryptedSetting(SettingKeys.Integration.PlexToken);
        if (string.IsNullOrWhiteSpace(plexUrl) || string.IsNullOrWhiteSpace(plexToken))
        {
            return null;
        }

        byte[]? bytes;
        try
        {
            bytes = await _plexApiClient.DownloadStream(
                plexUrl,
                plexToken,
                candidate.StreamKey!,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to download Plex subtitle stream {StreamKey}",
                candidate.StreamKey);
            throw;
        }

        if (bytes == null || bytes.Length == 0)
        {
            _logger.LogInformation(
                "Plex subtitle stream {StreamKey} returned no content",
                candidate.StreamKey);
            return null;
        }

        var tempPath = outputPath + DownloadingSuffix;
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            try
            {
                File.Move(tempPath, outputPath, overwrite: false);
            }
            catch (IOException) when (!HasNonEmptyFile(outputPath))
            {
                // Cloud mounts (rclone / 115) often cannot rename atomically.
                File.Copy(tempPath, outputPath, overwrite: false);
                TryDelete(tempPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(tempPath);
            if (HasNonEmptyFile(outputPath))
            {
                return outputPath;
            }

            _logger.LogWarning(
                ex,
                "Failed to write Plex subtitle sidecar {OutputPath}",
                outputPath);
            return null;
        }

        _logger.LogInformation(
            "Imported Plex subtitle {StreamKey} ({Language}) to {OutputPath}",
            candidate.StreamKey,
            candidate.Language,
            outputPath);
        return outputPath;
    }

    internal static string CanonicalSidecarPath(
        string directory,
        string fileName,
        string language,
        string caption,
        string extension)
    {
        var name = $"{fileName}.{language}";
        if (!string.IsNullOrEmpty(caption))
        {
            name += $".{caption}";
        }

        return Path.Combine(directory, name + extension);
    }

    private static bool HasNonEmptyFile(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDelete(string path)
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
