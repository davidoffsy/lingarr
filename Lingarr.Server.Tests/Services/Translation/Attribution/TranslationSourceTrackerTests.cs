using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Models;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services.Translation.Attribution;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Attribution;

/// <summary>
/// Tests for the source attribution of a translation run: which provider and model produced which
/// position, and the aggregation used for the subtitle translator tag and the statistics.
/// </summary>
public class TranslationSourceTrackerTests
{
    [Fact]
    public void Summarize_OneSourcePerLine_CountsLinesPerSource()
    {
        var tracker = new TranslationSourceTracker();
        tracker.Record(1, new TranslationAttribution(new TranslationSource("custom", "model-a"), null));
        tracker.Record(2, new TranslationAttribution(new TranslationSource("custom", "model-a"), null));
        tracker.Record(3, new TranslationAttribution(new TranslationSource("openai", "gpt-4o"), null));

        var summary = tracker.Summarize();

        Assert.Equal(3, summary.TotalLines);
        Assert.True(summary.IsMixed);
        Assert.Equal("custom", summary.Dominant?.Source.Provider);
        Assert.Equal(2, summary.Dominant?.Lines);
        Assert.Equal("custom:model-a, openai:gpt-4o", summary.Describe());
    }

    [Fact]
    public void Summarize_SameSourceReportedManyTimes_StaysASingleEntry()
    {
        var tracker = new TranslationSourceTracker();
        for (var position = 1; position <= 5; position++)
        {
            tracker.Record(position, new TranslationAttribution(new TranslationSource("deepl", null), null));
        }

        var summary = tracker.Summarize();

        Assert.False(summary.IsMixed);
        Assert.Equal(5, summary.TotalLines);
        Assert.Equal("deepl", summary.Describe());
    }

    [Fact]
    public void Record_SamePositionTwice_KeepsTheLastSuccessfulSource()
    {
        var tracker = new TranslationSourceTracker();
        tracker.Record(1, new TranslationAttribution(new TranslationSource("openai", null), null));
        tracker.Record(1, new TranslationAttribution(new TranslationSource("deepl", null), null));

        var summary = tracker.Summarize();

        Assert.Equal(1, summary.TotalLines);
        Assert.Equal("deepl", summary.Describe());
    }

    [Fact]
    public void Summarize_Positions_OnlyCountsTheRequestedOnes()
    {
        var tracker = new TranslationSourceTracker();
        tracker.Record(1, new TranslationAttribution(new TranslationSource("openai", null), null));
        tracker.Record(2, new TranslationAttribution(new TranslationSource("custom", "model-b"), null));

        var summary = tracker.Summarize([2, 3]);

        Assert.Equal(1, summary.TotalLines);
        Assert.Equal("custom:model-b", summary.Describe());
    }

    [Fact]
    public void Summarize_WithoutRecordedPositions_IsEmpty()
    {
        var summary = new TranslationSourceTracker().Summarize();

        Assert.True(summary.IsEmpty);
        Assert.False(summary.IsMixed);
        Assert.Null(summary.Dominant);
        Assert.Equal(0, summary.TotalLines);
        Assert.Equal(string.Empty, summary.Describe());
    }

    [Fact]
    public void TryGet_UnknownPosition_ReturnsFalse()
    {
        var tracker = new TranslationSourceTracker();

        Assert.False(tracker.TryGet(7, out _));
        Assert.True(tracker.IsEmpty);
    }

    [Fact]
    public void Summarize_SourcesWithEqualLineCounts_AreOrderedByProviderThenModel()
    {
        var summary = TranslationSourceSummary.From(
        [
            new TranslationSource("openai", "b-model"),
            new TranslationSource("custom", "z-model"),
            new TranslationSource("custom", "a-model")
        ]);

        Assert.Equal(
            new[] { "custom:a-model", "custom:z-model", "openai:b-model" },
            summary.Counts.Select(count => count.Source.DisplayName));
    }

    [Fact]
    public void TranslationSource_FromCandidate_PrefersTheCandidatesModelOverride()
    {
        var service = new FakeModelService("configured-model");

        var withOverride = TranslationSource.FromCandidate(
            new TranslationServiceEntry("openai", service, null, "override-model"));
        var withoutOverride = TranslationSource.FromCandidate(
            new TranslationServiceEntry("openai", service, null));

        Assert.Equal("openai:override-model", withOverride.DisplayName);
        Assert.Equal("openai:configured-model", withoutOverride.DisplayName);
    }

    private sealed class FakeModelService : Lingarr.Contracts.Translation.ITranslationService
    {
        public FakeModelService(string? model) => ModelName = model;

        public string? ModelName { get; }

        public Task<string> TranslateAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            List<string>? contextLinesBefore,
            List<string>? contextLinesAfter,
            CancellationToken cancellationToken) => Task.FromResult(text);

        public Task<List<SourceLanguage>> GetLanguages() => Task.FromResult(new List<SourceLanguage>());

        public Task<ModelsResponse> GetModels() => Task.FromResult(new ModelsResponse());

        public Task<LanguagePair?> GetLanguagePair(
            string requestedSource,
            string requestedTarget,
            CancellationToken cancellationToken) => Task.FromResult<LanguagePair?>(null);
    }
}
