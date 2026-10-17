using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Contracts.Translation;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SubtitleTranslationServiceTests
{
    private static TranslationRequest NewRequest() => new()
    {
        Title = "test",
        SourceLanguage = "en",
        TargetLanguage = "es",
        MediaType = MediaType.Movie,
        Status = TranslationStatus.InProgress
    };

    private static SubtitleItem Subtitle(int position, params string[] lines) => new()
    {
        Position = position,
        Lines = lines.ToList(),
        PlaintextLines = lines.ToList()
    };

    /// <summary>
    /// Mirrors how <c>TranslationRequestService.TranslateContentAsync</c> builds items for the
    /// <c>/api/translate/content</c> endpoint: position and text only, no timing information.
    /// </summary>
    private static SubtitleItem ContentLine(int position, string line) => Subtitle(position, line);

    private sealed record TranslateCall(string Text, List<string>? ContextBefore, List<string>? ContextAfter);

    private static readonly JsonSerializerOptions ExpectedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Expected context entry when translated context is enabled. Built from a dictionary on purpose,
    /// so the tests pin the wire format itself rather than share a type with the code under test.
    /// </summary>
    private static string ContextJson(int position, string line, string? translation = null)
    {
        var entry = new Dictionary<string, object> { ["position"] = position, ["line"] = line };
        if (translation is not null)
        {
            entry["translation"] = translation;
        }
        return JsonSerializer.Serialize(entry, ExpectedJsonOptions);
    }

    private sealed class PerLineHarness
    {
        public Mock<ITranslationService> TranslationServiceMock { get; init; } = null!;
        public Mock<IProgressService> ProgressServiceMock { get; init; } = null!;
        public SubtitleTranslationService Service { get; init; } = null!;
        public List<TranslateCall> Calls { get; init; } = [];
    }

    private sealed class BatchHarness
    {
        public Mock<IProgressService> ProgressServiceMock { get; init; } = null!;
        public SubtitleTranslationService Service { get; init; } = null!;
        public List<List<int>> BatchCalls { get; init; } = [];
        public List<List<int>> FallbackCalls { get; init; } = [];
        public List<List<TranslatedLineData>> EmittedLines { get; init; } = [];
    }

    private static PerLineHarness CreatePerLineHarness(
        Func<string, string> translate,
        bool useTranslatedContext = false,
        bool mergeStackedLines = true)
    {
        var calls = new List<TranslateCall>();
        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(t => t.GetLanguagePair(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string source, string target, CancellationToken _) => new LanguagePair { Source = source, Target = target, Tier = MatchTier.Exact });
        translationServiceMock
            .Setup(t => t.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, string _, List<string>? before, List<string>? after, CancellationToken _) =>
            {
                calls.Add(new TranslateCall(text, before?.ToList(), after?.ToList()));
                return translate(text);
            });

        var progressServiceMock = new Mock<IProgressService>();
        progressServiceMock
            .Setup(p => p.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        progressServiceMock
            .Setup(progressService => progressService.EmitLine(
                It.IsAny<TranslationRequest>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<LanguagePair?>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        progressServiceMock
            .Setup(p => p.EmitLines(It.IsAny<TranslationRequest>(), It.IsAny<List<TranslatedLineData>>()))
            .Returns(Task.CompletedTask);

        return new PerLineHarness
        {
            TranslationServiceMock = translationServiceMock,
            ProgressServiceMock = progressServiceMock,
            Service = new SubtitleTranslationService(
                [new TranslationServiceEntry("test", translationServiceMock.Object, null)],
                NullLogger.Instance,
                progressServiceMock.Object,
                useTranslatedContext,
                mergeStackedLines),
            Calls = calls
        };
    }

    private static BatchHarness CreateBatchHarness(
        Func<List<BatchSubtitleItem>, Dictionary<int, string>> batchTranslate,
        Func<List<BatchSubtitleItem>, Dictionary<int, string>>? fallbackTranslate = null)
    {
        var batchCalls = new List<List<int>>();
        var fallbackCalls = new List<List<int>>();
        var emittedLines = new List<List<TranslatedLineData>>();
        var translationServiceMock = new Mock<ITranslationService>();
        translationServiceMock
            .Setup(t => t.GetLanguagePair(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string source, string target, CancellationToken _) => new LanguagePair { Source = source, Target = target, Tier = MatchTier.Exact });
        var batchTranslationServiceMock = new Mock<IBatchTranslationService>();
        batchTranslationServiceMock
            .Setup(b => b.TranslateBatchAsync(
                It.IsAny<List<BatchSubtitleItem>>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<BatchSubtitleItem> items, string _, string _, CancellationToken _) =>
            {
                batchCalls.Add(items.Select(item => item.Position).ToList());
                return batchTranslate(items);
            });

        var services = new List<TranslationServiceEntry>
        {
            new("test", translationServiceMock.Object, batchTranslationServiceMock.Object)
        };

        if (fallbackTranslate != null)
        {
            var fallbackServiceMock = new Mock<ITranslationService>();
            fallbackServiceMock
                .Setup(t => t.GetLanguagePair(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((string source, string target, CancellationToken _) => new LanguagePair { Source = source, Target = target, Tier = MatchTier.Exact });
            var fallbackBatchMock = new Mock<IBatchTranslationService>();
            fallbackBatchMock
                .Setup(b => b.TranslateBatchAsync(
                    It.IsAny<List<BatchSubtitleItem>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((List<BatchSubtitleItem> items, string _, string _, CancellationToken _) =>
                {
                    fallbackCalls.Add(items.Select(item => item.Position).ToList());
                    return fallbackTranslate(items);
                });
            services.Add(new TranslationServiceEntry("fallback", fallbackServiceMock.Object, fallbackBatchMock.Object));
        }

        var progressServiceMock = new Mock<IProgressService>();
        progressServiceMock
            .Setup(p => p.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        progressServiceMock
            .Setup(p => p.EmitLines(It.IsAny<TranslationRequest>(), It.IsAny<List<TranslatedLineData>>()))
            .Callback<TranslationRequest, List<TranslatedLineData>>((_, lines) =>
                emittedLines.Add(lines.ToList()))
            .Returns(Task.CompletedTask);

        return new BatchHarness
        {
            ProgressServiceMock = progressServiceMock,
            Service = new SubtitleTranslationService(
                services,
                NullLogger.Instance,
                progressServiceMock.Object),
            BatchCalls = batchCalls,
            FallbackCalls = fallbackCalls,
            EmittedLines = emittedLines
        };
    }

    #region TranslateSubtitles Tests

    [Fact]
    public async Task TranslateSubtitles_SingleLine_TranslatesAsOneCallAndReturnsOneLine()
    {
        // Arrange
        var harness = CreatePerLineHarness(_ => "hola");
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hola"], subtitles[0].TranslatedLines);
        harness.TranslationServiceMock.Verify(
            t => t.TranslateAsync("hello", "en", "es", null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateSubtitles_MultiLinePreserveOff_MergesIntoOneCallAndReturnsOneLine()
    {
        // Arrange
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text =>
        {
            captured.Add(text);
            return "hola mundo";
        });
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hello world"], captured);
        Assert.Equal(["hola mundo"], subtitles[0].TranslatedLines);
        harness.TranslationServiceMock.Verify(
            t => t.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateSubtitles_MultiLinePreserveOffStripOn_RewrapsLongTranslation()
    {
        // Arrange - multi-line merged input with a long translation; rewrap kicks in because strip is on
        var harness = CreatePerLineHarness(_ => "Esta es una traducción bastante larga que sin duda excederá el límite de cuarenta y dos caracteres");
        var subtitles = new List<SubtitleItem> { Subtitle(1, "line a", "line b") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: true,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        var translated = subtitles[0].TranslatedLines;
        Assert.True(translated.Count > 1, "Expected the long translation to be wrapped into multiple lines");
        Assert.All(translated, line => Assert.True(line.Length <= 42, $"Line exceeded max length: '{line}'"));
    }

    [Fact]
    public async Task TranslateSubtitles_MultiLinePreserveOn_TranslatesEachLineSeparately()
    {
        // Arrange
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text =>
        {
            captured.Add(text);
            return text == "hello" ? "hola" : "mundo";
        });
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: true,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hello", "world"], captured);
        Assert.Equal(["hola", "mundo"], subtitles[0].TranslatedLines);
        harness.TranslationServiceMock.Verify(
            t => t.TranslateAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task TranslateSubtitles_PreserveOnPerLineTranslationWithEmbeddedNewline_KeepsModelOutputVerbatim()
    {
        // Arrange - a per-line translation contains a literal '\n'. We must NOT round-trip through ToSubtitleLines
        // (which would split on '\n' and trip the count-mismatch fallback). Output should be stored as the model returned.
        var harness = CreatePerLineHarness(text => text == "hello" ? "ho\nla" : "mundo");
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: true,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["ho\nla", "mundo"], subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitles_WhitespaceLine_SkipsTranslationAndPassesThrough()
    {
        // Arrange
        var translatedCalls = 0;
        var harness = CreatePerLineHarness(_ =>
        {
            translatedCalls++;
            return "mundo";
        });
        var subtitles = new List<SubtitleItem> { Subtitle(1, "", "world") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: true,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["", "mundo"], subtitles[0].TranslatedLines);
        Assert.Equal(1, translatedCalls);
    }

    // ===== Per-file dedup =====
    // Many fansub .ass files stack multiple Dialogue lines at the same timestamp
    // with byte-identical plaintext (shadow/glow/border/main rendered as separate
    // entries). The service caches by (StartTime, EndTime, plaintext) and reuses
    // the translation for the duplicates.

    [Fact]
    public async Task TranslateSubtitles_StackedDuplicates_TranslatesOnceAndReusesResult()
    {
        // Arrange - four entries with the same (Start, End, plaintext): the layered shadow/glow/border/main case
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text => { captured.Add(text); return "hola"; });
        var subtitles = new List<SubtitleItem>
        {
            new() { Position = 1, StartTime = 1000, EndTime = 2000, Lines = ["Hello"], PlaintextLines = ["Hello"] },
            new() { Position = 2, StartTime = 1000, EndTime = 2000, Lines = ["Hello"], PlaintextLines = ["Hello"] },
            new() { Position = 3, StartTime = 1000, EndTime = 2000, Lines = ["Hello"], PlaintextLines = ["Hello"] },
            new() { Position = 4, StartTime = 1000, EndTime = 2000, Lines = ["Hello"], PlaintextLines = ["Hello"] }
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Single(captured);
        Assert.All(subtitles, s => Assert.Equal(["hola"], s.TranslatedLines));
    }

    [Fact]
    public async Task TranslateSubtitles_SameTextDifferentTimes_TranslatesEachIndependently()
    {
        // Arrange - same plaintext at different timestamps are different scenes; each must be translated
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text => { captured.Add(text); return "si"; });
        var subtitles = new List<SubtitleItem>
        {
            new() { Position = 1, StartTime = 1000, EndTime = 2000, Lines = ["Yes"], PlaintextLines = ["Yes"] },
            new() { Position = 2, StartTime = 5000, EndTime = 6000, Lines = ["Yes"], PlaintextLines = ["Yes"] },
            new() { Position = 3, StartTime = 9000, EndTime = 10000, Lines = ["Yes"], PlaintextLines = ["Yes"] }
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(3, captured.Count);
    }

    [Fact]
    public async Task TranslateSubtitles_NoDuplicates_TranslatesEvery()
    {
        // Arrange - SRT/VTT typical case: every entry unique
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text => { captured.Add(text); return $"tr:{text}"; });
        var subtitles = new List<SubtitleItem>
        {
            new() { Position = 1, StartTime = 1000, EndTime = 2000, Lines = ["Hello"], PlaintextLines = ["Hello"] },
            new() { Position = 2, StartTime = 3000, EndTime = 4000, Lines = ["World"], PlaintextLines = ["World"] },
            new() { Position = 3, StartTime = 5000, EndTime = 6000, Lines = ["How are you?"], PlaintextLines = ["How are you?"] }
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["Hello", "World", "How are you?"], captured);
        Assert.Equal(["tr:Hello"], subtitles[0].TranslatedLines);
        Assert.Equal(["tr:World"], subtitles[1].TranslatedLines);
        Assert.Equal(["tr:How are you?"], subtitles[2].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitles_SameTimestampDifferentText_TranslatesEachIndependently()
    {
        // Arrange - overlapping dual-speaker dialogue: same timestamp, different text — both translated
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text => { captured.Add(text); return $"tr:{text}"; });
        var subtitles = new List<SubtitleItem>
        {
            new() { Position = 1, StartTime = 1000, EndTime = 2000, Lines = ["Run!"], PlaintextLines = ["Run!"] },
            new() { Position = 2, StartTime = 1000, EndTime = 2000, Lines = ["Watch out!"], PlaintextLines = ["Watch out!"] }
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["Run!", "Watch out!"], captured);
    }

    #endregion

    #region Context Tests

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOff_ContextIsSourceTextOnBothSides()
    {
        // Arrange
        var harness = CreatePerLineHarness(text => $"tr:{text}");
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "Hello"),
            Subtitle(2, "How are you"),
            Subtitle(3, "Fine")
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 2,
            contextAfter: 1,
            CancellationToken.None);

        // Assert - identical to the behaviour before the toggle existed
        Assert.Equal(3, harness.Calls.Count);
        Assert.Null(harness.Calls[0].ContextBefore);
        Assert.Equal(["How are you"], harness.Calls[0].ContextAfter);
        Assert.Equal(["Hello"], harness.Calls[1].ContextBefore);
        Assert.Equal(["Fine"], harness.Calls[1].ContextAfter);
        Assert.Equal(["Hello", "How are you"], harness.Calls[2].ContextBefore);
        Assert.Null(harness.Calls[2].ContextAfter);
    }

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOn_PreviouslyTranslatedLinesArePairedSentenceBySentence()
    {
        // Arrange
        var harness = CreatePerLineHarness(text => $"tr:{text}", useTranslatedContext: true);
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "Hello"),
            Subtitle(2, "How are you"),
            Subtitle(3, "Fine")
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 2,
            contextAfter: 0,
            CancellationToken.None);

        // Assert - each earlier line is one JSON object with position, line and translation, in order
        Assert.Equal(3, harness.Calls.Count);
        Assert.Null(harness.Calls[0].ContextBefore);
        Assert.Equal(
            [ContextJson(1, "Hello", "tr:Hello")],
            harness.Calls[1].ContextBefore);
        Assert.Equal(
            [ContextJson(1, "Hello", "tr:Hello"), ContextJson(2, "How are you", "tr:How are you")],
            harness.Calls[2].ContextBefore);
        Assert.Equal("{\"position\":1,\"line\":\"Hello\",\"translation\":\"tr:Hello\"}", harness.Calls[1].ContextBefore![0]);
    }

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOn_ContextAfterIsStructuredWithoutTranslation()
    {
        // Arrange
        var harness = CreatePerLineHarness(text => $"tr:{text}", useTranslatedContext: true);
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "Hello"),
            Subtitle(2, "How are you"),
            Subtitle(3, "Fine")
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 1,
            contextAfter: 2,
            CancellationToken.None);

        // Assert - the lines after have not been translated yet, so they carry position and source only
        Assert.Equal([ContextJson(2, "How are you"), ContextJson(3, "Fine")], harness.Calls[0].ContextAfter);
        Assert.Equal([ContextJson(3, "Fine")], harness.Calls[1].ContextAfter);
        Assert.Null(harness.Calls[2].ContextAfter);
        Assert.Equal("{\"position\":3,\"line\":\"Fine\"}", harness.Calls[1].ContextAfter![0]);
        Assert.Equal([ContextJson(2, "How are you", "tr:How are you")], harness.Calls[2].ContextBefore);
    }

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOn_ResumedLinesArePairedFromPersistedTranslation()
    {
        // Arrange - line 1 was translated in an earlier run and pre-filled the way TranslationJob resumes it
        var harness = CreatePerLineHarness(text => $"tr:{text}", useTranslatedContext: true);
        var resumed = Subtitle(1, "Hello");
        resumed.TranslatedLines = ["Hola"];
        var subtitles = new List<SubtitleItem> { resumed, Subtitle(2, "Fine") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 1,
            contextAfter: 0,
            CancellationToken.None);

        // Assert - the persisted translation is used, the line itself is not re-translated
        Assert.Single(harness.Calls);
        Assert.Equal("Fine", harness.Calls[0].Text);
        Assert.Equal([ContextJson(1, "Hello", "Hola")], harness.Calls[0].ContextBefore);
    }

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOnPreserveLineBreaks_MultiLinePairIsCollapsedToOneLineEachSide()
    {
        // Arrange - with preserveLineBreaks the first subtitle is translated line by line, yielding two TranslatedLines
        var harness = CreatePerLineHarness(text => $"tr:{text}", useTranslatedContext: true);
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "Hello", "world"),
            Subtitle(2, "Bye")
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: true,
            contextBefore: 1,
            contextAfter: 0,
            CancellationToken.None);

        // Assert - both sides of the pair are single lines, so the block structure stays intact
        Assert.Equal(["tr:Hello", "tr:world"], subtitles[0].TranslatedLines);
        var byeCall = Assert.Single(harness.Calls, call => call.Text == "Bye");
        Assert.Equal([ContextJson(1, "Hello world", "tr:Hello tr:world")], byeCall.ContextBefore);
    }

    [Fact]
    public async Task TranslateSubtitles_MergeStackedLinesOff_RepeatedTextIsTranslatedIndependentlyWithContext()
    {
        // Arrange - the /api/translate/content shape: same text on every line and no timing information.
        // With merging opted out every line is its own request and carries its own context.
        var harness = CreatePerLineHarness(text => $"tr:{text}", useTranslatedContext: true, mergeStackedLines: false);
        var subtitles = new List<SubtitleItem>
        {
            ContentLine(1, "Yeah."),
            ContentLine(2, "Yeah."),
            ContentLine(3, "Yeah.")
        };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 1,
            contextAfter: 0,
            CancellationToken.None);

        // Assert - three separate calls, each of the later ones carrying the previous line as context
        Assert.Equal(3, harness.Calls.Count);
        Assert.Null(harness.Calls[0].ContextBefore);
        Assert.Equal([ContextJson(1, "Yeah.", "tr:Yeah.")], harness.Calls[1].ContextBefore);
        Assert.Equal([ContextJson(2, "Yeah.", "tr:Yeah.")], harness.Calls[2].ContextBefore);
    }

    [Fact]
    public async Task TranslateSubtitles_MergeStackedLinesOn_UntimedRepeatedTextIsStillMerged()
    {
        // Arrange - the default keeps the stacked-layer merge, so lines that share (0, 0, text) are reused
        var captured = new List<string>();
        var harness = CreatePerLineHarness(text => { captured.Add(text); return "tr:Yeah."; });
        var subtitles = new List<SubtitleItem> { ContentLine(1, "Yeah."), ContentLine(2, "Yeah.") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Single(captured);
        Assert.All(subtitles, s => Assert.Equal(["tr:Yeah."], s.TranslatedLines));
    }

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOn_EmptyStoredTranslationIsPassedWithoutTranslationField()
    {
        // Arrange - the model answers nothing for the first line; that must not become an example for the next one
        var harness = CreatePerLineHarness(text => text == "Hello" ? "" : $"tr:{text}", useTranslatedContext: true);
        var subtitles = new List<SubtitleItem> { Subtitle(1, "Hello"), Subtitle(2, "Bye") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 1,
            contextAfter: 0,
            CancellationToken.None);

        // Assert - stored as empty, passed on like an untranslated line
        Assert.Equal([""], subtitles[0].TranslatedLines);
        Assert.Equal([ContextJson(1, "Hello")], harness.Calls[1].ContextBefore);
    }

    [Fact]
    public async Task TranslateSubtitles_TranslatedContextOn_EmbeddedNewlineAndNonAsciiStayOnOneReadableLine()
    {
        // Arrange - a /content line carries the whole multi-line subtitle with a literal newline
        var harness = CreatePerLineHarness(text => text == "Hello\nworld" ? "你好\n世界" : "再见", useTranslatedContext: true);
        var subtitles = new List<SubtitleItem> { ContentLine(1, "Hello\nworld"), ContentLine(2, "Bye") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 1,
            contextAfter: 0,
            CancellationToken.None);

        // Assert - the newline is JSON-escaped so the entry stays one physical line, CJK is not \u-escaped
        var entry = Assert.Single(harness.Calls[1].ContextBefore!);
        Assert.Equal("{\"position\":1,\"line\":\"Hello\\nworld\",\"translation\":\"你好\\n世界\"}", entry);
        Assert.DoesNotContain('\n', entry);
    }

    #endregion

    #region ProcessSubtitleBatch Tests

    [Fact]
    public async Task ProcessSubtitleBatch_PreserveOff_JoinsWithSpaceAndReturnsSingleLine()
    {
        // Arrange
        List<BatchSubtitleItem> seen = [];
        var harness = CreateBatchHarness(items =>
        {
            seen = items;
            return items.ToDictionary(i => i.Position, _ => "hola mundo");
        });
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            CancellationToken.None);

        // Assert
        Assert.Equal("hello world", seen[0].Line);
        Assert.Equal(["hola mundo"], subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_PreserveOnMultiLine_JoinsWithNewlineAndSplitsBack()
    {
        // Arrange
        List<BatchSubtitleItem> seen = [];
        var harness = CreateBatchHarness(items =>
        {
            seen = items;
            return items.ToDictionary(i => i.Position, _ => "hola\nmundo");
        });
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: true,
            CancellationToken.None);

        // Assert
        Assert.Equal("hello\nworld", seen[0].Line);
        Assert.Equal(["hola", "mundo"], subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_PreserveOnButLineCountMismatch_CollapsesToSingleLine()
    {
        // Arrange - model returns three '\n'-separated lines but source had two; expect fallback to a merged single line
        var harness = CreateBatchHarness(items =>
            items.ToDictionary(i => i.Position, _ => "hola\nmundo\nextra"));
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: true,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hola mundo extra"], subtitles[0].TranslatedLines);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_PreserveOffStripOnMultiLine_RewrapsLongTranslation()
    {
        // Arrange
        var harness = CreateBatchHarness(items =>
            items.ToDictionary(i => i.Position, _ => "Esta es una traducción bastante larga que sin duda excederá el límite de cuarenta y dos caracteres"));
        var subtitles = new List<SubtitleItem> { Subtitle(1, "line a", "line b") };

        // Act
        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: true,
            preserveLineBreaks: false,
            CancellationToken.None);

        // Assert
        var translated = subtitles[0].TranslatedLines;
        Assert.True(translated.Count > 1, "Expected the long translation to be wrapped into multiple lines");
        Assert.All(translated, line => Assert.True(line.Length <= 42, $"Line exceeded max length: '{line}'"));
    }

    [Fact]
    public async Task ProcessSubtitleBatch_MissingTranslation_FallsBackToOriginalLines()
    {
        // Arrange - a 1-item empty result is terminal and writes the original lines
        var harness = CreateBatchHarness(_ => new Dictionary<int, string>());
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello", "world") };

        // Act
        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hello", "world"], subtitles[0].TranslatedLines);
        Assert.Single(harness.BatchCalls);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_EmptyBatchResult_Throws()
    {
        // Arrange - two or more items with an empty dict is a service failure, not original fallback
        var harness = CreateBatchHarness(_ => new Dictionary<int, string>());
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello"), Subtitle(2, "world") };

        // Act + Assert
        await Assert.ThrowsAsync<TranslationException>(() =>
            harness.Service.ProcessSubtitleBatch(subtitles,
                "en", "es",
                stripSubtitleFormatting: false,
                preserveLineBreaks: false,
                CancellationToken.None));
        Assert.Single(harness.BatchCalls);
        Assert.All(subtitles, subtitle => Assert.Empty(subtitle.TranslatedLines));
    }

    [Fact]
    public async Task ProcessSubtitleBatch_PartialBatch_RetriesMissingHalfAndFillsAll()
    {
        // Arrange - each call returns the head half of the requested list
        var harness = CreateBatchHarness(items =>
            items.Take(Math.Max(1, items.Count / 2))
                .ToDictionary(item => item.Position, item => $"tr:{item.Line}"));
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "one"),
            Subtitle(2, "two"),
            Subtitle(3, "three"),
            Subtitle(4, "four")
        };

        // Act
        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            CancellationToken.None);

        // Assert
        Assert.True(harness.BatchCalls.Count > 1);
        Assert.Equal(4, harness.BatchCalls[0].Count);
        Assert.All(harness.BatchCalls.Skip(1), call => Assert.True(call.Count < harness.BatchCalls[0].Count));
        Assert.Equal(["tr:one"], subtitles[0].TranslatedLines);
        Assert.Equal(["tr:two"], subtitles[1].TranslatedLines);
        Assert.Equal(["tr:three"], subtitles[2].TranslatedLines);
        Assert.Equal(["tr:four"], subtitles[3].TranslatedLines);
    }

    [Fact]
    public async Task ProcessSubtitleBatch_PrimaryZeroHitOnSplit_FallbackFillsRemainder()
    {
        var harness = CreateBatchHarness(
            items => items.Count >= 4
                ? items.Take(1).ToDictionary(item => item.Position, item => $"a:{item.Line}")
                : new Dictionary<int, string>(),
            items => items.ToDictionary(item => item.Position, item => $"b:{item.Line}"));
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "one"),
            Subtitle(2, "two"),
            Subtitle(3, "three"),
            Subtitle(4, "four")
        };

        await harness.Service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            CancellationToken.None);

        Assert.NotEmpty(harness.FallbackCalls);
        Assert.Equal(["a:one"], subtitles[0].TranslatedLines);
        Assert.Equal(["b:two"], subtitles[1].TranslatedLines);
        Assert.Equal(["b:three"], subtitles[2].TranslatedLines);
        Assert.Equal(["b:four"], subtitles[3].TranslatedLines);
    }

    [Fact]
    public async Task TranslateSubtitlesBatch_ServiceFailure_EmitsPartialHits()
    {
        var harness = CreateBatchHarness(items =>
            items.Count >= 4
                ? items.Take(1).ToDictionary(item => item.Position, item => $"tr:{item.Line}")
                : new Dictionary<int, string>());
        var subtitles = new List<SubtitleItem>
        {
            Subtitle(1, "one"),
            Subtitle(2, "two"),
            Subtitle(3, "three"),
            Subtitle(4, "four")
        };

        await Assert.ThrowsAsync<TranslationException>(() =>
            harness.Service.TranslateSubtitlesBatch(
                subtitles,
                NewRequest(),
                stripSubtitleFormatting: false,
                preserveLineBreaks: false,
                batchSize: 0,
                CancellationToken.None));

        Assert.Single(harness.EmittedLines);
        Assert.Equal([1], harness.EmittedLines[0].Select(line => line.Position));
        Assert.Equal("tr:one", harness.EmittedLines[0][0].Target);
        Assert.Equal(["tr:one"], subtitles[0].TranslatedLines);
        Assert.All(subtitles.Skip(1), subtitle => Assert.Empty(subtitle.TranslatedLines));
    }

    [Fact]
    public async Task ProcessSubtitleBatch_NoBatchCapableCandidateForLanguagePair_FallsBackToLineTranslation()
    {
        // Arrange - the chain holds a batch-capable provider, but it does not support the requested
        // pair. The run has to degrade to the per-line chain instead of failing the whole file.
        var pair = new LanguagePair { Source = "en", Target = "es", Tier = MatchTier.Exact };
        var unsupportedBatch = MockService(pair: null, _ => "unused");
        var batchCapability = new Mock<IBatchTranslationService>();
        var lineService = MockService(pair, _ => "hola");
        var progress = MockProgressService();
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("batch-only", unsupportedBatch.Object, batchCapability.Object),
                new TranslationServiceEntry("line-only", lineService.Object, null)
            ],
            NullLogger.Instance,
            progress.Object);
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello") };

        // Act
        await service.ProcessSubtitleBatch(subtitles,
            "en", "es",
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hola"], subtitles[0].TranslatedLines);
        Assert.Equal("line-only", Assert.Single(service.SourceSummary.Counts).Source.Provider);
        batchCapability.Verify(batchService => batchService.TranslateBatchAsync(
            It.IsAny<List<BatchSubtitleItem>>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Chain-wide best-match resolution

    private static Mock<ITranslationService> MockService(LanguagePair? pair, Func<string, string> translate)
    {
        var mock = new Mock<ITranslationService>();
        mock.Setup(service => service.GetLanguagePair(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pair);
        mock.Setup(service => service.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, string _, List<string>? _, List<string>? _, CancellationToken _) => translate(text));
        return mock;
    }

    private static Mock<ITranslationService> MockFailingService(LanguagePair pair, Exception failure)
    {
        var mock = new Mock<ITranslationService>();
        mock.Setup(service => service.GetLanguagePair(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pair);
        mock.Setup(service => service.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);
        return mock;
    }

    /// <summary>
    /// Progress mock complete enough for <see cref="SubtitleTranslationService.TranslateSubtitles"/>.
    /// </summary>
    private static Mock<IProgressService> MockProgressService()
    {
        var progress = new Mock<IProgressService>();
        progress.Setup(progressService => progressService.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>()))
            .Returns(Task.CompletedTask);
        progress.Setup(progressService => progressService.EmitLine(
                It.IsAny<TranslationRequest>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<LanguagePair?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        progress.Setup(progressService => progressService.EmitLines(
                It.IsAny<TranslationRequest>(), It.IsAny<List<TranslatedLineData>>()))
            .Returns(Task.CompletedTask);
        return progress;
    }

    [Fact]
    public async Task TranslateSubtitleLine_PrefersExactMatchOverNeutralFallback()
    {
        // Arrange
        var primary = MockService(new LanguagePair { Source = "en", Target = "zh", Tier = MatchTier.NeutralEquivalent }, _ => "primary");
        var fallback = MockService(new LanguagePair { Source = "en", Target = "zh-TW", Tier = MatchTier.Exact }, _ => "fallback");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("primary", primary.Object, null),
                new TranslationServiceEntry("fallback", fallback.Object, null)
            ],
            NullLogger.Instance);

        // Act
        var (translation, source, pair) = await service.TranslateSubtitleLine(
            new TranslateAbleSubtitleLine { SubtitleLine = "hi", SourceLanguage = "en", TargetLanguage = "zh-TW" },
            CancellationToken.None);

        // Assert
        Assert.Equal("fallback", translation);
        Assert.Equal("fallback", source.Provider);
        Assert.Equal(MatchTier.Exact, pair.Tier);
        Assert.Equal("zh-TW", pair.Target);
    }

    [Fact]
    public async Task TranslateSubtitleLine_SameTierBreaksTiesByChainOrder()
    {
        // Arrange
        var primary = MockService(new LanguagePair { Source = "en", Target = "zh-TW", Tier = MatchTier.Exact }, _ => "primary");
        var fallback = MockService(new LanguagePair { Source = "en", Target = "zh-TW", Tier = MatchTier.Exact }, _ => "fallback");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("primary", primary.Object, null),
                new TranslationServiceEntry("fallback", fallback.Object, null)
            ],
            NullLogger.Instance);

        // Act
        var (translation, source, _) = await service.TranslateSubtitleLine(
            new TranslateAbleSubtitleLine { SubtitleLine = "hi", SourceLanguage = "en", TargetLanguage = "zh-TW" },
            CancellationToken.None);

        // Assert
        Assert.Equal("primary", translation);
        Assert.Equal("primary", source.Provider);
    }

    [Fact]
    public async Task TranslateSubtitleLine_FallbackMatchSurfacesMatchedCodesOnEmittedLine()
    {
        // Arrange
        var only = MockService(new LanguagePair { Source = "en", Target = "zh", Tier = MatchTier.NeutralEquivalent }, _ => "ni hao");
        var progress = new Mock<IProgressService>();
        progress.Setup(progressService => progressService.Emit(It.IsAny<TranslationRequest>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        progress.Setup(progressService => progressService.EmitLine(
                It.IsAny<TranslationRequest>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<LanguagePair?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        var service = new SubtitleTranslationService(
            [new TranslationServiceEntry("only", only.Object, null)],
            NullLogger.Instance,
            progress.Object);
        var request = new TranslationRequest
        {
            Title = "t",
            SourceLanguage = "en",
            TargetLanguage = "zh-TW",
            MediaType = MediaType.Movie,
            Status = TranslationStatus.InProgress
        };

        // Act
        await service.TranslateSubtitles([Subtitle(1, "hi")], request,
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        progress.Verify(progressService => progressService.EmitLine(
            request, 1, "hi", "ni hao", "only",
            It.Is<LanguagePair?>(pair => pair != null && pair.Source == "en" && pair.Target == "zh" && pair.Tier == MatchTier.NeutralEquivalent),
            null),
            Times.Once);
    }

    [Fact]
    public async Task TranslateSubtitleLine_NoServiceSupportsPair_Throws()
    {
        // Arrange
        var none = MockService(pair: null, _ => "unused");
        var service = new SubtitleTranslationService(
            [new TranslationServiceEntry("none", none.Object, null)],
            NullLogger.Instance);

        // Act + Assert
        await Assert.ThrowsAsync<TranslationException>(() =>
            service.TranslateSubtitleLine(
                new TranslateAbleSubtitleLine { SubtitleLine = "hi", SourceLanguage = "en", TargetLanguage = "zh-TW" },
                CancellationToken.None));
    }

    [Fact]
    public async Task TranslateSubtitles_SameProviderTwoModels_SecondModelIsUsedAndReportedAsSource()
    {
        // Arrange - one provider configured with two models: the first model is down, so the chain
        // tries the provider's second candidate and the line is attributed to that second model.
        var pair = new LanguagePair { Source = "en", Target = "es", Tier = MatchTier.Exact };
        var firstModel = MockFailingService(pair, new InvalidOperationException("model-a is down"));
        var secondModel = MockService(pair, _ => "hola");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("custom", firstModel.Object, null, "model-a"),
                new TranslationServiceEntry("custom", secondModel.Object, null, "model-b")
            ],
            NullLogger.Instance,
            MockProgressService().Object);
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello") };

        // Act
        await service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hola"], subtitles[0].TranslatedLines);
        var count = Assert.Single(service.SourceSummary.Counts);
        Assert.Equal("custom", count.Source.Provider);
        Assert.Equal("model-b", count.Source.Model);
        Assert.Equal(1, count.Lines);
    }

    [Fact]
    public async Task TranslateSubtitleLine_CallerCancellation_DoesNotFallBackToNextCandidate()
    {
        var pair = new LanguagePair { Source = "en", Target = "es", Tier = MatchTier.Exact };
        using var cts = new CancellationTokenSource();
        var cancelled = new Mock<ITranslationService>();
        cancelled.Setup(service => service.GetLanguagePair(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pair);
        cancelled.Setup(service => service.TranslateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());
        var next = MockService(pair, _ => "should not run");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("custom", cancelled.Object, null, "model-a"),
                new TranslationServiceEntry("deepl", next.Object, null)
            ],
            NullLogger.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TranslateSubtitleLine(
                new TranslateAbleSubtitleLine { SubtitleLine = "hi", SourceLanguage = "en", TargetLanguage = "es" },
                cts.Token));

        next.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TranslateSubtitleLine_AuthenticationFailure_SkipsRemainingModelsOfThatProvider()
    {
        // Arrange - the first candidate rejects the credentials. Every model of a provider shares
        // them, so the provider's second model must not be tried; the chain continues with openai.
        var pair = new LanguagePair { Source = "en", Target = "es", Tier = MatchTier.Exact };
        var unauthorized = MockFailingService(pair, new TranslationAuthenticationException("bad key"));
        var sameProviderSecondModel = MockService(pair, _ => "unused");
        var nextProvider = MockService(pair, _ => "openai says hi");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("custom", unauthorized.Object, null, "model-a"),
                new TranslationServiceEntry("custom", sameProviderSecondModel.Object, null, "model-b"),
                new TranslationServiceEntry("openai", nextProvider.Object, null)
            ],
            NullLogger.Instance);

        // Act
        var (translation, source, _) = await service.TranslateSubtitleLine(
            new TranslateAbleSubtitleLine { SubtitleLine = "hi", SourceLanguage = "en", TargetLanguage = "es" },
            CancellationToken.None);

        // Assert
        Assert.Equal("openai says hi", translation);
        Assert.Equal("openai", source.Provider);
        unauthorized.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        sameProviderSecondModel.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
        nextProvider.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TranslateSubtitleLine_ConfigurationFailure_SkipsRemainingModelsOfThatProvider()
    {
        // Arrange - the first candidate cannot produce a request at all. That failure affects every
        // model of the provider, so the provider's second model must not be tried; the chain continues
        // with the next provider.
        var pair = new LanguagePair { Source = "en", Target = "es", Tier = MatchTier.Exact };
        var misconfigured = MockFailingService(pair, new TranslationConfigurationException("missing endpoint"));
        var sameProviderSecondModel = MockService(pair, _ => "unused");
        var nextProvider = MockService(pair, _ => "openai says hi");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("custom", misconfigured.Object, null, "model-a"),
                new TranslationServiceEntry("custom", sameProviderSecondModel.Object, null, "model-b"),
                new TranslationServiceEntry("openai", nextProvider.Object, null)
            ],
            NullLogger.Instance);

        // Act
        var (translation, source, _) = await service.TranslateSubtitleLine(
            new TranslateAbleSubtitleLine { SubtitleLine = "hi", SourceLanguage = "en", TargetLanguage = "es" },
            CancellationToken.None);

        // Assert
        Assert.Equal("openai says hi", translation);
        Assert.Equal("openai", source.Provider);
        misconfigured.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
        sameProviderSecondModel.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Never);
        nextProvider.Verify(translationService => translationService.TranslateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<List<string>?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Stacked lines and source attribution

    [Fact]
    public async Task TranslateSubtitles_StackedLinesReuseCachedTranslation_AndKeepTheirSource()
    {
        // Arrange - two .ass layers with the same timing and text: the second is served from the
        // translation cache, but it is still a line that was translated by the same provider.
        var harness = CreatePerLineHarness(_ => "hola");
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello"), Subtitle(2, "hello") };

        // Act
        await harness.Service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        Assert.Equal(["hola"], subtitles[0].TranslatedLines);
        Assert.Equal(["hola"], subtitles[1].TranslatedLines);
        harness.TranslationServiceMock.Verify(
            t => t.TranslateAsync("hello", "en", "es", null, null, It.IsAny<CancellationToken>()),
            Times.Once);

        var count = Assert.Single(harness.Service.SourceSummary.Counts);
        Assert.Equal("test", count.Source.Provider);
        Assert.Equal(2, count.Lines);
        Assert.Equal(2, harness.Service.AttributionByPosition.Count);
    }

    [Fact]
    public async Task TranslateSubtitles_CountsEveryLineOncePerSource()
    {
        // Arrange - a mixed run has to report both sources with their own line counts.
        var pair = new LanguagePair { Source = "en", Target = "es", Tier = MatchTier.Exact };
        var failingService = MockFailingService(pair, new InvalidOperationException("provider is down"));
        var workingService = MockService(pair, _ => "hola");
        var service = new SubtitleTranslationService(
            [
                new TranslationServiceEntry("custom", failingService.Object, null, "model-a"),
                new TranslationServiceEntry("openai", workingService.Object, null)
            ],
            NullLogger.Instance,
            MockProgressService().Object);
        var subtitles = new List<SubtitleItem> { Subtitle(1, "hello"), Subtitle(2, "world") };

        // Act
        await service.TranslateSubtitles(subtitles, NewRequest(),
            stripSubtitleFormatting: false,
            preserveLineBreaks: false,
            contextBefore: 0,
            contextAfter: 0,
            CancellationToken.None);

        // Assert
        var count = Assert.Single(service.SourceSummary.Counts);
        Assert.Equal("openai", count.Source.Provider);
        Assert.Equal(2, count.Lines);
        Assert.False(service.SourceSummary.IsMixed);
    }

    #endregion
}
