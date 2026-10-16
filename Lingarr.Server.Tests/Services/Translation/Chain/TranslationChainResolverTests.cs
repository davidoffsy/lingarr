using System.Collections.Generic;
using System.Linq;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Chain;

/// <summary>
/// Contract tests for <see cref="TranslationChainResolver"/>: the read entry point every call site now
/// uses. The resolver owns the compatibility policy on top of <see cref="TranslationChainParser"/>:
/// an unusable value falls back to the default provider (and says so), while a readable value keeps
/// its usable candidates even when individual entries are broken.
/// </summary>
public class TranslationChainResolverTests
{
    private const string DefaultProvider = "libretranslate";

    private static IReadOnlyList<TranslationChainEntry> Resolve(string? raw) =>
        TranslationChainResolver.Resolve(raw, NullLogger.Instance).Entries;

    [Fact]
    public void Resolve_Null_UsesDefaultProviderAndReportsEmptyChain()
    {
        var result = TranslationChainResolver.Resolve(null);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DefaultProvider, entry.ProviderKey);
        Assert.False(entry.HasModelOverride);
        Assert.True(result.UsedDefaultFallback);

        // An empty value still translates through the default provider, but it is reported so the
        // settings validation can tell the user that their chain is not the one being used.
        var issue = Assert.Single(result.Issues);
        Assert.Equal(TranslationChainIssueKind.EmptyChain, issue.Kind);
        Assert.Equal(-1, issue.Index);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyValue_UsesDefaultProvider(string raw)
    {
        var result = TranslationChainResolver.Resolve(raw);

        Assert.Equal(DefaultProvider, Assert.Single(result.Entries).ProviderKey);
        Assert.True(result.UsedDefaultFallback);
        Assert.Equal(TranslationChainIssueKind.EmptyChain, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public void Resolve_UnbracketedSingleValue_IsReadAsSingleProvider()
    {
        var result = TranslationChainResolver.Resolve("openai");

        Assert.Equal("openai", Assert.Single(result.Entries).ProviderKey);
        Assert.False(result.UsedDefaultFallback);
    }

    [Fact]
    public void Resolve_UnbracketedValueWithLeadingWhitespace_IsTrimmed()
    {
        // The legacy parser passed "  openai" through verbatim; the chain parser trims before deciding
        // whether the value is JSON, so the provider id is clean.
        Assert.Equal("openai", Assert.Single(Resolve("  openai")).ProviderKey);
    }

    [Fact]
    public void Resolve_JsonStringArray_PreservesConfiguredOrder()
    {
        var entries = Resolve("[\"openai\",\"deepl\"]");

        Assert.Equal(new[] { "openai", "deepl" }, entries.Select(entry => entry.ProviderKey));
        Assert.All(entries, entry => Assert.False(entry.HasModelOverride));
    }

    [Fact]
    public void Resolve_ObjectArray_ReadsProviderAndModel()
    {
        var entries = Resolve("[{\"provider\":\"custom\",\"model\":\"model-a\"},{\"provider\":\"openai\"}]");

        Assert.Equal(2, entries.Count);
        Assert.Equal("custom", entries[0].ProviderKey);
        Assert.Equal("model-a", entries[0].ModelKey);
        Assert.True(entries[0].HasModelOverride);
        Assert.Equal("openai", entries[1].ProviderKey);
        Assert.False(entries[1].HasModelOverride);
    }

    [Fact]
    public void Resolve_MixedArray_ReadsBothElementShapes()
    {
        var entries = Resolve("[\"openai\",{\"provider\":\"custom\",\"model\":\"model-b\"}]");

        Assert.Equal(new[] { "openai", "custom" }, entries.Select(entry => entry.ProviderKey));
        Assert.Equal("model-b", entries[1].ModelKey);
    }

    [Fact]
    public void Resolve_MalformedJson_FallsBackToDefaultWithIssue()
    {
        var result = TranslationChainResolver.Resolve("[\"openai\"", NullLogger.Instance);

        Assert.Equal(DefaultProvider, Assert.Single(result.Entries).ProviderKey);
        Assert.True(result.UsedDefaultFallback);
        Assert.Contains(result.Issues, issue => issue.Kind == TranslationChainIssueKind.MalformedJson);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[ ]")]
    [InlineData("[1,2]")]
    public void Resolve_ValueWithoutUsableEntry_FallsBackToDefault(string raw)
    {
        var result = TranslationChainResolver.Resolve(raw, NullLogger.Instance);

        Assert.Equal(DefaultProvider, Assert.Single(result.Entries).ProviderKey);
        Assert.True(result.UsedDefaultFallback);
    }

    [Fact]
    public void Resolve_OneBadEntryAmongGoodOnes_KeepsTheUsableChain()
    {
        // The central compatibility promise: a single broken entry must never rewrite a legitimate
        // chain to the default provider.
        var result = TranslationChainResolver.Resolve("[\"openai\",1,\"deepl\"]", NullLogger.Instance);

        Assert.Equal(new[] { "openai", "deepl" }, result.Entries.Select(entry => entry.ProviderKey));
        Assert.False(result.UsedDefaultFallback);
        Assert.Contains(result.Issues, issue => issue.Kind == TranslationChainIssueKind.InvalidEntry);
    }

    [Fact]
    public void Resolve_UnknownProvider_IsReadAndLeftToValidation()
    {
        var result = TranslationChainResolver.Resolve("[\"not-a-known-provider\"]", NullLogger.Instance);

        Assert.Equal("not-a-known-provider", Assert.Single(result.Entries).ProviderKey);
        Assert.False(result.UsedDefaultFallback);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Resolve_SingleObjectOrQuotedString_IsAOneEntryChain()
    {
        Assert.Equal("openai", Assert.Single(Resolve("{\"provider\":\"openai\"}")).ProviderKey);
        Assert.Equal("openai", Assert.Single(Resolve("\"openai\"")).ProviderKey);
    }

    [Fact]
    public void Resolve_WhitespaceModel_IsTreatedAsNoOverride()
    {
        var entry = Assert.Single(Resolve("[{\"provider\":\"openai\",\"model\":\"  \"}]"));

        Assert.False(entry.HasModelOverride);
    }

    [Fact]
    public void Normalise_Null_WritesDefaultChainAsObjectArray()
    {
        Assert.Equal($"[{{\"provider\":\"{DefaultProvider}\"}}]", TranslationChainResolver.Normalise(null));
    }

    [Fact]
    public void Normalise_LegacySingleValue_IsPromotedToTheObjectForm()
    {
        Assert.Equal("[{\"provider\":\"openai\"}]", TranslationChainResolver.Normalise("openai"));
    }

    [Fact]
    public void Normalise_ObjectArray_IsIdempotent()
    {
        const string raw = "[{\"provider\":\"custom\",\"model\":\"model-a\"},{\"provider\":\"openai\"}]";

        Assert.Equal(raw, TranslationChainResolver.Normalise(raw));
        Assert.Equal(raw, TranslationChainResolver.Normalise(TranslationChainResolver.Normalise(raw)));
    }

    [Fact]
    public void Normalise_ModelIsOmittedInsteadOfWrittenAsNull()
    {
        Assert.Equal("[{\"provider\":\"openai\"}]", TranslationChainResolver.Normalise("[\"openai\"]"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("openai")]
    [InlineData("  openai")]
    [InlineData("[\"openai\",\"deepl\"]")]
    [InlineData("[]")]
    [InlineData("[ ]")]
    [InlineData("[\"openai\"")]
    [InlineData("[1,2]")]
    [InlineData("[{\"provider\":\"openai\"}]")]
    [InlineData("not-a-known-provider")]
    public void Resolve_EveryInput_YieldsAtLeastOneCandidate(string? raw)
    {
        // Hard contract kept from the legacy parser: every call site needs a usable candidate, so the
        // resolver always substitutes the default provider instead of returning an empty chain.
        Assert.NotEmpty(TranslationChainResolver.Resolve(raw, NullLogger.Instance).Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openai")]
    [InlineData("[\"openai\",\"deepl\"]")]
    [InlineData("[{\"provider\":\"custom\",\"model\":\"model-a\"}]")]
    [InlineData("[\"openai\"")]
    public void Normalise_EveryInput_IsReadableAgain(string? raw)
    {
        // Normalised values (startup promotion, environment variables) must round-trip through the
        // reader without changing which candidates the chain describes.
        var original = TranslationChainResolver.Resolve(raw, NullLogger.Instance).Entries;
        var normalised = TranslationChainResolver.Normalise(raw, NullLogger.Instance);

        Assert.Equal(
            original.Select(entry => entry.Describe()),
            TranslationChainResolver.Resolve(normalised, NullLogger.Instance).Entries.Select(entry => entry.Describe()));
    }
}
