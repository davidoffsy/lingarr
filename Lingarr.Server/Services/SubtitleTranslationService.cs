using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Core.Entities;
using Lingarr.Server.Extensions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Translation.Attribution;
using Lingarr.Server.Services.Translation.Batch;
using Lingarr.Server.Services.Translation.Chain;

namespace Lingarr.Server.Services;

public class SubtitleTranslationService
{
    private const int MaxLineLength = 42;

    /// <summary>
    /// The wire shape of one context line when structured context is enabled: the position, the
    /// line text, and the translation for lines that already have one. Written only, Lingarr never
    /// reads it back; the shape is documented for plugin authors in the plugin docs.
    /// </summary>
    private sealed class ContextLine
    {
        [JsonPropertyName("position")]
        public int Position { get; init; }

        [JsonPropertyName("line")]
        public string Line { get; init; } = string.Empty;

        [JsonPropertyName("translation")]
        public string? Translation { get; init; }
    }

    /// <summary>
    /// Keeps non-ASCII text readable in context lines instead of \uXXXX escapes, and drops the
    /// translation property for lines that do not have one yet.
    /// </summary>
    private static readonly JsonSerializerOptions ContextLineJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// One cached translation of a stacked line (.ass layers that share timing and text). It carries
    /// the attribution of the line the translation came from, so reused positions are credited to the
    /// same provider and model instead of counting as untranslated.
    /// </summary>
    private readonly record struct CachedLine(string Translation, TranslationSource? Source, LanguagePair? Pair);

    private int _lastProgression = -1;
    private readonly IReadOnlyList<TranslationServiceEntry> _services;
    private readonly IProgressService? _progressService;
    private readonly ILogger _logger;
    private readonly bool _useTranslatedContext;
    private readonly bool _mergeStackedLines;
    private readonly TranslationCandidateScheduler _scheduler;
    private readonly TranslationSourceTracker _sources = new();
    private readonly HashSet<string> _unusableProviders = [];

    /// <param name="services">Ordered translation service chain; the first entry is the primary, the rest are fallbacks.</param>
    /// <param name="logger">Logger for progress and fallback diagnostics.</param>
    /// <param name="progressService">Progress reporter, required by <see cref="TranslateSubtitles"/> and <see cref="TranslateSubtitlesBatch"/>.</param>
    /// <param name="useTranslatedContext">
    /// When true, context lines are passed as one JSON object per line carrying the position and
    /// line text, plus the translation for lines that already have one.
    /// When false, context lines are passed as plain source text.
    /// </param>
    /// <param name="mergeStackedLines">
    /// When true (the default), subtitles that share start time, end time and text are translated once
    /// and the result is reused, which collapses the stacked Dialogue layers of fansub .ass files.
    /// Pass false for input without timing information, where nothing can be stacked.
    /// </param>
    public SubtitleTranslationService(
        IReadOnlyList<TranslationServiceEntry> services,
        ILogger logger,
        IProgressService? progressService = null,
        bool useTranslatedContext = false,
        bool mergeStackedLines = true)
    {
        if (services.Count == 0)
        {
            throw new TranslationException("Subtitle translator could not be initialized, translation services list is empty.");
        }
        _services = services;
        _progressService = progressService;
        _logger = logger;
        _useTranslatedContext = useTranslatedContext;
        _mergeStackedLines = mergeStackedLines;
        _scheduler = new TranslationCandidateScheduler(services, logger);
    }

    /// <summary>
    /// Provider and model that produced each position translated by this instance. Positions are
    /// recorded once per successful translation, so callers can attribute lines, statistics and the
    /// subtitle translator tag to the source that really translated them.
    /// </summary>
    public IReadOnlyDictionary<int, TranslationAttribution> AttributionByPosition => _sources.ByPosition;

    /// <summary>
    /// Line counts per provider and model over everything this instance translated, including lines
    /// that were already translated when the run started.
    /// </summary>
    public TranslationSourceSummary SourceSummary => _sources.Summarize();

    /// <summary>Line counts per source restricted to <paramref name="positions"/>.</summary>
    public TranslationSourceSummary SourceSummaryOf(IEnumerable<int> positions) => _sources.Summarize(positions);

    /// <summary>
    /// Pre-loads attribution for positions that an earlier run already translated, so summaries and
    /// the subtitle translator tag describe the whole file after a resume. Positions translated by
    /// this run overwrite their seeded attribution.
    /// </summary>
    public void SeedAttribution(IReadOnlyDictionary<int, TranslationAttribution> attribution)
    {
        ArgumentNullException.ThrowIfNull(attribution);

        foreach (var (position, value) in attribution)
        {
            _sources.Record(position, value);
        }
    }

    /// <summary>
    /// Translates a list of subtitle items from the source language to the target language.
    /// </summary>
    /// <param name="subtitles">The list of subtitle items to translate.</param>
    /// <param name="translationRequest">Contains the source and target language specifications.</param>
    /// <param name="stripSubtitleFormatting">Boolean used for indicating that styles need to be stripped from the subtitle</param>
    /// <param name="preserveLineBreaks">When true, multi-line subtitles are translated keeping their line breaks intact</param>
    /// <param name="contextBefore">Amount of context before the subtitle line</param>
    /// <param name="contextAfter">Amount of context after the subtitle line</param>
    /// <param name="cancellationToken">Token to support cancellation of the translation operation.</param>
    public async Task<List<SubtitleItem>> TranslateSubtitles(
        List<SubtitleItem> subtitles,
        TranslationRequest translationRequest,
        bool stripSubtitleFormatting,
        bool preserveLineBreaks,
        int contextBefore,
        int contextAfter,
        CancellationToken cancellationToken)
    {
        if (_progressService == null)
        {
            throw new TranslationException("Subtitle translator could not be initialized, progress service is null.");
        }

        var iteration = 0;
        var totalSubtitles = subtitles.Count;

        // Many fansub .ass files stack multiple Dialogue lines at the same
        // timestamp with identical plaintext (shadow, glow, border, main
        // layers). Translate each unique (Start, End, plaintext) once per
        // file and reuse the result for the rest. SRT/VTT files almost never
        // share timestamps, so this is a no-op there.
        var translationCache = new Dictionary<string, CachedLine>();

        for (var index = 0; index < totalSubtitles; index++)
        {
            var subtitle = subtitles[index];

            if (cancellationToken.IsCancellationRequested)
            {
                _lastProgression = -1;
                break;
            }

            if (subtitle.TranslatedLines.Count > 0)
            {
                var existingContentLines = stripSubtitleFormatting ? subtitle.PlaintextLines : subtitle.Lines;
                if (_mergeStackedLines && !(preserveLineBreaks && existingContentLines.Count > 1))
                {
                    var sourceLine = string.Join(" ", existingContentLines);
                    if (!string.IsNullOrWhiteSpace(sourceLine))
                    {
                        var cacheKey = $"{subtitle.StartTime}|{subtitle.EndTime}|{sourceLine}";
                        // Carry the attribution of the line the translation came from (seeded
                        // attribution from earlier runs included), so a reused position is credited to
                        // the provider/model that really produced it instead of counting as un-attributed.
                        _sources.TryGet(subtitle.Position, out var cachedAttribution);
                        translationCache.TryAdd(
                            cacheKey,
                            new CachedLine(
                                string.Join(" ", subtitle.TranslatedLines),
                                cachedAttribution.Source,
                                cachedAttribution.Pair));
                    }
                }

                iteration++;
                await EmitProgress(translationRequest, iteration, totalSubtitles);
                continue;
            }

            var contextLinesBefore = BuildContext(subtitles, index, contextBefore, stripSubtitleFormatting, true);
            var contextLinesAfter = BuildContext(subtitles, index, contextAfter, stripSubtitleFormatting, false);

            var contentLines = stripSubtitleFormatting ? subtitle.PlaintextLines : subtitle.Lines;
            var subtitleLines = preserveLineBreaks && contentLines.Count > 1
                ? contentLines
                : [string.Join(" ", contentLines)];

            var translatedLines = new List<string>(subtitleLines.Count);
            TranslationSource? source = null;
            LanguagePair? pair = null;
            foreach (var subtitleLine in subtitleLines)
            {
                if (string.IsNullOrWhiteSpace(subtitleLine))
                {
                    translatedLines.Add(subtitleLine);
                    continue;
                }

                var cacheKey = $"{subtitle.StartTime}|{subtitle.EndTime}|{subtitleLine}";
                if (_mergeStackedLines && translationCache.TryGetValue(cacheKey, out var cachedTranslation))
                {
                    translatedLines.Add(cachedTranslation.Translation);
                    source ??= cachedTranslation.Source;
                    pair ??= cachedTranslation.Pair;
                    continue;
                }

                var result = await TranslateSubtitleLine(new TranslateAbleSubtitleLine
                {
                    SubtitleLine = subtitleLine,
                    SourceLanguage = translationRequest.SourceLanguage,
                    TargetLanguage = translationRequest.TargetLanguage,
                    ContextLinesBefore = contextLinesBefore.Count > 0 ? contextLinesBefore : null,
                    ContextLinesAfter = contextLinesAfter.Count > 0 ? contextLinesAfter : null
                }, cancellationToken);
                if (_mergeStackedLines)
                {
                    translationCache[cacheKey] = new CachedLine(result.Translation, result.Source, result.Pair);
                }
                translatedLines.Add(result.Translation);
                source ??= result.Source;
                pair ??= result.Pair;
            }

            subtitle.TranslatedLines = translatedLines.Count > 1
                ? translatedLines
                : ToSubtitleLines(translatedLines[0], contentLines.Count, preserveLineBreaks, stripSubtitleFormatting, subtitle.Position);

            var sourceText = string.Join(" ", contentLines);
            var translatedText = string.Join(" ", subtitle.TranslatedLines);
            if (source != null && pair != null)
            {
                _sources.Record(subtitle.Position, new TranslationAttribution(source.Value, pair));
            }

            await _progressService!.EmitLine(
                translationRequest,
                subtitle.Position,
                sourceText,
                translatedText,
                service: source?.Provider,
                pair: pair,
                model: source?.Model);

            iteration++;
            await EmitProgress(translationRequest, iteration, totalSubtitles);
        }

        _lastProgression = -1;
        return subtitles;
    }

    /// <summary>
    /// Translates a single subtitle line, walking configured services in best-match order and falling back on per-service failure.
    /// </summary>
    /// <returns>The translated line, the provider and model that produced it, and the language pair used.</returns>
    public async Task<(string Translation, TranslationSource Source, LanguagePair Pair)> TranslateSubtitleLine(
        TranslateAbleSubtitleLine translateAbleSubtitle,
        CancellationToken cancellationToken)
    {
        var candidates = await _scheduler.GetCandidates(
            translateAbleSubtitle.SourceLanguage,
            translateAbleSubtitle.TargetLanguage,
            batch: false,
            cancellationToken);

        if (candidates.Count == 0)
        {
            throw new TranslationException(
                $"All configured translation services were skipped, no service supports {translateAbleSubtitle.SourceLanguage}->{translateAbleSubtitle.TargetLanguage}.");
        }

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsProviderUnusable(candidate))
            {
                continue;
            }

            try
            {
                var translated = await candidate.Entry.Service.TranslateAsync(
                    translateAbleSubtitle.SubtitleLine,
                    candidate.Pair.Source,
                    candidate.Pair.Target,
                    translateAbleSubtitle.ContextLinesBefore,
                    translateAbleSubtitle.ContextLinesAfter,
                    cancellationToken);
                _scheduler.LogFallback(candidate, translateAbleSubtitle.SourceLanguage, translateAbleSubtitle.TargetLanguage);
                return (translated, TranslationSource.FromCandidate(candidate.Entry), candidate.Pair);
            }
            catch (TranslationAuthenticationException ex)
            {
                MarkProviderUnusable(candidate, ex,
                    "the provider rejected the configured credentials.");
                lastError = ex;
            }
            catch (TranslationConfigurationException ex)
            {
                MarkProviderUnusable(candidate, ex,
                    "the candidate's settings cannot produce a request (missing endpoint, key or an unusable address).");
                lastError = ex;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Translation service {Service} failed.", candidate.Entry.Describe());
            }
        }

        throw new TranslationException("All configured translation services failed for subtitle line.", lastError);
    }

    /// <summary>
    /// True when the candidate's provider already failed in a way that affects every model of that
    /// provider during this run (rejected credentials, unusable configuration). Only failures that can
    /// differ per model are retried with the provider's next candidate; the chain continues with the
    /// other providers.
    /// </summary>
    private bool IsProviderUnusable(TranslationCandidate candidate) =>
        _unusableProviders.Contains(candidate.Entry.Name);

    private void MarkProviderUnusable(TranslationCandidate candidate, Exception failure, string reason)
    {
        if (_unusableProviders.Add(candidate.Entry.Name))
        {
            _logger.LogWarning(
                failure,
                "Skipping the remaining {Provider} candidates: {Reason}",
                candidate.Entry.Describe(),
                reason);
        }
    }

    /// <summary>
    /// Translates subtitles in batch mode.
    /// </summary>
    public async Task<List<SubtitleItem>> TranslateSubtitlesBatch(
        List<SubtitleItem> subtitles,
        TranslationRequest translationRequest,
        bool stripSubtitleFormatting,
        bool preserveLineBreaks,
        int batchSize = 0,
        CancellationToken cancellationToken = default)
    {
        if (_progressService == null)
        {
            throw new TranslationException("Subtitle translator could not be initialized, progress service is null.");
        }

        if (batchSize <= 0)
        {
            batchSize = subtitles.Count;
        }

        var totalBatches = (int)Math.Ceiling((double)subtitles.Count / batchSize);
        var processedSubtitles = 0;

        for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _lastProgression = -1;
                break;
            }

            var currentBatch = subtitles
                .Skip(batchIndex * batchSize)
                .Take(batchSize)
                .ToList();

            var pending = currentBatch.Where(subtitle => subtitle.TranslatedLines.Count == 0).ToList();
            try
            {
                var newlyTranslated = await ProcessSubtitleBatch(
                    currentBatch,
                    translationRequest.SourceLanguage,
                    translationRequest.TargetLanguage,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    cancellationToken);

                await EmitTranslatedLines(translationRequest, newlyTranslated, stripSubtitleFormatting);
            }
            catch
            {
                var partialHits = pending.Where(subtitle => subtitle.TranslatedLines.Count > 0).ToList();
                await EmitTranslatedLines(translationRequest, partialHits, stripSubtitleFormatting);
                throw;
            }

            processedSubtitles += currentBatch.Count;
            await EmitProgress(translationRequest, processedSubtitles, subtitles.Count);
        }

        _lastProgression = -1;
        return subtitles;
    }

    /// <summary>
    /// Translates a batch of subtitles, walking batch-capable services in best-match order
    /// and falling back on per-service failure. When every batch candidate is unusable or fails,
    /// remaining lines are translated through the per-line chain instead of failing the file.
    /// </summary>
    public async Task<List<SubtitleItem>> ProcessSubtitleBatch(
        List<SubtitleItem> currentBatch,
        string sourceLanguage,
        string targetLanguage,
        bool stripSubtitleFormatting,
        bool preserveLineBreaks,
        CancellationToken cancellationToken)
    {
        var toTranslate = currentBatch.Where(subtitle => subtitle.TranslatedLines.Count == 0).ToList();
        if (toTranslate.Count == 0)
        {
            return [];
        }

        var candidates = await _scheduler.GetCandidates(sourceLanguage, targetLanguage, batch: true, cancellationToken);
        var capableCandidates = candidates.Where(candidate => candidate.Entry.BatchService is not null).ToList();

        if (capableCandidates.Count == 0)
        {
            // Batch translation was requested but no batch-capable candidate supports this language
            // pair. Degrade to the per-line chain (which still honours the configured order) instead of
            // failing the whole file, because the per-line candidates may well support the pair.
            _logger.LogWarning(
                "No batch-capable translation candidate supports {Source}->{Target}; falling back to line translation.",
                sourceLanguage,
                targetLanguage);
            await TranslatePendingLinesAsync(
                toTranslate,
                sourceLanguage,
                targetLanguage,
                stripSubtitleFormatting,
                preserveLineBreaks,
                cancellationToken);
            return toTranslate;
        }

        Exception? lastError = null;
        foreach (var candidate in capableCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsProviderUnusable(candidate))
            {
                continue;
            }

            try
            {
                await RunBatch(
                    candidate,
                    toTranslate,
                    stripSubtitleFormatting,
                    preserveLineBreaks,
                    cancellationToken);
                _scheduler.LogFallback(candidate, sourceLanguage, targetLanguage);
                return toTranslate;
            }
            catch (TranslationAuthenticationException ex)
            {
                MarkProviderUnusable(candidate, ex,
                    "the provider rejected the configured credentials.");
                lastError = ex;
            }
            catch (TranslationConfigurationException ex)
            {
                MarkProviderUnusable(candidate, ex,
                    "the candidate's settings cannot produce a request (missing endpoint, key or an unusable address).");
                lastError = ex;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Batch translation service {Service} failed.", candidate.Entry.Describe());
            }
        }

        // Batch candidates are exhausted, but the chain may still have per-line fallbacks
        // (or the same providers succeeding on a single-line request shape).
        _logger.LogWarning(
            lastError,
            "All batch-capable translation candidates failed for {Source}->{Target}; falling back to line translation.",
            sourceLanguage,
            targetLanguage);
        await TranslatePendingLinesAsync(
            toTranslate,
            sourceLanguage,
            targetLanguage,
            stripSubtitleFormatting,
            preserveLineBreaks,
            cancellationToken);
        return toTranslate;
    }

    /// <summary>
    /// Translates the pending subtitles through the per-line chain and records their attribution.
    /// Used when batch translation was requested but no batch-capable candidate supports the language
    /// pair, or every batch candidate failed, so the request degrades to line translation instead of
    /// failing the whole file.
    /// </summary>
    private async Task TranslatePendingLinesAsync(
        List<SubtitleItem> toTranslate,
        string sourceLanguage,
        string targetLanguage,
        bool stripSubtitleFormatting,
        bool preserveLineBreaks,
        CancellationToken cancellationToken)
    {
        foreach (var subtitle in toTranslate.Where(subtitle => subtitle.TranslatedLines.Count == 0))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var contentLines = stripSubtitleFormatting ? subtitle.PlaintextLines : subtitle.Lines;
            var subtitleLines = preserveLineBreaks && contentLines.Count > 1
                ? contentLines
                : [string.Join(" ", contentLines)];

            var translatedLines = new List<string>(subtitleLines.Count);
            TranslationSource? source = null;
            LanguagePair? pair = null;
            foreach (var subtitleLine in subtitleLines)
            {
                if (string.IsNullOrWhiteSpace(subtitleLine))
                {
                    translatedLines.Add(subtitleLine);
                    continue;
                }

                var result = await TranslateSubtitleLine(new TranslateAbleSubtitleLine
                {
                    SubtitleLine = subtitleLine,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage
                }, cancellationToken);
                translatedLines.Add(result.Translation);
                source ??= result.Source;
                pair ??= result.Pair;
            }

            subtitle.TranslatedLines = translatedLines.Count > 1
                ? translatedLines
                : ToSubtitleLines(translatedLines[0], contentLines.Count, preserveLineBreaks,
                    stripSubtitleFormatting, subtitle.Position);

            if (source != null && pair != null)
            {
                _sources.Record(subtitle.Position, new TranslationAttribution(source.Value, pair));
            }
        }
    }

    private async Task RunBatch(
        TranslationCandidate candidate,
        List<SubtitleItem> toTranslate,
        bool stripSubtitleFormatting,
        bool preserveLineBreaks,
        CancellationToken cancellationToken)
    {
        var pending = toTranslate.Where(subtitle => subtitle.TranslatedLines.Count == 0).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var lineSeparator = preserveLineBreaks ? "\n" : " ";
        var batchItems = pending.Select(subtitle =>
        {
            var contentLines = stripSubtitleFormatting ? subtitle.PlaintextLines : subtitle.Lines;
            return new BatchSubtitleItem
            {
                Position = subtitle.Position,
                Line = string.Join(lineSeparator, contentLines)
            };
        }).ToList();

        var batchResults = new Dictionary<int, string>();
        var fillCompleted = false;
        try
        {
            await BatchFillLoop.FillAsync(
                candidate.Entry.BatchService!,
                batchItems,
                candidate.Pair.Source,
                candidate.Pair.Target,
                _logger,
                cancellationToken,
                batchResults);
            fillCompleted = true;
        }
        finally
        {
            foreach (var subtitle in pending)
            {
                if (subtitle.TranslatedLines.Count > 0)
                {
                    continue;
                }

                var contentLines = stripSubtitleFormatting ? subtitle.PlaintextLines : subtitle.Lines;
                if (batchResults.TryGetValue(subtitle.Position, out var translated))
                {
                    if (stripSubtitleFormatting)
                    {
                        translated = SubtitleFormatterService.RemoveMarkup(translated);
                    }

                    subtitle.TranslatedLines = ToSubtitleLines(
                        translated,
                        contentLines.Count,
                        preserveLineBreaks,
                        stripSubtitleFormatting,
                        subtitle.Position);
                    _sources.Record(
                        subtitle.Position,
                        new TranslationAttribution(
                            TranslationSource.FromCandidate(candidate.Entry),
                            candidate.Pair));
                    continue;
                }

                if (!fillCompleted)
                {
                    continue;
                }

                _logger.LogWarning(
                    "Translation not found for subtitle at position {Position} using original line.",
                    subtitle.Position);
                subtitle.TranslatedLines = contentLines;
            }
        }
    }

    /// <summary>
    /// Resolves a raw translated string back into a list of subtitle lines.
    /// </summary>
    private List<string> ToSubtitleLines(
        string translated,
        int originalLineCount,
        bool preserveLineBreaks,
        bool stripSubtitleFormatting,
        int position)
    {
        if (preserveLineBreaks && originalLineCount > 1)
        {
            var splitLines = translated.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            if (splitLines.Count == originalLineCount)
            {
                return splitLines;
            }

            _logger.LogWarning(
                "Line break count mismatch at position {Position} (expected {Expected}, got {Actual}); collapsing to single line.",
                position, originalLineCount, splitLines.Count);
            return [string.Join(" ", splitLines)];
        }

        return originalLineCount > 1 && stripSubtitleFormatting
            ? translated.SplitIntoLines(MaxLineLength)
            : [translated];
    }

    /// <summary>
    /// Builds a list of subtitle text strings as context around a given subtitle index.
    /// </summary>
    /// <param name="subtitles">The list of subtitle items.</param>
    /// <param name="startIndex">The index around which to build context.</param>
    /// <param name="count">The number of subtitles to include before or after the index.</param>
    /// <param name="stripSubtitleFormatting">Whether to strip formatting from subtitles.</param>
    /// <param name="isBeforeContext">If true, builds context before the index; otherwise, builds after.</param>
    /// <remarks>
    /// With <c>useTranslatedContext</c> enabled every context line becomes one JSON object with its
    /// position and text; "before" lines that already carry a non-empty translation include it, so
    /// the model sees how earlier lines were translated. "After" lines never have a translation yet.
    /// With the option disabled, lines are plain source text.
    /// </remarks>
    private List<string> BuildContext(
        List<SubtitleItem> subtitles, 
        int startIndex, 
        int count,
        bool stripSubtitleFormatting, 
        bool isBeforeContext)
    {
        List<string> context = [];

        var start = isBeforeContext
            ? Math.Max(0, startIndex - count)
            : startIndex + 1;

        var end = isBeforeContext
            ? startIndex
            : Math.Min(subtitles.Count, startIndex + 1 + count);

        for (var i = start; i < end; i++)
        {
            var contextSubtitle = subtitles[i];
            var sourceText = string.Join(" ",
                stripSubtitleFormatting ? contextSubtitle.PlaintextLines : contextSubtitle.Lines);

            if (!_useTranslatedContext)
            {
                context.Add(sourceText);
                continue;
            }

            // An empty stored translation (the model answered nothing) is not an example to follow,
            // so such a line is passed like one that has not been translated yet.
            var translatedText = isBeforeContext && contextSubtitle.TranslatedLines.Count > 0
                ? string.Join(" ", contextSubtitle.TranslatedLines)
                : null;
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                translatedText = null;
            }

            context.Add(JsonSerializer.Serialize(
                new ContextLine { Position = contextSubtitle.Position, Line = sourceText, Translation = translatedText },
                ContextLineJsonOptions));
        }

        return context.Count > 0 ? context : [];
    }

    private async Task EmitTranslatedLines(
        TranslationRequest translationRequest,
        List<SubtitleItem> subtitles,
        bool stripSubtitleFormatting)
    {
        if (subtitles.Count == 0)
        {
            return;
        }

        var lineData = subtitles.Select(subtitle =>
        {
            _sources.TryGet(subtitle.Position, out var attribution);
            return new TranslatedLineData
            {
                Position = subtitle.Position,
                Source = string.Join(" ", stripSubtitleFormatting ? subtitle.PlaintextLines : subtitle.Lines),
                Target = string.Join(" ", subtitle.TranslatedLines),
                Service = attribution == default ? null : attribution.Source.Provider,
                Model = attribution == default ? null : attribution.Source.Model,
                Pair = attribution.Pair
            };
        }).ToList();
        await _progressService!.EmitLines(translationRequest, lineData);
    }

    /// <summary>
    /// Emits translation progress updates if progress has changed since the last emission.
    /// </summary>
    /// <param name="request">The translation request being processed.</param>
    /// <param name="iteration">The current subtitle index being processed.</param>
    /// <param name="total">The total number of subtitles in the request.</param>
    private async Task EmitProgress(TranslationRequest request, int iteration, int total)
    {
        var progress = (int)Math.Round((double)iteration * 100 / total);

        if (progress != _lastProgression)
        {
            _logger.LogInformation($"Progress: {progress}% (Subtitle {iteration} of {total})");
            await _progressService!.Emit(request, progress);
            _lastProgression = progress;
        }
    }
}
