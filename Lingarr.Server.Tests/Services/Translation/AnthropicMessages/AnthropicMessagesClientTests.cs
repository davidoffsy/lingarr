using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
using Lingarr.Core;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Services.Translation.AnthropicMessages;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.AnthropicMessages;

/// <summary>
/// HTTP-level contract for the Messages adapter: x-api-key and anthropic-version, required
/// max_tokens, tool-use batches, JSON fallback, and the non-streaming rejection.
/// </summary>
public class AnthropicMessagesClientTests
{
    private const string Endpoint = "http://localhost:8000/v1/messages";
    private const string ValidJson =
        "[{\"position\":1,\"line\":\"Hola\"},{\"position\":2,\"line\":\"Mundo\"}]";

    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock = new();
    private readonly Mock<ILogger> _loggerMock = new();
    private readonly AnthropicMessagesClient _client;
    private readonly string _defaultTemplate;

    public AnthropicMessagesClientTests()
    {
        var templateService = new RequestTemplateService();
        _defaultTemplate =
            templateService.GetDefaultTemplate(SettingKeys.Translation.Custom.MessagesRequestTemplate)!;
        _client = new AnthropicMessagesClient(
            new HttpClient(_httpMessageHandlerMock.Object),
            templateService,
            _loggerMock.Object,
            "Custom");
    }

    [Fact]
    public async Task CompleteAsync_ReturnsText_AndSendsMessagesHeaders()
    {
        var bodies = CaptureRequestBodies(() => TextResponse("Hola"));

        var result = await CompleteAsync();

        Assert.Equal("Hola", result);
        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        Assert.True(document.RootElement.TryGetProperty("system", out _));
        Assert.False(document.RootElement.TryGetProperty("input", out _));
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal(8192, document.RootElement.GetProperty("max_tokens").GetInt32());
        VerifyHeader("x-api-key", "secret", Times.Once());
        VerifyHeader("anthropic-version", "2023-06-01", Times.Once());
        VerifyHeader("User-Agent", $"{LingarrVersion.Name}/{LingarrVersion.Number}", Times.Once());
        VerifyHeaderAbsent("Authorization");
    }

    [Fact]
    public async Task CompleteAsync_MaxTokensSetting_OverridesTemplate()
    {
        var bodies = CaptureRequestBodies(() => TextResponse("Hola"));

        await CompleteAsync(maxTokens: 4096);

        using var document = JsonDocument.Parse(bodies[0]);
        Assert.Equal(4096, document.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("max_output_tokens", out _));
        Assert.False(document.RootElement.TryGetProperty("thinking", out _));
    }

    [Fact]
    public async Task CompleteAsync_OmitsApiKeyHeader_WhenKeyIsMissing()
    {
        SetupResponse(() => TextResponse("Hola"));

        await CompleteAsync(apiKey: null);

        VerifyHeaderAbsent("x-api-key");
        VerifyHeader("anthropic-version", "2023-06-01", Times.Once());
    }

    [Fact]
    public async Task CompleteAsync_BlankVersion_IsConfigurationError()
    {
        var exception = await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => CompleteAsync(version: "  "));

        Assert.Contains("anthropic-version", exception.Message);
        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task CompleteAsync_MissingMaxTokens_IsConfigurationError()
    {
        const string template = "{\"model\":\"{model}\",\"messages\":[{\"role\":\"user\",\"content\":\"{userMessage}\"}]}";

        var exception = await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => CompleteAsync(template: template));

        Assert.Contains("max_tokens", exception.Message);
        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task CompleteAsync_Refusal_IsNotRetriedByTheAdapter()
    {
        SetupResponse(() => TextResponse("No", "refusal"));

        var exception = await Assert.ThrowsAsync<TranslationUnusableResponseException>(() => CompleteAsync());

        Assert.Contains("refused", exception.Message);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteAsync_Truncation_IsUnusable()
    {
        SetupResponse(() => TextResponse("Hol", "max_tokens"));

        var exception = await Assert.ThrowsAsync<TranslationUnusableResponseException>(() => CompleteAsync());

        Assert.Contains("truncated", exception.Message);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteAsync_Unauthorized_IsAuthenticationFailure()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("{\"error\":\"invalid x-api-key\"}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<TranslationAuthenticationException>(() => CompleteAsync());

        Assert.Contains("Unauthorized", exception.Message);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteAsync_StreamingTemplate_IsRejectedBeforeSend()
    {
        const string template =
            "{\"model\":\"{model}\",\"max_tokens\":8192,\"stream\":true,\"messages\":[{\"role\":\"user\",\"content\":\"{userMessage}\"}]}";

        var exception = await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => CompleteAsync(template: template));

        Assert.Contains("\"stream\": true", exception.Message);
        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task CompleteAsync_EventStream_IsUnusable()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("data: {\"type\":\"content_block_delta\"}\n\n", Encoding.UTF8, "text/event-stream")
        });

        await Assert.ThrowsAsync<TranslationUnusableResponseException>(() => CompleteAsync());

        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteAsync_Lenient_ReturnsTruncatedText()
    {
        SetupResponse(() => TextResponse("Hol", "max_tokens"));

        var result = await CompleteAsync(strict: false);

        Assert.Equal("Hol", result);
    }

    [Fact]
    public async Task CompleteBatchAsync_Lenient_MissingTool_DoesNotFallBack()
    {
        SetupResponse(() => TextResponse(ValidJson));

        var exception = await Assert.ThrowsAsync<TranslationException>(() => _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: false, CancellationToken.None));

        Assert.Contains("no tool_use block", exception.Message);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteBatchAsync_ToolUse_SendsToolChoice()
    {
        var bodies = CaptureRequestBodies(() => ToolResponse(ValidPositions()));

        var result = await _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: true, CancellationToken.None);

        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        Assert.Equal("record_translation_batch", document.RootElement.GetProperty("tool_choice").GetProperty("name").GetString());
        Assert.Equal(
            "record_translation_batch",
            document.RootElement.GetProperty("tools")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteBatchAsync_TextJsonWithoutTool_IsParsedWithoutASecondRequest()
    {
        SetupResponse(() => TextResponse(ValidJson));

        var result = await _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: true, CancellationToken.None);

        Assert.Equal("Hola", result[1]);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteBatchAsync_ToolRejected_FallsBackToJsonWithoutTools()
    {
        var bodies = CaptureRequestBodies(
            () => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("{\"error\":\"tools are not supported\"}", Encoding.UTF8, "application/json")
            },
            () => TextResponse(ValidJson));

        var result = await _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: true, CancellationToken.None);

        Assert.Equal(2, bodies.Count);
        Assert.Equal("Mundo", result[2]);
        using var first = JsonDocument.Parse(bodies[0]);
        using var second = JsonDocument.Parse(bodies[1]);
        Assert.True(first.RootElement.TryGetProperty("tools", out _));
        Assert.False(second.RootElement.TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task CompleteBatchAsync_Refusal_DoesNotFallBack()
    {
        SetupResponse(() => TextResponse("No", "refusal"));

        await Assert.ThrowsAsync<TranslationUnusableResponseException>(() => _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: true, CancellationToken.None));

        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteBatchAsync_Unauthorized_DoesNotFallBack()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("{\"error\":\"invalid x-api-key\"}", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<TranslationAuthenticationException>(() => _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: true, CancellationToken.None));

        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task CompleteBatchAsync_BadGateway_DoesNotFallBack()
    {
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadGateway,
            Content = new StringContent("{\"error\":\"bad gateway\"}", Encoding.UTF8, "application/json")
        });

        var exception = await Assert.ThrowsAsync<TranslationResponseException>(() => _client.CompleteBatchAsync(
            Endpoint, "secret", "2023-06-01", _defaultTemplate, Replacements(), 0, strict: true, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        VerifyRequestsSent(1);
    }

    private Task<string> CompleteAsync(
        int maxTokens = 0,
        string? apiKey = "secret",
        string version = "2023-06-01",
        string? template = null,
        bool strict = true) =>
        _client.CompleteAsync(
            Endpoint,
            apiKey,
            version,
            template ?? _defaultTemplate,
            Replacements(),
            maxTokens,
            strict,
            CancellationToken.None);

    private static Dictionary<string, string> Replacements() => new()
    {
        ["model"] = "claude-proxy",
        ["systemPrompt"] = "Translate",
        ["userMessage"] = "Hello"
    };

    private static HttpResponseMessage TextResponse(string text, string stopReason = "end_turn") =>
        JsonResponse(new
        {
            stop_reason = stopReason,
            content = new[] { new { type = "text", text } }
        });

    private static HttpResponseMessage ToolResponse(object translations) =>
        JsonResponse(new
        {
            stop_reason = "tool_use",
            content = new[]
            {
                new
                {
                    type = "tool_use",
                    name = "record_translation_batch",
                    input = new { translations }
                }
            }
        });

    private static object ValidPositions() => new[]
    {
        new { position = 1, line = "Hola" },
        new { position = 2, line = "Mundo" }
    };

    private static HttpResponseMessage JsonResponse(object body) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
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

    private void VerifyRequestsSent(int count)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(count),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    private void VerifyHeader(string name, string expected, Times times)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            times,
            ItExpr.Is<HttpRequestMessage>(request => HeaderEquals(request, name, expected)),
            ItExpr.IsAny<CancellationToken>());
    }

    private void VerifyHeaderAbsent(string name)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(request => !request.Headers.Contains(name)),
            ItExpr.IsAny<CancellationToken>());
    }

    private static bool HeaderEquals(HttpRequestMessage request, string name, string expected)
    {
        if (!request.Headers.TryGetValues(name, out var values))
        {
            return false;
        }

        foreach (var value in values)
        {
            if (value == expected)
            {
                return true;
            }
        }

        return false;
    }
}
