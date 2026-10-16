using System.Collections.Generic;
using Lingarr.Server.Services.Translation.Chain;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Chain;

public class TranslationChainParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_MissingOrBlankValue_ReturnsNoEntriesAndNoIssues(string? raw)
    {
        var result = TranslationChainParser.Parse(raw);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_LegacySingleProvider_ReturnsOneEntry()
    {
        var result = TranslationChainParser.Parse("openai");

        Assert.Equal(new[] { new TranslationChainEntry("openai", null) }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_LegacySingleProvider_TrimsSurroundingWhitespace()
    {
        var result = TranslationChainParser.Parse("  openai\n");

        Assert.Equal(new[] { new TranslationChainEntry("openai", null) }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_LegacySingleProvider_PreservesConfiguredCasing()
    {
        var result = TranslationChainParser.Parse("OpenAI");

        Assert.Equal(new[] { new TranslationChainEntry("OpenAI", null) }, result.Entries);
        Assert.Equal("openai", result.Entries[0].ProviderKey);
    }

    [Fact]
    public void Parse_LegacyArray_KeepsConfiguredOrder()
    {
        var result = TranslationChainParser.Parse("[\"openai\",\"deepl\"]");

        Assert.Equal(
            new[]
            {
                new TranslationChainEntry("openai", null),
                new TranslationChainEntry("deepl", null)
            },
            result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_ObjectArray_ReadsProviderAndModel()
    {
        var result = TranslationChainParser.Parse("[{\"provider\":\"custom\",\"model\":\"model-a\"}]");

        Assert.Equal(new[] { new TranslationChainEntry("custom", "model-a") }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_ObjectEntryWithoutModel_HasNoOverride()
    {
        var result = TranslationChainParser.Parse("[{\"provider\":\"custom\"}]");

        Assert.Equal(new[] { new TranslationChainEntry("custom", null) }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"   \"")]
    public void Parse_ObjectEntryWithUnusableModel_FallsBackToProviderDefault(string modelJson)
    {
        var result = TranslationChainParser.Parse($"[{{\"provider\":\"custom\",\"model\":{modelJson}}}]");

        Assert.Equal(new[] { new TranslationChainEntry("custom", null) }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_ObjectEntry_IgnoresUnsupportedProperties()
    {
        var result = TranslationChainParser.Parse(
            "[{\"provider\":\"custom\",\"model\":\"model-a\",\"weight\":3,\"enabled\":true,\"nested\":{}}]");

        Assert.Equal(new[] { new TranslationChainEntry("custom", "model-a") }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_MixedArray_KeepsConfiguredOrder()
    {
        var result = TranslationChainParser.Parse(
            "[\"openai\",{\"provider\":\"custom\",\"model\":\"model-b\"},\"deepl\"]");

        Assert.Equal(
            new[]
            {
                new TranslationChainEntry("openai", null),
                new TranslationChainEntry("custom", "model-b"),
                new TranslationChainEntry("deepl", null)
            },
            result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_SingleJsonObject_ReturnsOneEntry()
    {
        var result = TranslationChainParser.Parse("{\"provider\":\"custom\",\"model\":\"model-c\"}");

        Assert.Equal(new[] { new TranslationChainEntry("custom", "model-c") }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_JsonStringRoot_ReturnsOneEntry()
    {
        var result = TranslationChainParser.Parse("\"openai\"");

        Assert.Equal(new[] { new TranslationChainEntry("openai", null) }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[ ]")]
    public void Parse_EmptyArray_ReturnsNoEntries(string raw)
    {
        var result = TranslationChainParser.Parse(raw);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_DuplicateEntries_AreKeptForValidation()
    {
        var result = TranslationChainParser.Parse(
            "[{\"provider\":\"custom\",\"model\":\"model-a\"},{\"provider\":\"custom\",\"model\":\"model-a\"}]");

        Assert.Equal(2, result.Entries.Count);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_EmptyProviderElement_ReportsIssueAndKeepsRemainingEntries()
    {
        var result = TranslationChainParser.Parse("[\"openai\",\"\"]");

        Assert.Equal(new[] { new TranslationChainEntry("openai", null) }, result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.EmptyProvider, issue.Kind);
        Assert.Equal(1, issue.Index);
    }

    [Fact]
    public void Parse_BlankProviderElement_ReportsIssueWithoutDroppingWholeChain()
    {
        var result = TranslationChainParser.Parse("[\"   \",\"deepl\"]");

        Assert.Equal(new[] { new TranslationChainEntry("deepl", null) }, result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.EmptyProvider, issue.Kind);
        Assert.Equal(0, issue.Index);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("[\"windows\"]")]
    public void Parse_NonStringElement_ReportsInvalidEntry(string element)
    {
        var result = TranslationChainParser.Parse($"[\"openai\",{element}]");

        Assert.Equal(new[] { new TranslationChainEntry("openai", null) }, result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.InvalidEntry, issue.Kind);
        Assert.Equal(1, issue.Index);
    }

    [Fact]
    public void Parse_ObjectWithoutProvider_ReportsInvalidEntry()
    {
        var result = TranslationChainParser.Parse("[{\"model\":\"model-a\"}]");

        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.InvalidEntry, issue.Kind);
        Assert.Equal(0, issue.Index);
    }

    [Fact]
    public void Parse_ObjectWithNonStringProvider_ReportsInvalidEntry()
    {
        var result = TranslationChainParser.Parse("[{\"provider\":5}]");

        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.InvalidEntry, issue.Kind);
        Assert.Equal(0, issue.Index);
    }

    [Fact]
    public void Parse_ObjectWithNonStringModel_ReportsInvalidModelButKeepsEntry()
    {
        var result = TranslationChainParser.Parse("[{\"provider\":\"custom\",\"model\":5}]");

        Assert.Equal(new[] { new TranslationChainEntry("custom", null) }, result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.InvalidModel, issue.Kind);
        Assert.Equal(0, issue.Index);
    }

    [Theory]
    [InlineData("[\"openai\"")]
    [InlineData("[\"openai\"] trailing")]
    [InlineData("[{\"provider\":\"openai\",}]")]
    public void Parse_MalformedJson_ReportsIssueInsteadOfDefaulting(string raw)
    {
        var result = TranslationChainParser.Parse(raw);

        // A broken value must not be turned into a silent default provider; the caller decides.
        Assert.Empty(result.Entries);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.MalformedJson, issue.Kind);
        Assert.Equal(-1, issue.Index);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    public void Parse_BareToken_IsTreatedAsLegacyProviderName(string raw)
    {
        // Anything that is not JSON-shaped is the historical single-value form, so the token is
        // passed through as a provider id; the validator rejects it as an unknown provider.
        var result = TranslationChainParser.Parse(raw);

        Assert.Equal(new[] { new TranslationChainEntry(raw, null) }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_TrimsProviderAndModelOfObjectEntries()
    {
        var result = TranslationChainParser.Parse("[{\"provider\":\" custom \",\"model\":\" model-a \"}]");

        Assert.Equal(new[] { new TranslationChainEntry("custom", "model-a") }, result.Entries);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Parse_ReturnsEntriesInConfiguredOrder()
    {
        var result = TranslationChainParser.Parse(
            "[\"deepl\",\"openai\",{\"provider\":\"custom\",\"model\":\"model-a\"}]");

        Assert.Equal(
            new[]
            {
                new TranslationChainEntry("deepl", null),
                new TranslationChainEntry("openai", null),
                new TranslationChainEntry("custom", "model-a")
            },
            result.Entries);
    }
}
