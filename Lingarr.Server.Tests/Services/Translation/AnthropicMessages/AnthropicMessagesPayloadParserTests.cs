using Lingarr.Contracts.Exceptions;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation.AnthropicMessages;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.AnthropicMessages;

/// <summary>
/// Contract tests for a non-streaming Messages payload. Strict mode (Custom) rejects refusals,
/// truncations and empty text. Lenient mode (official Anthropic) still returns partial text.
/// </summary>
public class AnthropicMessagesPayloadParserTests
{
    private const string ServiceLabel = "Custom";

    [Fact]
    public void ExtractText_TextBlock_ReturnsText()
    {
        var body = Message("end_turn", """[{"type":"text","text":"Hola"}]""");

        Assert.Equal("Hola", AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_ThinkingBlock_IsIgnored()
    {
        var body = Message(
            "end_turn",
            """[{"type":"thinking","thinking":"draft"},{"type":"text","text":"Hola"}]""");

        Assert.Equal("Hola", AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_MultipleTextBlocks_AreConcatenated()
    {
        var body = Message(
            "end_turn",
            """[{"type":"text","text":"Hola"},{"type":"text","text":"Mundo"}]""");

        Assert.Equal("HolaMundo", AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_ToolUseWithoutText_IsEmpty()
    {
        var body = Message(
            "tool_use",
            """[{"type":"tool_use","name":"record_translation_batch","input":{"translations":[]}}]""");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("empty response", exception.Message);
    }

    [Fact]
    public void ExtractText_MissingStopReason_IsAccepted()
    {
        const string body = """{"content":[{"type":"text","text":"Hola"}]}""";

        Assert.Equal("Hola", AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_StopSequence_IsAccepted()
    {
        var body = Message("stop_sequence", """[{"type":"text","text":"Hola"}]""");

        Assert.Equal("Hola", AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_RefusalStopReason_IsTerminal()
    {
        var body = Message("refusal", """[{"type":"text","text":"I will not translate that."}]""");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("refused", exception.Message);
    }

    [Fact]
    public void ExtractText_RefusalBlock_IsTerminal()
    {
        var body = Message("end_turn", """[{"type":"refusal"}]""");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("refused", exception.Message);
    }

    [Fact]
    public void ExtractText_MaxTokens_IsTruncation()
    {
        var body = Message("max_tokens", """[{"type":"text","text":"Hol"}]""");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("truncated", exception.Message);
        Assert.Contains("max_tokens", exception.Message);
    }

    [Fact]
    public void ExtractText_NoContent_IsEmpty()
    {
        var body = Message("end_turn", "[]");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("no content", exception.Message);
    }

    [Fact]
    public void ExtractText_WhitespaceText_IsEmpty()
    {
        var body = Message("end_turn", """[{"type":"text","text":"  "}]""");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("empty response", exception.Message);
    }

    [Fact]
    public void ExtractText_InvalidJson_IsUnusable()
    {
        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractText("not-json", ServiceLabel));

        Assert.Contains("not valid Messages JSON", exception.Message);
    }

    [Fact]
    public void ExtractTextLenient_MaxTokens_ReturnsPartialText()
    {
        var body = Message("max_tokens", """[{"type":"text","text":"Hol"}]""");

        Assert.Equal("Hol", AnthropicMessagesPayloadParser.ExtractTextLenient(body, "Anthropic"));
    }

    [Fact]
    public void ExtractTextLenient_Refusal_ReturnsText()
    {
        var body = Message("refusal", """[{"type":"text","text":"No"}]""");

        Assert.Equal("No", AnthropicMessagesPayloadParser.ExtractTextLenient(body, "Anthropic"));
    }

    [Fact]
    public void ExtractTextLenient_NoContent_MatchesHistoricalMessage()
    {
        var body = Message("end_turn", "[]");

        var exception = Assert.Throws<TranslationException>(
            () => AnthropicMessagesPayloadParser.ExtractTextLenient(body, "Anthropic"));

        Assert.Equal("Anthropic returned no content (stop_reason: end_turn).", exception.Message);
    }

    [Fact]
    public void ExtractToolTranslations_ReadsPositionMap()
    {
        var body = Message(
            "tool_use",
            """[{"type":"tool_use","name":"record_translation_batch","input":{"translations":[{"position":1,"line":"Hola"},{"position":2,"line":"Mundo"}]}}]""");

        var result = AnthropicMessagesPayloadParser.ExtractToolTranslations(body, ServiceLabel, strict: true);

        Assert.Equal(2, result.Count);
        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
    }

    [Fact]
    public void ExtractToolTranslations_Refusal_IsTerminalWhenStrict()
    {
        var body = Message("refusal", """[{"type":"text","text":"No"}]""");

        Assert.Throws<TranslationUnusableResponseException>(
            () => AnthropicMessagesPayloadParser.ExtractToolTranslations(body, ServiceLabel, strict: true));
    }

    [Fact]
    public void ExtractToolTranslations_MissingTool_IsShapeFailure()
    {
        var body = Message("end_turn", """[{"type":"text","text":"Hola"}]""");

        var exception = Assert.Throws<TranslationException>(
            () => AnthropicMessagesPayloadParser.ExtractToolTranslations(body, ServiceLabel, strict: true));

        Assert.Contains("no tool_use block", exception.Message);
    }

    private static string Message(string stopReason, string content) =>
        $$"""{"stop_reason":"{{stopReason}}","content":{{content}}}""";
}
