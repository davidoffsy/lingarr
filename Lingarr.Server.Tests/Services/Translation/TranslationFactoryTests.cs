using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Lingarr.Contracts.Translation;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plugins.Manifests;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

/// <summary>
/// Tests for the real <see cref="TranslationFactory.CreateTranslationServices"/> (the rest of the suite
/// injects a mock <see cref="ITranslationServiceFactory"/>, so ordering, skipping, model overrides and
/// duplicate handling of the production factory would otherwise be untested).
/// </summary>
public class TranslationFactoryTests
{
    /// <summary>
    /// Built-in manifests as registered in <c>ServiceCollectionExtensions</c>. Kept in sync with the
    /// registration list so the capability test below covers every built-in provider.
    /// </summary>
    private static readonly IReadOnlyList<IPluginManifest> BuiltInManifests =
    [
        new AnthropicPluginManifest(),
        new OpenAiPluginManifest(),
        new GeminiPluginManifest(),
        new DeepSeekPluginManifest(),
        new MistralPluginManifest(),
        new XAiPluginManifest(),
        new LocalAiPluginManifest(),
        new CustomPluginManifest(),
        new DeepLPluginManifest(),
        new LibreTranslatePluginManifest(),
        new GoogleTranslatePluginManifest(),
        new BingTranslatePluginManifest(),
        new MicrosoftTranslatePluginManifest(),
        new YandexTranslatePluginManifest()
    ];

    /// <summary>
    /// Builds a minimal real container for the factory. Only providers that do not need
    /// IHttpClientFactory/HttpClient are exercised here ("deepl", "openai", "custom", "localai");
    /// everything they require is registered below: the real LanguageCodeService, mocked setting/template
    /// services and logging.
    /// </summary>
    private static TranslationFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<LanguageCodeService>();
        services.AddSingleton<ISettingService>(Mock.Of<ISettingService>());
        services.AddSingleton<IRequestTemplateService>(Mock.Of<IRequestTemplateService>());
        services.AddSingleton(new HttpClient());

        var serviceProvider = services.BuildServiceProvider();
        return new TranslationFactory(serviceProvider, NullLogger<TranslationFactory>.Instance);
    }

    private static IReadOnlyList<TranslationChainEntry> Chain(params string[] providers) =>
        providers.Select(provider => new TranslationChainEntry(provider, null)).ToList();

    [Fact]
    public void CreateTranslationServices_TwoKnownProviders_PreservesConfiguredOrder()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(Chain("deepl", "openai"));

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "deepl", "openai" }, entries.Select(entry => entry.Name));
        Assert.IsType<DeepLService>(entries[0].Service);
        Assert.IsType<OpenAiService>(entries[1].Service);
    }

    [Fact]
    public void CreateTranslationServices_UnknownProvider_IsSkippedWithoutAffectingOthers()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(Chain("deepl", "not-a-plugin", "openai"));

        // The unconstructable entry is dropped with a warning; the surviving entries keep the
        // configured order.
        Assert.Equal(new[] { "deepl", "openai" }, entries.Select(entry => entry.Name));
    }

    [Fact]
    public void CreateTranslationServices_SameProviderTwice_IsDeduplicated()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(Chain("deepl", "deepl"));

        // The same (provider, model) is not an extra fallback: the second instance would fail exactly
        // like the first, so it is dropped instead of consuming another round of retries.
        Assert.Equal(new[] { "deepl" }, entries.Select(entry => entry.Name));
    }

    [Fact]
    public void CreateTranslationServices_SameProviderWithDifferentModels_KeepsBothCandidates()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(
        [
            new TranslationChainEntry("openai", "model-a"),
            new TranslationChainEntry("openai", "model-b")
        ]);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { "model-a", "model-b" }, entries.Select(entry => entry.Model));
        Assert.NotSame(entries[0].Service, entries[1].Service);
    }

    [Fact]
    public void CreateTranslationServices_ModelOverride_IsAppliedToTheBuiltService()
    {
        var factory = CreateFactory();

        var entry = Assert.Single(factory.CreateTranslationServices(
            [new TranslationChainEntry("openai", "model-a")]));

        Assert.IsAssignableFrom<IModelOverridable>(entry.Service);
        Assert.Equal("model-a", entry.Model);
    }

    [Fact]
    public void CreateTranslationServices_ModelOverrideOnProviderWithoutSupport_IsSkipped()
    {
        var factory = CreateFactory();

        // DeepL has no model concept and does not implement IModelOverridable, so a candidate asking for
        // one is dropped rather than silently translated with some default.
        var entries = factory.CreateTranslationServices(
        [
            new TranslationChainEntry("deepl", "model-a"),
            new TranslationChainEntry("openai", null)
        ]);

        Assert.Equal(new[] { "openai" }, entries.Select(entry => entry.Name));
    }

    [Fact]
    public void CreateTranslationServices_ModelsAreComparedCaseSensitively()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(
        [
            new TranslationChainEntry("openai", "model-a"),
            new TranslationChainEntry("openai", "Model-A")
        ]);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void CreateTranslationServices_EmptyInput_ReturnsEmptyList()
    {
        var factory = CreateFactory();

        Assert.Empty(factory.CreateTranslationServices([]));
    }

    [Fact]
    public void CreateTranslationServices_AllProvidersUnknown_ReturnsEmptyList()
    {
        var factory = CreateFactory();

        Assert.Empty(factory.CreateTranslationServices(Chain("not-a-plugin", "also-not-a-plugin")));
    }

    [Fact]
    public void CreateTranslationServices_MixedCaseProviderName_IsNormalisedToLowerCase()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices([new TranslationChainEntry("DeepL", null)]);

        var entry = Assert.Single(entries);
        Assert.Equal("deepl", entry.Name);
        Assert.IsType<DeepLService>(entry.Service);
    }

    [Fact]
    public void CreateTranslationServices_CustomProvider_IsConstructedIndependently()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(Chain("custom"));

        var entry = Assert.Single(entries);
        Assert.Equal("custom", entry.Name);
        Assert.IsType<CustomService>(entry.Service);
    }

    [Fact]
    public void CreateTranslationServices_CustomAndLocalAi_DoNotShareIdentity()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices(Chain("custom", "localai"));

        Assert.Equal(new[] { "custom", "localai" }, entries.Select(entry => entry.Name));
        Assert.IsType<CustomService>(entries[0].Service);
        Assert.IsType<LocalAiService>(entries[1].Service);
    }

    [Fact]
    public void CreateTranslationServices_EntryDescribesItsCandidate()
    {
        var factory = CreateFactory();

        var entries = factory.CreateTranslationServices([new TranslationChainEntry("openai", "model-a")]);

        Assert.Equal("openai (model-a)", Assert.Single(entries).Describe());
    }

    [Fact]
    public void SupportsModelOverride_IsDeclaredByExactlyTheProvidersThatHonourIt()
    {
        // Two declarations have to agree: the manifest advertises per-row model pickers in the settings
        // UI, and the service decides whether the factory can apply the override. A provider that only
        // does one of the two either offers a model the factory drops or silently ignores a configured
        // model, so this test keeps them in sync.
        var factory = CreateFactory();

        foreach (var manifest in BuiltInManifests)
        {
            var service = factory.CreateTranslationService(manifest.Provider);
            Assert.Equal(manifest.SupportsModelOverride, service is IModelOverridable);
        }
    }
}
