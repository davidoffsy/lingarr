using Hangfire;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Media;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Services.Translation.Attribution;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Extensions;
using SubtitleValidationOptions = Lingarr.Server.Models.SubtitleValidationOptions;

namespace Lingarr.Server.Jobs;

public class TranslationJob
{
    private readonly ILogger<TranslationJob> _logger;
    private readonly ISettingService _settings;
    private readonly LingarrDbContext _dbContext;
    private readonly IProgressService _progressService;
    private readonly ISubtitleService _subtitleService;
    private readonly IScheduleService _scheduleService;
    private readonly IStatisticsService _statisticsService;
    private readonly ITranslationServiceFactory _translationServiceFactory;
    private readonly ITranslationRequestService _translationRequestService;
    private readonly ITranslationRequestEventService _eventService;
    private readonly IEmbeddedSubtitleService _embeddedSubtitleService;

    public TranslationJob(
        ILogger<TranslationJob> logger,
        ISettingService settings,
        LingarrDbContext dbContext,
        IProgressService progressService,
        ISubtitleService subtitleService,
        IScheduleService scheduleService,
        IStatisticsService statisticsService,
        ITranslationServiceFactory translationServiceFactory,
        ITranslationRequestService translationRequestService,
        ITranslationRequestEventService eventService,
        IEmbeddedSubtitleService embeddedSubtitleService)
    {
        _logger = logger;
        _settings = settings;
        _dbContext = dbContext;
        _progressService = progressService;
        _subtitleService = subtitleService;
        _scheduleService = scheduleService;
        _statisticsService = statisticsService;
        _translationServiceFactory = translationServiceFactory;
        _translationRequestService = translationRequestService;
        _eventService = eventService;
        _embeddedSubtitleService = embeddedSubtitleService;
    }

    [AutomaticRetry(Attempts = 0)]
    [Queue("translation")]
    public async Task Execute(
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        var jobName = JobContextFilter.GetCurrentJobTypeName();
        var jobId = JobContextFilter.GetCurrentJobId();

        try
        {
            await _scheduleService.UpdateJobState(jobName, JobStatus.Processing.GetDisplayName());
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsNoLongerRunnableAsync(translationRequest.Id, cancellationToken))
            {
                return;
            }

            var request = await _translationRequestService.UpdateTranslationRequest(translationRequest,
                TranslationStatus.InProgress,
                jobId);
            await _eventService.LogEvent(request.Id, TranslationStatus.InProgress);
            await _translationRequestService.UpdateActiveCount();

            var isEmbeddedExtract = EmbeddedSubtitlePlaceholder.TryParse(request.SubtitleToTranslate, out _, out _);
            if (isEmbeddedExtract)
            {
                await _progressService.Emit(
                    request,
                    TranslationProgressMapping.ExtractStart,
                    TranslationProgressMapping.ExtractPhase);
                await WarnIfHangfireTimeoutTooLowAsync();
            }

            await EnsureEmbeddedSidecarAsync(request, cancellationToken, isEmbeddedExtract);

            if (await ReloadAndSkipIfNotRunnableAsync(request, cancellationToken))
            {
                return;
            }

            if (isEmbeddedExtract)
            {
                await _progressService.Emit(
                    request,
                    TranslationProgressMapping.ExtractComplete,
                    TranslationProgressMapping.ExtractPhase);
            }

            _logger.LogInformation("TranslateJob started for subtitle: |Green|{filePath}|/Green|",
                request.SubtitleToTranslate);
            var settings = await _settings.GetSettings([
                SettingKeys.Translation.ServiceType,
                SettingKeys.Translation.FixOverlappingSubtitles,
                SettingKeys.Translation.StripSubtitleFormatting,
                SettingKeys.Translation.PreserveLineBreaks,
                SettingKeys.Translation.AddTranslatorInfo,

                SettingKeys.SubtitleValidation.ValidateSubtitles,
                SettingKeys.SubtitleValidation.MaxFileSizeBytes,
                SettingKeys.SubtitleValidation.MaxSubtitleLength,
                SettingKeys.SubtitleValidation.MinSubtitleLength,
                SettingKeys.SubtitleValidation.MinDurationMs,
                SettingKeys.SubtitleValidation.MaxDurationSecs,

                SettingKeys.Translation.AiContextBefore,
                SettingKeys.Translation.AiContextAfter,
                SettingKeys.Translation.AiContextUseTranslated,
                SettingKeys.Translation.UseBatchTranslation,
                SettingKeys.Translation.MaxBatchSize,
                SettingKeys.Translation.RemoveLanguageTag,
                SettingKeys.Translation.UseSubtitleTagging,
                SettingKeys.Translation.SubtitleTag
            ]);
            var chainEntries = TranslationChainResolver
                .Resolve(settings[SettingKeys.Translation.ServiceType], _logger)
                .Entries;
            var stripSubtitleFormatting = settings[SettingKeys.Translation.StripSubtitleFormatting] == "true";
            var preserveLineBreaks = settings[SettingKeys.Translation.PreserveLineBreaks] == "true";
            var addTranslatorInfo = settings[SettingKeys.Translation.AddTranslatorInfo] == "true";
            var validateSubtitles = settings[SettingKeys.SubtitleValidation.ValidateSubtitles] != "false";
            var removeLanguageTag = settings[SettingKeys.Translation.RemoveLanguageTag] != "false";

            var contextBefore = int.TryParse(settings[SettingKeys.Translation.AiContextBefore],
                out var linesBefore)
                ? linesBefore
                : 0;
            var contextAfter = int.TryParse(settings[SettingKeys.Translation.AiContextAfter],
                out var linesAfter)
                ? linesAfter
                : 0;
            var useTranslatedContext = settings[SettingKeys.Translation.AiContextUseTranslated] == "true";

            // validate subtitles
            if (validateSubtitles)
            {
                var validationOptions = new SubtitleValidationOptions
                {
                    // File size setting - default to 2MB if parsing fails
                    MaxFileSizeBytes = long.TryParse(settings[SettingKeys.SubtitleValidation.MaxFileSizeBytes],
                        out var maxFileSizeBytes)
                        ? maxFileSizeBytes
                        : 2 * 1024 * 1024,

                    // Maximum characters per subtitle - default to 500 if parsing fails
                    MaxSubtitleLength = int.TryParse(settings[SettingKeys.SubtitleValidation.MaxSubtitleLength],
                        out var maxSubtitleLength)
                        ? maxSubtitleLength
                        : 500,

                    // Minimum characters per subtitle - default to 1 if parsing fails
                    MinSubtitleLength = int.TryParse(settings[SettingKeys.SubtitleValidation.MinSubtitleLength],
                        out var minSubtitleLength)
                        ? minSubtitleLength
                        : 2,

                    // Minimum duration in milliseconds - default to 500ms if parsing fails
                    MinDurationMs = double.TryParse(settings[SettingKeys.SubtitleValidation.MinDurationMs],
                        out var minDurationMs)
                        ? minDurationMs
                        : 500,

                    // Maximum duration in seconds - default to 10s if parsing fails
                    MaxDurationSecs = double.TryParse(settings[SettingKeys.SubtitleValidation.MaxDurationSecs],
                        out var maxDurationSecs)
                        ? maxDurationSecs
                        : 10,

                    // Used to determine content length when
                    StripSubtitleFormatting = stripSubtitleFormatting
                };

                if (!_subtitleService.ValidateSubtitle(request.SubtitleToTranslate, validationOptions))
                {
                    _logger.LogWarning("Subtitle is not valid according to configured preferences.");
                    throw new TaskCanceledException("Subtitle is not valid according to configured preferences.");
                }
            }

            // translate subtitles
            var services = _translationServiceFactory.CreateTranslationServices(chainEntries);
            if (services.Count == 0)
            {
                throw new TranslationException(
                    $"No usable translation services configured: [{TranslationChainEntry.DescribeAll(chainEntries)}]");
            }
            IProgressService translateProgress = _progressService;
            if (isEmbeddedExtract)
            {
                translateProgress = new MappedProgressService(
                    _progressService,
                    TranslationProgressMapping.ExtractComplete,
                    TranslationProgressMapping.TranslateSpan,
                    TranslationProgressMapping.TranslatePhase);
            }

            var translator = new SubtitleTranslationService(services, _logger, translateProgress, useTranslatedContext);
            var subtitles = await _subtitleService.ReadSubtitles(request.SubtitleToTranslate);

            // subtitle already carries a translation from an earlier prior run.
            // Group by Position and keep the most recent row in case the same position was used more than once.
            var persistedLines = (await _dbContext.TranslationRequestLines
                    .Where(line => line.TranslationRequestId == request.Id)
                    .Select(line => new
                    {
                        line.Id,
                        line.Position,
                        line.Target,
                        line.Service,
                        line.Model,
                        line.MatchedSource,
                        line.MatchedTarget,
                        line.Tier
                    })
                    .ToListAsync(cancellationToken))
                .GroupBy(line => line.Position)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.Id).First());

            if (persistedLines.Count > 0)
            {
                _logger.LogInformation(
                    "Resuming translation for request {RequestId}: {Resumed} of {Total} lines already translated.",
                    request.Id, persistedLines.Count, subtitles.Count);

                foreach (var subtitle in subtitles)
                {
                    if (persistedLines.TryGetValue(subtitle.Position, out var line))
                    {
                        subtitle.TranslatedLines = [line.Target];
                    }
                }

                // Attribution of the lines an earlier run translated, so the translator tag and the
                // source statistics cover the whole file instead of only this run's lines. Pair is
                // restored when it was stored (non-exact matches); Exact pairs are persisted as null
                // and that is enough for resume to still Record stacked cache hits.
                translator.SeedAttribution(persistedLines
                    .Where(line => !string.IsNullOrEmpty(line.Value.Service))
                    .ToDictionary(
                        line => line.Key,
                        line => new TranslationAttribution(
                            new TranslationSource(line.Value.Service!, line.Value.Model),
                            StoredPair(
                                line.Value.MatchedSource,
                                line.Value.MatchedTarget,
                                line.Value.Tier))));
            }

            List<SubtitleItem> translatedSubtitles;
            if (settings[SettingKeys.Translation.UseBatchTranslation] == "true"
                && services.Any(entry => entry.Service is IBatchTranslationService))
            {
                var maxSize = int.TryParse(settings[SettingKeys.Translation.MaxBatchSize],
                    out var batchSize)
                    ? batchSize
                    : 10000;

                _logger.LogInformation(
                    "Using batch translation with max batch size: {maxBatchSize} for subtitle: {filePath}",
                    maxSize, translationRequest.SubtitleToTranslate);

                translatedSubtitles = await translator.TranslateSubtitlesBatch(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    maxSize,
                    cancellationToken);
            }
            else
            {
                if (contextBefore > 0 || contextAfter > 0)
                {
                    _logger.LogInformation(
                        "Using individual translation with context (before: {contextBefore}, after: {contextAfter}, translated context: {useTranslatedContext}) for subtitle: {filePath}",
                        contextBefore, contextAfter, useTranslatedContext, translationRequest.SubtitleToTranslate);
                }

                translatedSubtitles = await translator.TranslateSubtitles(
                    subtitles,
                    request,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    contextBefore,
                    contextAfter,
                    cancellationToken
                );
            }

            if (settings[SettingKeys.Translation.FixOverlappingSubtitles] == "true")
            {
                translatedSubtitles = _subtitleService.FixOverlappingSubtitles(translatedSubtitles);
            }

            // statistics tracking, only count subtitles translated by this run
            // Computed before AddTranslatorInfo: the intro line is written to the subtitle file but was
            // never attributed, so counting it would add a line the sources do not cover and break the
            // invariant that the line totals match the attributed per-source line counts.
            var newlyTranslatedSubtitles = persistedLines.Count == 0
                ? translatedSubtitles
                : translatedSubtitles.Where(s => !persistedLines.ContainsKey(s.Position)).ToList();
            await _statisticsService.UpdateTranslationStatisticsFromSubtitles(
                request,
                translator.SourceSummaryOf(newlyTranslatedSubtitles.Select(subtitle => subtitle.Position)),
                newlyTranslatedSubtitles);

            if (addTranslatorInfo)
            {
                _subtitleService.AddTranslatorInfo(translator.SourceSummary, translatedSubtitles);
            }

            if (stripSubtitleFormatting)
            {
                var format = translatedSubtitles[0].SsaFormat;
                if (format != null)
                {
                    format.Styles = [];
                }
            }

            var subtitleTag = "";
            if (settings[SettingKeys.Translation.UseSubtitleTagging] == "true")
            {
                subtitleTag = settings[SettingKeys.Translation.SubtitleTag];
            }

            await WriteSubtitles(request, translatedSubtitles, stripSubtitleFormatting, subtitleTag, removeLanguageTag);
            await HandleCompletion(jobName, request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await HandleCancellation(jobName, translationRequest);
        }
        catch (Exception ex)
        {
            await _translationRequestService.ClearMediaHash(translationRequest);
            translationRequest = await _translationRequestService.UpdateTranslationRequest(translationRequest, TranslationStatus.Failed,
                jobId);
            
            translationRequest.ErrorMessage = ex.Message;
            translationRequest.StackTrace = ex.ToString();
            await _dbContext.SaveChangesAsync(cancellationToken);
            
            await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Failed, ex.Message);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Failed.GetDisplayName());
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            throw;
        }
    }

    private async Task EnsureEmbeddedSidecarAsync(
        TranslationRequest request,
        CancellationToken cancellationToken,
        bool reportProgress)
    {
        IProgress<FfmpegExtractProgress>? progress = null;
        if (reportProgress)
        {
            var lastMapped = 0;
            progress = new SynchronousProgress<FfmpegExtractProgress>(update =>
            {
                HangfireJobHeartbeat.Touch();
                var mapped = update.Percent is int percent
                    ? TranslationProgressMapping.FromExtractPercent(percent)
                    : TranslationProgressMapping.ExtractStart;
                if (mapped == lastMapped)
                {
                    return;
                }

                lastMapped = mapped;
                _ = _progressService.Emit(request, mapped, TranslationProgressMapping.ExtractPhase);
            });
        }

        var sidecarPath = await EmbeddedSidecarPreparer.EnsureAsync(
            _embeddedSubtitleService,
            request.SubtitleToTranslate,
            cancellationToken,
            progress);

        if (string.Equals(sidecarPath, request.SubtitleToTranslate, StringComparison.Ordinal))
        {
            return;
        }

        request.SubtitleToTranslate = sidecarPath;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task WarnIfHangfireTimeoutTooLowAsync()
    {
        var extractMinutes = (int)(await GetExtractTimeoutAsync()).TotalMinutes;
        var hangfireMinutes = HangfireJobTimeout.GetStorageMinutes();
        if (extractMinutes <= hangfireMinutes)
        {
            return;
        }

        _logger.LogWarning(
            "Extract timeout ({Extract} min) is greater than the Hangfire SQLite timeout ({Hangfire} min). " +
            "Raise JOB_TIMEOUT_MINUTES or EXTRACT_TIMEOUT_MINUTES so Hangfire does not reclaim the job during a cloud extract.",
            extractMinutes,
            hangfireMinutes);
    }

    private async Task<TimeSpan> GetExtractTimeoutAsync()
    {
        var value = await _settings.GetSetting(SettingKeys.Translation.ExtractTimeoutMinutes);
        return ExtractTimeout.Resolve(value);
    }

    private async Task WriteSubtitles(TranslationRequest translationRequest,
        List<SubtitleItem> translatedSubtitles,
        bool stripSubtitleFormatting,
        string subtitleTag,
        bool removeLanguageTag)
    {
        try
        {
            var targetLanguage = removeLanguageTag ? "" : translationRequest.TargetLanguage;

            var outputPath = _subtitleService.CreateFilePath(
                translationRequest.SubtitleToTranslate,
                targetLanguage,
                subtitleTag);

            await _subtitleService.WriteSubtitles(outputPath, translatedSubtitles, stripSubtitleFormatting);
            translationRequest.TranslatedSubtitle = outputPath;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("TranslateJob completed and created subtitle: |Green|{filePath}|/Green|",
                outputPath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    private async Task<bool> IsNoLongerRunnableAsync(int requestId, CancellationToken cancellationToken)
    {
        var storedStatus = await _dbContext.TranslationRequests
            .Where(storedRequest => storedRequest.Id == requestId)
            .Select(storedRequest => (TranslationStatus?)storedRequest.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (storedStatus is null or TranslationStatus.Completed or TranslationStatus.Cancelled)
        {
            _logger.LogInformation(
                "Skipping translation job for request {RequestId}, it is no longer runnable ({Status}).",
                requestId, storedStatus);
            return true;
        }

        return false;
    }

    private async Task<bool> ReloadAndSkipIfNotRunnableAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        var entry = _dbContext.Entry(request);
        if (entry.State != EntityState.Detached)
        {
            await entry.ReloadAsync(cancellationToken);
            if (request.Status is TranslationStatus.Completed or TranslationStatus.Cancelled)
            {
                _logger.LogInformation(
                    "Skipping translation job for request {RequestId} after extract ({Status}).",
                    request.Id, request.Status);
                return true;
            }

            return false;
        }

        return await IsNoLongerRunnableAsync(request.Id, cancellationToken);
    }

    private async Task HandleCompletion(
        string jobName,
        TranslationRequest translationRequest,
        CancellationToken cancellationToken)
    {
        if (await ReloadAndSkipIfNotRunnableAsync(translationRequest, cancellationToken))
        {
            return;
        }

        translationRequest.CompletedAt = DateTimeOffset.UtcNow;
        translationRequest.Status = TranslationStatus.Completed;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Completed);
        await _translationRequestService.UpdateActiveCount();
        await _progressService.Emit(translationRequest, 100);
        await _scheduleService.UpdateJobState(jobName, JobStatus.Succeeded.GetDisplayName());
    }

    /// <summary>
    /// Rebuilds the language pair an earlier run stored on a line. Exact matches are not persisted,
    /// so missing codes become null rather than a fake Exact pair.
    /// </summary>
    private static LanguagePair? StoredPair(string? matchedSource, string? matchedTarget, MatchTier? tier)
    {
        if (string.IsNullOrEmpty(matchedSource) || string.IsNullOrEmpty(matchedTarget))
        {
            return null;
        }

        return new LanguagePair
        {
            Source = matchedSource,
            Target = matchedTarget,
            Tier = tier ?? MatchTier.Exact
        };
    }

    private async Task HandleCancellation(string jobName, TranslationRequest request)
    {
        _logger.LogInformation("Translation cancelled for subtitle: |Orange|{subtitlePath}|/Orange|",
            request.SubtitleToTranslate);
        var translationRequest =
            await _dbContext.TranslationRequests.FirstOrDefaultAsync(translationRequest =>
                translationRequest.Id == request.Id);

        if (translationRequest != null)
        {
            translationRequest.CompletedAt = DateTimeOffset.UtcNow;
            translationRequest.Status = TranslationStatus.Cancelled;
            translationRequest.ErrorMessage = "Translation was cancelled";
            await _dbContext.SaveChangesAsync();
            await _eventService.LogEvent(translationRequest.Id, TranslationStatus.Cancelled, "Translation was cancelled");

            await _translationRequestService.ClearMediaHash(translationRequest);
            await _translationRequestService.UpdateActiveCount();
            await _progressService.Emit(translationRequest, 0);
            await _scheduleService.UpdateJobState(jobName, JobStatus.Cancelled.GetDisplayName());
        }
    }
}