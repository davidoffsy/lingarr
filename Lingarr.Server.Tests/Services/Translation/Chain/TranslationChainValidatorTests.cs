using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Services.Translation.Chain;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Chain;

public class TranslationChainValidatorTests
{
    private static readonly IReadOnlyCollection<string> KnownProviders =
        ["deepl", "openai", "custom", "my-plugin"];

    [Fact]
    public void Validate_EmptyChain_ReportsEmptyChain()
    {
        var issues = TranslationChainValidator.Validate([], KnownProviders);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.EmptyChain, issue.Kind);
        Assert.Equal(-1, issue.Index);
    }

    [Fact]
    public void Validate_SupportedChain_ReturnsNoIssues()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("custom", "model-a"),
                new TranslationChainEntry("custom", "model-b"),
                new TranslationChainEntry("openai", null),
                new TranslationChainEntry("deepl", null)
            ],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_ProviderLookup_IsCaseInsensitive()
    {
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("OpenAI", null)],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_ThirdPartyPluginProvider_IsAcceptedWhenDeclared()
    {
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("my-plugin", null)],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_UnknownProvider_ReportsUnknownProviderWithoutDroppingOtherEntries()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("openai", null),
                new TranslationChainEntry("not-installed", null),
                new TranslationChainEntry("deepl", null)
            ],
            KnownProviders);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.UnknownProvider, issue.Kind);
        Assert.Equal(1, issue.Index);
        Assert.Equal("not-installed", issue.Value);
    }

    [Fact]
    public void Validate_DuplicateProviderAndModel_ReportsDuplicate()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("custom", "model-a"),
                new TranslationChainEntry("custom", "model-a")
            ],
            KnownProviders);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.DuplicateProviderModel, issue.Kind);
        Assert.Equal(1, issue.Index);
        Assert.Equal("model-a", issue.Value);
    }

    [Fact]
    public void Validate_DuplicateProvider_IsDetectedCaseInsensitively()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("OpenAI", "model-c"),
                new TranslationChainEntry("openai", "model-c")
            ],
            KnownProviders);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.DuplicateProviderModel, issue.Kind);
        Assert.Equal(1, issue.Index);
    }

    [Fact]
    public void Validate_SameProviderWithDifferentModels_ReturnsNoIssues()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("custom", "model-a"),
                new TranslationChainEntry("custom", "model-b")
            ],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_SameProviderWithAndWithoutModelOverride_ReturnsNoIssues()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("custom", "model-a"),
                new TranslationChainEntry("custom", null)
            ],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_DuplicateProviderWithoutModel_IsDetected()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("deepl", null),
                new TranslationChainEntry("deepl", "  ")
            ],
            KnownProviders);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.DuplicateProviderModel, issue.Kind);
        Assert.Equal(1, issue.Index);
    }

    [Fact]
    public void Validate_ModelComparison_IsCaseSensitive()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("custom", "model-a"),
                new TranslationChainEntry("custom", "Model-A")
            ],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_EntryWithoutProvider_ReportsEmptyProvider()
    {
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("  ", null)],
            KnownProviders);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.EmptyProvider, issue.Kind);
        Assert.Equal(0, issue.Index);
    }

    [Fact]
    public void Validate_BlankModel_IsTreatedAsProviderDefault()
    {
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("deepl", "   ")],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_ReportsEachBrokenEntryOnce()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("not-installed", null),
                new TranslationChainEntry("not-installed", "model-a"),
                new TranslationChainEntry("custom", "model-a"),
                new TranslationChainEntry("Custom", "model-a")
            ],
            KnownProviders);

        Assert.Equal(3, issues.Count);
        Assert.Equal(
            [
                TranslationChainIssueKind.UnknownProvider,
                TranslationChainIssueKind.UnknownProvider,
                TranslationChainIssueKind.DuplicateProviderModel
            ],
            issues.Select(issue => issue.Kind));
        Assert.Equal([0, 1, 3], issues.Select(issue => issue.Index));
    }

    [Fact]
    public void Validate_ModelOverrideWithoutProviderSupport_IsReported()
    {
        var issues = TranslationChainValidator.Validate(
            [
                new TranslationChainEntry("deepl", "model-a"),
                new TranslationChainEntry("openai", "model-b")
            ],
            KnownProviders,
            ["openai"]);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.ModelOverrideNotSupported, issue.Kind);
        Assert.Equal(0, issue.Index);
        Assert.Equal("model-a", issue.Value);
    }

    [Fact]
    public void Validate_ModelOverrideSupportLookup_IsCaseInsensitive()
    {
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("OpenAI", "model-a")],
            KnownProviders,
            ["openai"]);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_WithoutOverrideCapabilities_DoesNotReportOverrides()
    {
        // Callers that do not know which providers support overrides (for example a plugin registry
        // that has not loaded yet) keep the previous behaviour instead of flagging every model.
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("deepl", "model-a")],
            KnownProviders);

        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_OverrideOnUnknownProvider_IsNotDoubleReported()
    {
        var issues = TranslationChainValidator.Validate(
            [new TranslationChainEntry("not-installed", "model-a")],
            KnownProviders,
            ["openai"]);

        var issue = Assert.Single(issues);
        Assert.Equal(TranslationChainIssueKind.UnknownProvider, issue.Kind);
    }
}
