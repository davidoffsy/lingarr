using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle.Bilingual;

public class BilingualSubtitleExportService : IBilingualSubtitleExportService
{
    private readonly ISettingService _settings;
    private readonly ILogger<BilingualSubtitleExportService> _logger;
    private readonly BilingualAssWriter _writer = new();

    public BilingualSubtitleExportService(
        ISettingService settings,
        ILogger<BilingualSubtitleExportService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        IReadOnlyList<SubtitleItem> items,
        string monolingualPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await IsEnabledAsync())
            {
                return;
            }

            await WriteSidecarAsync(items, monolingualPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Bilingual ASS export failed for {Path}; monolingual subtitle was left unchanged",
                monolingualPath);
        }
    }

    /// <inheritdoc />
    public async Task TryRegenerateAsync(
        IReadOnlyList<SubtitleItem> sourceItems,
        IReadOnlyList<SubtitleItem> targetItems,
        string monolingualPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await IsEnabledAsync())
            {
                return;
            }

            if (!TryAlign(sourceItems, targetItems, monolingualPath, out var aligned))
            {
                return;
            }

            await WriteSidecarAsync(aligned, monolingualPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Bilingual ASS regenerate failed for {Path}; monolingual subtitle was left unchanged",
                monolingualPath);
        }
    }

    private async Task<bool> IsEnabledAsync()
    {
        var value = await _settings.GetSetting(SettingKeys.Translation.ExportBilingualAss);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryAlign(
        IReadOnlyList<SubtitleItem> sourceItems,
        IReadOnlyList<SubtitleItem> targetItems,
        string monolingualPath,
        out List<SubtitleItem> aligned)
    {
        aligned = [];
        var sourceByPosition = sourceItems
            .GroupBy(item => item.Position)
            .ToDictionary(group => group.Key, group => group.First());

        var matched = 0;
        foreach (var target in targetItems)
        {
            var item = Copy(target);
            if (item.TranslatedLines.Count == 0 || item.TranslatedLines.All(string.IsNullOrEmpty))
            {
                item.TranslatedLines = [..target.Lines];
            }

            if (sourceByPosition.TryGetValue(target.Position, out var source))
            {
                item.PlaintextLines = [..source.PlaintextLines];
                item.Lines = [..source.Lines];
                item.StartTime = source.StartTime;
                item.EndTime = source.EndTime;
                matched++;
            }

            aligned.Add(item);
        }

        if (sourceItems.Count > 0 && targetItems.Count > 0 && matched == 0)
        {
            _logger.LogWarning(
                "Bilingual ASS regenerate skipped for {Path}: no Position overlap between source and target",
                monolingualPath);
            aligned = [];
            return false;
        }

        return true;
    }

    private async Task WriteSidecarAsync(
        IReadOnlyList<SubtitleItem> items,
        string monolingualPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(monolingualPath))
        {
            return;
        }

        var sidecarPath = BilingualAssPath.FromMonolingual(monolingualPath);
        if (string.Equals(sidecarPath, monolingualPath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Bilingual ASS export skipped: sidecar path equals monolingual path {Path}",
                monolingualPath);
            return;
        }

        await WriteAtomicallyAsync(sidecarPath, items, cancellationToken);
        _logger.LogInformation("Wrote bilingual ASS sidecar: |Green|{Path}|/Green|", sidecarPath);
    }

    private async Task WriteAtomicallyAsync(
        string sidecarPath,
        IReadOnlyList<SubtitleItem> items,
        CancellationToken cancellationToken)
    {
        var tempPath = BilingualAssPath.TempPath(sidecarPath);
        try
        {
            var directory = Path.GetDirectoryName(sidecarPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using (var stream = new FileStream(
                             tempPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await _writer.WriteAsync(stream, items, cancellationToken);
            }

            try
            {
                File.Move(tempPath, sidecarPath, overwrite: true);
            }
            catch (IOException)
            {
                // Cloud mounts (rclone) often cannot rename atomically.
                File.Copy(tempPath, sidecarPath, overwrite: true);
                TryDelete(tempPath);
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static SubtitleItem Copy(SubtitleItem item) => new()
    {
        Position = item.Position,
        StartTime = item.StartTime,
        EndTime = item.EndTime,
        Lines = [..item.Lines],
        PlaintextLines = [..item.PlaintextLines],
        TranslatedLines = [..item.TranslatedLines]
    };

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
            // Best-effort cleanup of the temp file.
        }
    }
}
