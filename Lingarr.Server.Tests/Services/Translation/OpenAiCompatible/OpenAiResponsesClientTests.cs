using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Services.Translation.OpenAiCompatible;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.OpenAiCompatible;

/// <summary>
/// HTTP-level contract tests for the non-streaming Responses adapter: request shape per protocol,
/// structured batch output, JSON fallback, failure classification and the streaming rejection.
/// </summary>
public class OpenAiResponsesClientTests
{
    private const string Endpoint = "http://localhost:8000/v1/responses";
    private const string ValidJson =
        "[{\"position\":1,\"line\":\"Hola\"},{\"position\":2,\"line\":\"Mundo\"}]";

    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock = new();
    private readonly Mock<ILogger> _loggerMock = new();
    private readonly OpenAiResponsesClient _client;
    private readonly string _defaultTemplate;

    public OpenAiResponsesClientTests()
    {
        var templateService = new RequestTemplateService();
        _defaultTemplate =
            templateService.GetDefaultTemplate(SettingKeys.Translation.Custom.ResponsesRequestTemplate)!;
        _client = new OpenAiResponsesClient(
            new HttpClient(_httpMessageHandlerMock.Object),
            templateService,
            _loggerMock.Object,
            "Custom");
    }

    [Fact]
    public async Task CompleteAsync_ReturnsOutputText()
    {
        SetupResponse(() => ResponsesOk("Hola"));

        var result = await CompleteAsync();

        Assert.Equal("Hola", result);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteAsync_InjectsMaxOutputTokens()
    {
        var bodies = CaptureRequestBodies(() => ResponsesOk("Hola"));

        await CompleteAsync(maxTokens: 8192);

        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        Assert.Equal(8192, document.RootElement.GetProperty("max_output_tokens").GetInt32());
        // Responses uses max_output_tokens, never the chat field.
        Assert.False(document.RootElement.TryGetProperty("max_tokens", out _));
    }

    [Fact]
    public async Task CompleteAsync_DoesNotInjectThinking()
    {
        var bodies = CaptureRequestBodies(() => ResponsesOk("Hola"));

        // ai_thinking writes the Chat Completions 'thinking' extension; the Responses payload has no
        // equivalent parameter and an unknown field would be rejected by strict gateways.
        await CompleteAsync(thinking: AiThinkingMode.On);

        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        Assert.False(document.RootElement.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task CompleteAsync_RetryableStatus_ReportsResponseException()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            Content = new StringContent("{\"error\":\"slow down\"}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<TranslationResponseException>(() => CompleteAsync());

        Assert.Contains("TooManyRequests", exception.Message);
        // The retry policy belongs to the caller; the adapter sends exactly one request.
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteAsync_Unauthorized_ReportsAuthenticationFailure()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent(
                "{\"error\":{\"message\":\"Missing API key.\"}}", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<TranslationAuthenticationException>(() => CompleteAsync());
    }

    [Fact]
    public async Task CompleteAsync_EventStreamResponse_IsRejected()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Ho\"}\n\ndata: [DONE]\n\n",
                Encoding.UTF8,
                "text/event-stream")
        });

        var exception = await Assert.ThrowsAsync<TranslationUnusableResponseException>(() => CompleteAsync());

        Assert.Contains("streaming", exception.Message);
    }

    [Fact]
    public async Task CompleteAsync_EventStreamBodyWithJsonContentType_IsRejected()
    {
        // A gateway that announces JSON but answers with SSE must not be parsed as an empty body.
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<TranslationUnusableResponseException>(() => CompleteAsync());

        Assert.Contains("streaming", exception.Message);
    }

    [Fact]
    public async Task CompleteAsync_StreamingTemplate_IsRejectedWithoutRequest()
    {
        const string streamingTemplate =
            "{\"model\":\"{model}\",\"stream\":true,\"input\":[{\"role\":\"user\",\"content\":\"{userMessage}\"}]}";

        var exception = await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => _client.CompleteAsync(
                Endpoint, null, streamingTemplate, Replacements(), AiThinkingMode.Auto, 0, CancellationToken.None));

        Assert.Contains("\"stream\": true", exception.Message);
        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task CompleteBatchAsync_UsesStructuredTextFormat()
    {
        var bodies = CaptureRequestBodies(() => ResponsesOk("{\"translations\":" + ValidJson + "}"));

        var result = await CompleteBatchAsync();

        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        var format = document.RootElement.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("batch_translation_response", format.GetProperty("name").GetString());
        // Responses structured output never reuses the chat response_format field.
        Assert.False(document.RootElement.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task CompleteBatchAsync_MissingPosition_IsNotFabricated()
    {
        SetupResponse(() => ResponsesOk(
            "{\"translations\":[{\"position\":1,\"line\":\"Hola\"}]}"));

        var result = await CompleteBatchAsync();

        // Only the answered position is reported; the caller (batch fill loop) retries the rest.
        Assert.Single(result);
        Assert.True(result.ContainsKey(1));
    }

    [Fact]
    public async Task CompleteBatchAsync_FallsBackToJsonParsing()
    {
        SetupResponseSequence(
            ResponsesOk("not json at all"),
            ResponsesOk(ValidJson));

        var result = await CompleteBatchAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Mundo", result[2]);
        VerifyRequestsSent(2);
    }

    [Fact]
    public async Task CompleteBatchAsync_Unauthorized_IsNotRetriedAsFallback()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent(
                "{\"error\":{\"message\":\"Missing API key.\"}}", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<TranslationAuthenticationException>(CompleteBatchAsync);

        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteBatchAsync_StreamingTemplate_IsRejectedWithoutRequest()
    {
        const string streamingTemplate =
            "{\"model\":\"{model}\",\"stream\":true,\"input\":[{\"role\":\"user\",\"content\":\"{userMessage}\"}]}";

        var exception = await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => _client.CompleteBatchAsync(
                Endpoint, null, streamingTemplate, Replacements(), AiThinkingMode.Auto, 0, CancellationToken.None));

        Assert.Contains("\"stream\": true", exception.Message);
        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task CompleteBatchAsync_EventStream_IsNotRetriedAsFallback()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<TranslationUnusableResponseException>(CompleteBatchAsync);

        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteBatchAsync_Refusal_IsNotRetriedAsFallback()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(
                """{"status":"completed","output":[{"type":"message","content":[{"type":"refusal","refusal":"no"}]}]}""",
                Encoding.UTF8,
                "application/json")
        });

        await Assert.ThrowsAsync<TranslationUnusableResponseException>(CompleteBatchAsync);

        VerifyRequestsSent(1);
    }

    private Task<string> CompleteAsync(AiThinkingMode thinking = AiThinkingMode.Auto, int maxTokens = 0) =>
        _client.CompleteAsync(Endpoint, null, _defaultTemplate, Replacements(), thinking, maxTokens,
            CancellationToken.None);

    private Task<Dictionary<int, string>> CompleteBatchAsync() =>
        _client.CompleteBatchAsync(Endpoint, null, _defaultTemplate, Replacements(), AiThinkingMode.Auto, 0,
            CancellationToken.None);

    private static Dictionary<string, string> Replacements() => new()
    {
        ["model"] = "my-model",
        ["systemPrompt"] = "Translate this",
        ["userMessage"] = "Hello"
    };

    private static HttpResponseMessage ResponsesOk(string text) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                status = "completed",
                output = new[]
                {
                    new
                    {
                        type = "message",
                        content = new[] { new { type = "output_text", text } }
                    }
                }
            }),
            Encoding.UTF8,
            "application/json")
    };

    private void SetupResponse(Func<HttpResponseMessage> responseFactory)
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(() => Task.FromResult(responseFactory()));
    }

    private List<string> CaptureRequestBodies(params Func<HttpResponseMessage>[] responseFactories)
    {
        var captured = new List<string>();
        var index = 0;
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                captured.Add(body);
            })
            .Returns(() =>
            {
                var factory = responseFactories[Math.Min(index, responseFactories.Length - 1)];
                index++;
                return Task.FromResult(factory());
            });
        return captured;
    }

    private void SetupResponseSequence(params HttpResponseMessage[] responses)
    {
        var sequence = _httpMessageHandlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        foreach (var response in responses)
        {
            sequence = sequence.ReturnsAsync(response);
        }
    }

    private void VerifyRequestsSent(int count)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(count),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
