using Lingarr.Core.Entities;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Translation.Attribution;

namespace Lingarr.Server.Interfaces.Services;

public interface IStatisticsService
{
    Task<Statistics> GetStatistics();
    Task<IEnumerable<DailyStatistics>> GetDailyStatistics(int days = 30);

    /// <summary>
    /// Records one finished subtitle translation.
    /// <para>
    /// File level counters keep their historical meaning (one file per finished request) and are
    /// attributed to the source that translated most of its lines, so the totals stay comparable with
    /// earlier versions. A request that produced no new line is not counted as a file, which keeps
    /// "files translated" equal to the sum of the per service counters. The per line source statistics
    /// are accumulated for every provider and model that produced lines.
    /// </para>
    /// </summary>
    /// <param name="request">The finished translation request.</param>
    /// <param name="sources">Providers and models that actually produced the translated lines.</param>
    /// <param name="translatedSubtitles">Subtitles translated by this run.</param>
    Task<int> UpdateTranslationStatisticsFromSubtitles(
        TranslationRequest request,
        TranslationSourceSummary sources,
        List<SubtitleItem> translatedSubtitles);

    /// <summary>
    /// Records one finished content translation (single lines translated through the content API).
    /// See <see cref="UpdateTranslationStatisticsFromSubtitles"/> for how <paramref name="sources"/>
    /// is used.
    /// </summary>
    Task<int> UpdateTranslationStatisticsFromLines(
        TranslationRequest request,
        TranslationSourceSummary sources,
        BatchTranslatedLine[] translatedLines);
    Task ResetStatistics();
}