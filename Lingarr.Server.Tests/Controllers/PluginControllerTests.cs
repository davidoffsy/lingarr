using System.Collections.Generic;
using System.Linq;
using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Plugins;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Models.Api;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

/// <summary>
/// Contract tests for the translation chain validation endpoint. The settings UI decides whether a
/// chain may be stored from this response, so it has to apply exactly the runtime rules (unknown
/// providers, duplicate candidates, unsupported model overrides) and has to report a fallback to the
/// default provider as invalid instead of silently rewriting the user's value.
/// </summary>
public class PluginControllerTests
{
    private const string DefaultProvider = "libretranslate";

    [Fact]
    public void ValidateTranslationChain_EmptyValue_IsInvalidAndReportsEmptyChain()
    {
        var controller = CreateController();

        var response = Validate(controller, string.Empty);

        Assert.False(response.IsValid);
        Assert.True(response.UsedDefaultFallback);
        var issue = Assert.Single(response.Issues);
        Assert.Equal(TranslationChainIssueKind.EmptyChain, issue.Kind);
        Assert.Equal(-1, issue.Index);

        // The canonical value is still returned, so a client can see which chain the server would use.
        Assert.Equal(
            DefaultProvider,
            Assert.Single(TranslationChainParser.Parse(response.ServiceType).Entries).ProviderKey);
    }

    [Fact]
    public void ValidateTranslationChain_ValidChain_IsValidAndKeepsOrderAndModels()
    {
        var controller = CreateController();

        var response = Validate(controller,
            "[{\"provider\":\"openai\",\"model\":\"gpt-4o\"},{\"provider\":\"deepl\"}]");

        Assert.True(response.IsValid);
        Assert.False(response.UsedDefaultFallback);
        Assert.Empty(response.Issues);
        Assert.Equal(["openai (gpt-4o)", "deepl"], response.Candidates);
    }

    [Fact]
    public void ValidateTranslationChain_UnknownProviderAndUnsupportedOverride_AreReported()
    {
        var controller = CreateController();

        var response = Validate(controller,
            "[{\"provider\":\"nope\"},{\"provider\":\"deepl\",\"model\":\"anything\"}]");

        Assert.False(response.IsValid);
        Assert.Contains(response.Issues, issue => issue.Kind == TranslationChainIssueKind.UnknownProvider);
        Assert.Contains(response.Issues,
            issue => issue.Kind == TranslationChainIssueKind.ModelOverrideNotSupported);
    }

    [Fact]
    public void ValidateTranslationChain_DuplicateCandidate_IsReported()
    {
        var controller = CreateController();

        var response = Validate(controller, "[{\"provider\":\"openai\"},{\"provider\":\"openai\"}]");

        Assert.False(response.IsValid);
        Assert.Contains(response.Issues,
            issue => issue.Kind == TranslationChainIssueKind.DuplicateProviderModel);
    }

    [Fact]
    public void ValidateTranslationChain_HistoricSingleValue_IsValid()
    {
        var response = Validate(CreateController(), "openai");

        Assert.True(response.IsValid);
        Assert.False(response.UsedDefaultFallback);
        Assert.Empty(response.Issues);
        Assert.Equal(["openai"], response.Candidates);
    }

    [Fact]
    public void ValidateTranslationChain_HistoricStringArray_IsValid()
    {
        var response = Validate(CreateController(), "[\"openai\",\"deepl\"]");

        Assert.True(response.IsValid);
        Assert.False(response.UsedDefaultFallback);
        Assert.Empty(response.Issues);
        Assert.Equal(["openai", "deepl"], response.Candidates);
    }

    [Fact]
    public void ValidateTranslationChain_SameProviderDifferentModels_IsValid()
    {
        var response = Validate(CreateController(),
            "[{\"provider\":\"openai\",\"model\":\"model-a\"},{\"provider\":\"openai\",\"model\":\"model-b\"}]");

        Assert.True(response.IsValid);
        Assert.False(response.UsedDefaultFallback);
        Assert.Empty(response.Issues);
        Assert.Equal(["openai (model-a)", "openai (model-b)"], response.Candidates);
    }

    private static TranslationChainValidationResponse Validate(PluginController controller, string serviceType)
    {
        var result = controller.ValidateTranslationChain(
            new TranslationChainValidationRequest { ServiceType = serviceType });

        // Ok(...) lands in Result; ActionResult<T>.Value stays unset for that conversion.
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TranslationChainValidationResponse>(ok.Value);
    }

    private static PluginController CreateController()
    {
        var registry = new Mock<IPluginRegistry>();
        registry.Setup(pluginRegistry => pluginRegistry.All).Returns(
        [
            new RegisteredPlugin
            {
                Manifest = new TestManifest("openai", supportsModelOverride: true),
                IsBuiltIn = true
            },
            new RegisteredPlugin
            {
                Manifest = new TestManifest("deepl", supportsModelOverride: false),
                IsBuiltIn = true
            },
            new RegisteredPlugin
            {
                // The default provider has to be known, otherwise an empty value would be reported as
                // an unknown provider instead of as an empty chain.
                Manifest = new TestManifest("libretranslate", supportsModelOverride: false),
                IsBuiltIn = true
            }
        ]);

        return new PluginController(
            registry.Object,
            new Mock<ISettingService>().Object,
            new Mock<ITranslationServiceFactory>().Object,
            Mock.Of<ILogger<PluginController>>());
    }

    /// <summary>Minimal manifest double: the endpoint only reads the provider ids and the override flag.</summary>
    private sealed class TestManifest(string provider, bool supportsModelOverride) : IPluginManifest
    {
        public string Provider { get; } = provider;
        public string DisplayName => Provider;
        public string? Description => null;
        public IReadOnlyList<PluginSettingField> Settings => [];
        public bool SupportsModelOverride { get; } = supportsModelOverride;
    }
}
