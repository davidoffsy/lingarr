using System.Collections.Generic;
using Lingarr.Server.Services.Translation.Chain;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.Chain;

public class TranslationChainSerializerTests
{
    [Fact]
    public void Serialize_ModelOverride_WritesProviderAndModel()
    {
        var json = TranslationChainSerializer.Serialize([new TranslationChainEntry("custom", "model-a")]);

        Assert.Equal("[{\"provider\":\"custom\",\"model\":\"model-a\"}]", json);
    }

    [Fact]
    public void Serialize_MissingModelOverride_OmitsModel()
    {
        var json = TranslationChainSerializer.Serialize([new TranslationChainEntry("deepl", null)]);

        Assert.Equal("[{\"provider\":\"deepl\"}]", json);
    }

    [Fact]
    public void Serialize_KeepsConfiguredOrder()
    {
        var json = TranslationChainSerializer.Serialize(
        [
            new TranslationChainEntry("custom", "model-a"),
            new TranslationChainEntry("custom", "model-b"),
            new TranslationChainEntry("openai", null)
        ]);

        Assert.Equal(
            "[{\"provider\":\"custom\",\"model\":\"model-a\"}," +
            "{\"provider\":\"custom\",\"model\":\"model-b\"}," +
            "{\"provider\":\"openai\"}]",
            json);
    }

    [Fact]
    public void Serialize_EmptyChain_WritesEmptyArray()
    {
        var json = TranslationChainSerializer.Serialize([]);

        Assert.Equal("[]", json);
    }

    [Fact]
    public void Serialize_WhitespaceAndBlankModel_IsNormalised()
    {
        var json = TranslationChainSerializer.Serialize([new TranslationChainEntry(" openai ", "   ")]);

        Assert.Equal("[{\"provider\":\"openai\"}]", json);
    }

    [Fact]
    public void Serialize_OutputRoundTripsThroughParser()
    {
        var entries = new List<TranslationChainEntry>
        {
            new("custom", "model-a"),
            new("custom", null),
            new("OpenAI", "model-c")
        };

        var result = TranslationChainParser.Parse(TranslationChainSerializer.Serialize(entries));

        Assert.Empty(result.Issues);
        Assert.Equal(entries, result.Entries);
    }

    [Fact]
    public void Serialize_DropsPropertiesThatAreNotPartOfTheContract()
    {
        var parsed = TranslationChainParser.Parse(
            "[{\"provider\":\"custom\",\"model\":\"model-a\",\"weight\":3,\"api_key\":\"secret\"}]");

        var json = TranslationChainSerializer.Serialize(parsed.Entries);

        Assert.Equal("[{\"provider\":\"custom\",\"model\":\"model-a\"}]", json);
        Assert.DoesNotContain("weight", json);
        Assert.DoesNotContain("api_key", json);
    }
}
