using System.Collections.Generic;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation.OpenAiCompatible;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.OpenAiCompatible;

/// <summary>
/// Contract tests for the non-streaming Responses payload: only a completed response with text
/// counts as success; refusal, incomplete/failed/non-final states, gateway errors and empty
/// output must never be mistaken for a translation.
/// </summary>
public class OpenAiResponsesPayloadParserTests
{
    private const string ServiceLabel = "Custom";

    [Fact]
    public void ExtractText_CompletedOutputText_ReturnsText()
    {
        var body = Response(
            status: "completed",
            output: """[{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Hola"}]}]""");

        Assert.Equal("Hola", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_MultipleTextBlocks_AreJoined()
    {
        var body = Response(
            status: "completed",
            output:
            """[{"type":"message","content":[{"type":"output_text","text":"Hola"},{"type":"output_text","text":"Mundo"}]}]""");

        Assert.Equal("Hola\nMundo", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_ReasoningItems_AreIgnored()
    {
        var body = Response(
            status: "completed",
            output:
            """[{"type":"reasoning","summary":[{"type":"summary_text","text":"thinking..."}]},{"type":"message","content":[{"type":"output_text","text":"Hola"}]}]""");

        Assert.Equal("Hola", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_ContentWithoutType_IsAccepted()
    {
        var body = Response(
            status: "completed",
            output: """[{"type":"message","content":[{"text":"Hola"}]}]""");

        Assert.Equal("Hola", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_NonMessageOutputItems_AreIgnored()
    {
        // Only message items carry assistant text; tool call items must not leak into the result.
        var body = Response(
            status: "completed",
            output:
            """[{"type":"function_call","name":"translate","content":[{"type":"output_text","text":"not the answer"}]},{"type":"message","content":[{"type":"output_text","text":"Hola"}]}]""");

        Assert.Equal("Hola", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_MissingStatusWithOutput_IsAccepted()
    {
        var body = Response(
            status: null,
            output: """[{"type":"message","content":[{"type":"output_text","text":"Hola"}]}]""");

        Assert.Equal("Hola", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_TopLevelOutputText_IsUsedWhenOutputIsEmpty()
    {
        var body = Response(status: "completed", output: "[]", outputText: "Hola");

        Assert.Equal("Hola", OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_IncompleteStatus_IsNotSuccess()
    {
        var body = Response(
            status: "incomplete",
            incompleteReason: "max_output_tokens",
            output: "[]");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("incomplete", exception.Message);
        Assert.Contains("max_output_tokens", exception.Message);
    }

    [Fact]
    public void ExtractText_FailedStatus_IsNotSuccess()
    {
        var body = Response(status: "failed", output: "[]");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("failed", exception.Message);
    }

    [Fact]
    public void ExtractText_NonFinalStatus_IsNotSuccess()
    {
        var body = Response(status: "in_progress", output: "[]");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("non-final", exception.Message);
    }

    [Fact]
    public void ExtractText_Refusal_IsNotSuccess()
    {
        var body = Response(
            status: "completed",
            output:
            """[{"type":"message","content":[{"type":"refusal","refusal":"I cannot help with that."}]}]""");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("refused", exception.Message);
    }

    [Fact]
    public void ExtractText_ErrorObject_IsReported()
    {
        var body = """
            {"status":null,"error":{"code":"invalid_api_key","message":"Incorrect API key provided."},"output":[]}
            """;

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("Incorrect API key provided.", exception.Message);
        Assert.Contains("invalid_api_key", exception.Message);
    }

    [Fact]
    public void ExtractText_EmptyOutput_IsNotSuccess()
    {
        var body = Response(status: "completed", output: "[]");

        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));

        Assert.Contains("empty", exception.Message);
    }

    [Fact]
    public void ExtractText_WhitespaceOutput_IsNotSuccess()
    {
        var body = Response(
            status: "completed",
            output: """[{"type":"message","content":[{"type":"output_text","text":"   "}]}]""");

        Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText(body, ServiceLabel));
    }

    [Fact]
    public void ExtractText_MalformedJson_IsNotSuccess()
    {
        var exception = Assert.Throws<TranslationUnusableResponseException>(
            () => OpenAiResponsesPayloadParser.ExtractText("not json at all", ServiceLabel));

        Assert.Contains("not valid Responses JSON", exception.Message);
    }

    private static string Response(
        string? status,
        string output = "[]",
        string? incompleteReason = null,
        string? outputText = null)
    {
        var parts = new List<string>();
        parts.Add(status is null ? "\"status\":null" : $"\"status\":\"{status}\"");
        if (incompleteReason is not null)
        {
            parts.Add($"\"incomplete_details\":{{\"reason\":\"{incompleteReason}\"}}");
        }

        parts.Add($"\"output\":{output}");
        if (outputText is not null)
        {
            parts.Add($"\"output_text\":\"{outputText}\"");
        }

        return "{" + string.Join(",", parts) + "}";
    }
}
