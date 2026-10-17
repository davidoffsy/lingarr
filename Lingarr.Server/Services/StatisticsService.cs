using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Batch.Response;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Translation.Attribution;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class StatisticsService : IStatisticsService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ISettingService _settingService;

    private static DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneConfiguration.Current).DateTime);

    public StatisticsService(
        LingarrDbContext dbContext,
        ISettingService settingService)
    {
        _dbContext = dbContext;
        _settingService = settingService;
    }

    public async Task<Statistics> GetStatistics()
    {
        return await GetOrCreateStatistics();
    }

    public async Task<IEnumerable<DailyStatistics>> GetDailyStatistics(int days = 30)
    {
        var startDate = Today.AddDays(-days + 1); // +1 to include today
        var stats = await _dbContext.DailyStatistics
            .Where(d => d.Date >= startDate)
            .OrderBy(d => d.Date)
            .ToListAsync();

        return stats;
    }

    private async Task<Statistics> GetOrCreateStatistics()
    {
        var stats = await _dbContext.Statistics.SingleOrDefaultAsync();
        if (stats == null)
        {
            stats = new Statistics();
            _dbContext.Statistics.Add(stats);
            await _dbContext.SaveChangesAsync();
        }

        return stats;
    }

    private async Task<DailyStatistics> GetOrCreateDailyStatistics(DateOnly today)
    {
        var dailyStats = await _dbContext.DailyStatistics
            .Where(d => d.Date >= today)
            .FirstOrDefaultAsync();

        if (dailyStats == null)
        {
            dailyStats = new DailyStatistics { Date = today };
            _dbContext.DailyStatistics.Add(dailyStats);
        }

        return dailyStats;
    }

    public async Task<int> UpdateTranslationStatisticsFromSubtitles(
        TranslationRequest request,
        TranslationSourceSummary sources,
        List<SubtitleItem> translatedSubtitles)
    {
        var charCount = translatedSubtitles.Sum(s => s.Lines.Sum(l => l.Length));

        return await UpdateTranslationStatisticsInternal(request, sources, charCount);
    }

    public async Task<int> UpdateTranslationStatisticsFromLines(
        TranslationRequest request,
        TranslationSourceSummary sources,
        BatchTranslatedLine[] translatedLines)
    {
        var charCount = translatedLines.Sum(s => s.Line.Length);

        return await UpdateTranslationStatisticsInternal(request, sources, charCount);
    }

    private async Task<int> UpdateTranslationStatisticsInternal(
        TranslationRequest request,
        TranslationSourceSummary sources,
        int totalCharacters)
    {
        var stats = await GetOrCreateStatistics();
        var today = Today;

        // Invariant: TotalFilesTranslated, TranslationsByMediaType, SubtitlesByLanguage and the daily
        // counter all count finished files, and TranslationsByService counts those same files grouped by
        // the source that did most of the work. A run that produced no new translated line -- every
        // position was already translated by an earlier run, or the remaining lines failed -- must
        // therefore not touch any of them, otherwise the "by service" breakdown the front end shows
        // stops adding up to the file total.
        // Line totals use sources.TotalLines (one per attributed position, matching LinesBySource),
        // not Sum(cue.Lines.Count): a multi-line cue would otherwise make TotalLinesTranslated diverge
        // from Sum(LinesBySource). Characters still come from the caller's subtitle/line texts.
        if (!sources.IsEmpty)
        {
            // Update total counts
            stats.TotalLinesTranslated += sources.TotalLines;
            stats.TotalCharactersTranslated += totalCharacters;
            stats.TotalFilesTranslated++;

            // Update media type statistics
            var mediaType = request.MediaType.ToString();
            var mediaTypeStats = stats.TranslationsByMediaType;
            mediaTypeStats[mediaType] = mediaTypeStats.GetValueOrDefault(mediaType) + 1;
            stats.TranslationsByMediaType = mediaTypeStats;

            // File level attribution: the finished file is counted once, under the source that translated
            // most of its lines. Line level detail lives in LinesBySource below.
            var dominant = sources.Dominant!;
            var serviceStats = stats.TranslationsByService;
            serviceStats[dominant.Source.Provider] = serviceStats.GetValueOrDefault(dominant.Source.Provider) + 1;
            stats.TranslationsByService = serviceStats;

            if (!string.IsNullOrWhiteSpace(dominant.Source.Model))
            {
                var modelStats = stats.TranslationsByModel;
                modelStats[dominant.Source.DisplayName] = modelStats.GetValueOrDefault(dominant.Source.DisplayName) + 1;
                stats.TranslationsByModel = modelStats;
            }

            // Update language pair statistics 
            var languageStats = stats.SubtitlesByLanguage;
            var languagePair = $"{request.SourceLanguage}:{request.TargetLanguage}";
            languageStats[languagePair] = languageStats.GetValueOrDefault(languagePair) + 1;
            stats.SubtitlesByLanguage = languageStats;

            // Line level attribution: every provider and model that produced lines is counted, so a chain
            // that fell back from one model to another reports both. Sum(LinesBySource) equals
            // TotalLinesTranslated for everything counted here, which is what the front end relies on.
            var sourceStats = stats.LinesBySource;
            foreach (var count in sources.Counts)
            {
                var key = count.Source.DisplayName;
                sourceStats[key] = sourceStats.GetValueOrDefault(key) + count.Lines;
            }
            stats.LinesBySource = sourceStats;

            // Update daily statistics
            var dailyStats = await GetOrCreateDailyStatistics(today);
            dailyStats.TranslationCount++;
        }

        return await _dbContext.SaveChangesAsync();
    }

    public async Task ResetStatistics()
    {
        // Reset the main statistics entity
        var stats = await GetOrCreateStatistics();
        stats.TotalLinesTranslated = 0;
        stats.TotalCharactersTranslated = 0;
        stats.TotalFilesTranslated = 0;
        stats.TotalMovies = 0;
        stats.TotalEpisodes = 0;
        stats.TotalSubtitles = 0;
        stats.TranslationsByMediaType = new Dictionary<string, int>();
        stats.TranslationsByService = new Dictionary<string, int>();
        stats.SubtitlesByLanguage = new Dictionary<string, int>();
        stats.TranslationsByModel = new Dictionary<string, int>();
        stats.LinesBySource = new Dictionary<string, int>();

        // Delete all daily statistics
        var dailyStats = await _dbContext.DailyStatistics.ToListAsync();
        _dbContext.DailyStatistics.RemoveRange(dailyStats);

        await _dbContext.SaveChangesAsync();
    }
}