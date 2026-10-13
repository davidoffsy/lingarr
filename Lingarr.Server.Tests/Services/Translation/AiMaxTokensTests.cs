using System.Text.Json;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class AiMaxTokensTests
{
    [Theory]
    [InlineData("8192", 8192)]
    [InlineData(" 8192 ", 8192)]
    [InlineData("0", 0)]
    [InlineData("", 8192)]
    [InlineData("   ", 8192)]
    [InlineData(null, 8192)]
    [InlineData("-1", 0)]
    [InlineData("garbage", 0)]
    [InlineData("1.5", 0)]
    public void Parse_MapsValues(string? value, int expected)
    {
        Assert.Equal(expected, AiMaxTokens.Parse(value));
    }

    [Fact]
    public void ApplyChat_Positive_AddsMaxTokens()
    {
        var templates = new RequestTemplateService();

        var applied = AiMaxTokens.ApplyChat(templates, """{"model":"test"}""", 8192);

        using var document = JsonDocument.Parse(applied);
        Assert.Equal(8192, document.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public void ApplyOpenAi_Positive_AddsMaxCompletionTokensOnly()
    {
        var templates = new RequestTemplateService();

        var applied = AiMaxTokens.ApplyOpenAi(templates, """{"model":"test"}""", 8192);

        using var document = JsonDocument.Parse(applied);
        Assert.Equal(8192, document.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public void ApplyOpenAiCompatible_Positive_AddsBothTokenFields()
    {
        var templates = new RequestTemplateService();

        var applied = AiMaxTokens.ApplyOpenAiCompatible(templates, """{"model":"test"}""", 8192);

        using var document = JsonDocument.Parse(applied);
        Assert.Equal(8192, document.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(8192, document.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ApplyChat_NonPositive_LeavesBodyUnchanged(int maxTokens)
    {
        const string body = """{"model":"test"}""";
        var templates = new RequestTemplateService();

        var applied = AiMaxTokens.ApplyChat(templates, body, maxTokens);

        Assert.Equal(body, applied);
        using var document = JsonDocument.Parse(applied);
        Assert.False(document.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public void ApplyGemini_Positive_AddsMaxOutputTokensWithoutWipingSiblings()
    {
        const string body = """{"generationConfig":{"response_mime_type":"application/json","temperature":0.2}}""";
        var templates = new RequestTemplateService();

        var applied = AiMaxTokens.ApplyGemini(templates, body, 8192);

        using var document = JsonDocument.Parse(applied);
        var generationConfig = document.RootElement.GetProperty("generationConfig");
        Assert.Equal(8192, generationConfig.GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("application/json", generationConfig.GetProperty("response_mime_type").GetString());
        Assert.Equal(0.2, generationConfig.GetProperty("temperature").GetDouble());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ApplyGemini_NonPositive_LeavesBodyUnchanged(int maxTokens)
    {
        const string body = """{"generationConfig":{"response_mime_type":"application/json"}}""";
        var templates = new RequestTemplateService();

        var applied = AiMaxTokens.ApplyGemini(templates, body, maxTokens);

        Assert.Equal(body, applied);
        using var document = JsonDocument.Parse(applied);
        Assert.False(document.RootElement.GetProperty("generationConfig").TryGetProperty("maxOutputTokens", out _));
    }
}
