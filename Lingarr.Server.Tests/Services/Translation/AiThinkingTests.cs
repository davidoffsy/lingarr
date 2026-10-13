using System.Collections.Generic;
using System.Text.Json;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class AiThinkingTests
{
    [Theory]
    [InlineData("off", AiThinkingMode.Off)]
    [InlineData("OFF", AiThinkingMode.Off)]
    [InlineData(" Off ", AiThinkingMode.Off)]
    [InlineData("on", AiThinkingMode.On)]
    [InlineData("ON", AiThinkingMode.On)]
    [InlineData(" on ", AiThinkingMode.On)]
    [InlineData("auto", AiThinkingMode.Auto)]
    [InlineData("AUTO", AiThinkingMode.Auto)]
    [InlineData(" auto ", AiThinkingMode.Auto)]
    [InlineData("", AiThinkingMode.Off)]
    [InlineData("   ", AiThinkingMode.Off)]
    [InlineData(null, AiThinkingMode.Off)]
    [InlineData("garbage", AiThinkingMode.Auto)]
    [InlineData("true", AiThinkingMode.Auto)]
    [InlineData("disabled", AiThinkingMode.Auto)]
    public void Parse_MapsValues(string? value, AiThinkingMode expected)
    {
        Assert.Equal(expected, AiThinking.Parse(value));
    }

    [Fact]
    public void ToRequestFields_Auto_ReturnsNull()
    {
        Assert.Null(AiThinking.ToRequestFields(AiThinkingMode.Auto));
    }

    [Fact]
    public void ToRequestFields_Off_DisablesThinking()
    {
        var fields = AiThinking.ToRequestFields(AiThinkingMode.Off);

        Assert.NotNull(fields);
        Assert.Equal("disabled", ThinkingType(fields));
    }

    [Fact]
    public void ToRequestFields_On_EnablesThinking()
    {
        var fields = AiThinking.ToRequestFields(AiThinkingMode.On);

        Assert.NotNull(fields);
        Assert.Equal("enabled", ThinkingType(fields));
    }

    [Fact]
    public void Apply_Auto_LeavesBodyUnchanged()
    {
        const string body = """{"model":"test"}""";
        var templates = new RequestTemplateService();

        var applied = AiThinking.Apply(templates, body, AiThinkingMode.Auto);

        Assert.Equal(body, applied);
    }

    [Theory]
    [InlineData(AiThinkingMode.Off, "disabled")]
    [InlineData(AiThinkingMode.On, "enabled")]
    public void Apply_InjectsThinkingType(AiThinkingMode mode, string expectedType)
    {
        var templates = new RequestTemplateService();

        var applied = AiThinking.Apply(templates, """{"model":"test"}""", mode);

        using var document = JsonDocument.Parse(applied);
        Assert.Equal(expectedType, document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    private static string? ThinkingType(Dictionary<string, object?> fields)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(fields));
        return document.RootElement.GetProperty("thinking").GetProperty("type").GetString();
    }
}
