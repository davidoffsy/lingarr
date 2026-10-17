using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Subtitle.Bilingual;

/// <summary>
/// Best-effort bilingual ASS sidecar writer. Failures are logged and never fail
/// the monolingual translation, proofread, or line-edit path.
/// </summary>
public interface IBilingualSubtitleExportService
{
    /// <summary>
    /// Writes <c>{monolingualStem}.bilingual.ass</c> next to the monolingual file
    /// when the setting is on. No-ops when the setting is missing, invalid, or off.
    /// </summary>
    Task ExportAsync(
        IReadOnlyList<SubtitleItem> items,
        string monolingualPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the sidecar from source timeline + updated monolingual target lines,
    /// aligned by <see cref="SubtitleItem.Position"/>. Logs and skips when nothing
    /// can be paired; never rewrites the monolingual file.
    /// </summary>
    Task TryRegenerateAsync(
        IReadOnlyList<SubtitleItem> sourceItems,
        IReadOnlyList<SubtitleItem> targetItems,
        string monolingualPath,
        CancellationToken cancellationToken = default);
}
