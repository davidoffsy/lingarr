using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Models;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation.Base;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class BaseLanguageServiceTests
{
    private readonly LanguageCodeService _languageCodeService = new();
    private readonly Mock<ISettingService> _settings = new();

    [Theory]
    [InlineData("unknown")]
    [InlineData("und")]
    public async Task GetLanguagePair_AiService_AcceptsUnknownSource(string source)
    {
        var service = CreateAiService();

        var pair = await service.GetLanguagePair(source, "zh-CN", CancellationToken.None);

        Assert.NotNull(pair);
        Assert.Equal(source, pair.Source);
        Assert.Equal("zh-CN", pair.Target);
        Assert.Equal(MatchTier.Exact, pair.Tier);
    }

    [Fact]
    public async Task GetLanguagePair_AiService_RejectsInvalidSource()
    {
        var service = CreateAiService();

        var pair = await service.GetLanguagePair("not-a-language", "zh-CN", CancellationToken.None);

        Assert.Null(pair);
    }

    [Fact]
    public async Task GetLanguagePair_ListedService_RejectsUnknownSource()
    {
        var service = CreateListedService();

        var pair = await service.GetLanguagePair("unknown", "zh-CN", CancellationToken.None);

        Assert.Null(pair);
    }

    [Fact]
    public async Task GetLanguagePair_ListedService_StillMatchesKnownPair()
    {
        var service = CreateListedService();

        var pair = await service.GetLanguagePair("en", "zh-CN", CancellationToken.None);

        Assert.NotNull(pair);
        Assert.Equal("en", pair.Source);
        Assert.Equal("zh-CN", pair.Target);
    }

    [Theory]
    [InlineData("unknown", false, "the source language (detect it automatically; typically English)")]
    [InlineData("und", false, "the source language (detect it automatically; typically English)")]
    [InlineData("unknown", true, "auto")]
    [InlineData("en", false, "English")]
    [InlineData("en", true, "en")]
    public void SetLanguageReplacements_UnknownSource_AsksModelToDetect(
        string source,
        bool useLanguageCodes,
        string expected)
    {
        var service = CreateAiService();

        service.ApplyLanguageReplacements(source, "zh-CN", useLanguageCodes ? "true" : "false");

        Assert.Equal(expected, service.SourceReplacement);
    }

    private TestAiService CreateAiService() =>
        new(_settings.Object, NullLogger.Instance, _languageCodeService);

    private TestListedService CreateListedService() =>
        new(_settings.Object, NullLogger.Instance, _languageCodeService);

    private sealed class TestAiService : BaseLanguageService
    {
        public TestAiService(
            ISettingService settings,
            ILogger logger,
            LanguageCodeService languageCodeService)
            : base(settings, logger, languageCodeService)
        {
        }

        public override string? ModelName => "test-ai";

        public override Task<string> TranslateAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            List<string>? contextLinesBefore,
            List<string>? contextLinesAfter,
            CancellationToken cancellationToken) =>
            Task.FromResult(text);

        public void ApplyLanguageReplacements(string source, string target, string format) =>
            SetLanguageReplacements(source, target, format);

        public string SourceReplacement => _replacements["sourceLanguage"];
    }

    private sealed class TestListedService : BaseLanguageService
    {
        public TestListedService(
            ISettingService settings,
            ILogger logger,
            LanguageCodeService languageCodeService)
            : base(settings, logger, languageCodeService)
        {
        }

        public override string? ModelName => "test-listed";

        public override Task<string> TranslateAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            List<string>? contextLinesBefore,
            List<string>? contextLinesAfter,
            CancellationToken cancellationToken) =>
            Task.FromResult(text);

        protected override Task<List<SourceLanguage>> GetServiceLanguages() =>
            Task.FromResult(new List<SourceLanguage>
            {
                new() { Code = "en", Name = "English", Targets = ["zh-CN"] },
                new() { Code = "zh-CN", Name = "Chinese", Targets = ["en"] }
            });
    }
}
