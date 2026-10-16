using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
using Lingarr.Contracts.Models.Batch;
using Lingarr.Core.Configuration;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class CustomServiceTests
{
    private const string BaseEndpoint = "http://localhost:8000/v1";
    private const string ChatEndpoint = "http://localhost:8000/v1/chat/completions";

    /// <summary>
    /// A response where the model left an unescaped quote inside a translated line.
    /// </summary>
    private const string MalformedJson =
        "[{\"position\":1,\"line\":\"Hola\"},{\"position\":2,\"line\":\"Él dijo \"adiós\" y se fue\"}]";

    private const string ValidJson =
        "[{\"position\":1,\"line\":\"Hola\"},{\"position\":2,\"line\":\"Mundo\"}]";

    private readonly Mock<ISettingService> _settingsMock;
    private readonly Mock<ILogger<CustomService>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly CustomService _service;

    public CustomServiceTests()
    {
        _settingsMock = new Mock<ISettingService>();
        _loggerMock = new Mock<ILogger<CustomService>>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();

        _settingsMock.Setup(settingService => settingService.GetEncryptedSetting(It.IsAny<string>()))
            .ReturnsAsync(string.Empty);

        _service = new CustomService(
            _settingsMock.Object,
            new HttpClient(_httpMessageHandlerMock.Object),
            _loggerMock.Object,
            new LanguageCodeService(),
            new RequestTemplateService());
    }

    [Fact]
    public async Task TranslateAsync_PostsToChatCompletions()
    {
        UseSettings(BaseEndpoint);
        SetupResponse(() => ChatResponse("Hola"));

        var result = await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Equal("Hola", result);
        VerifyRequestUri(ChatEndpoint, Times.Once());
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task TranslateAsync_FullEndpointPath_IsPreserved()
    {
        UseSettings(ChatEndpoint);
        SetupResponse(() => ChatResponse("Hola"));

        var result = await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Equal("Hola", result);
        VerifyRequestUri(ChatEndpoint, Times.Once());
    }

    [Fact]
    public async Task TranslateAsync_ThinkingAndMaxTokens_AreInjected()
    {
        UseSettings(BaseEndpoint, aiThinking: "off", aiMaxTokens: "8192");
        var bodies = CaptureRequestBodies(() => ChatResponse("Hola"));

        await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        Assert.Equal("disabled", document.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(8192, document.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task TranslateAsync_WithApiKey_SendsBearerAuthorization()
    {
        const string apiKey = "sk-custom-key";
        _settingsMock.Setup(settingService => settingService.GetEncryptedSetting(SettingKeys.Translation.Custom.ApiKey))
            .ReturnsAsync("  " + apiKey + " \n");
        UseSettings(BaseEndpoint);
        SetupResponse(() => ChatResponse("Hola"));

        await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        // The custom provider reads its own key, never the LocalAI one.
        _settingsMock.Verify(
            settingService => settingService.GetEncryptedSetting(SettingKeys.Translation.Custom.ApiKey),
            Times.AtLeastOnce());
        VerifyAuthorization("Bearer " + apiKey, Times.Once());
    }

    [Fact]
    public async Task TranslateAsync_DoesNotReadLocalAiSettings()
    {
        UseSettings(BaseEndpoint);
        SetupResponse(() => ChatResponse("Hola"));

        await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        // The custom provider must never fall back to the LocalAI identity.
        _settingsMock.Verify(
            settingService => settingService.GetEncryptedSetting(SettingKeys.Translation.LocalAi.ApiKey),
            Times.Never());
        _settingsMock.Verify(
            settingService => settingService.GetSettings(It.Is<IEnumerable<string>>(keys =>
                keys.Contains(SettingKeys.Translation.Custom.Endpoint)
                && !keys.Contains(SettingKeys.Translation.LocalAi.Endpoint))),
            Times.Once());
    }

    [Fact]
    public async Task TranslateAsync_Unauthorized_DoesNotRetry()
    {
        UseSettings(BaseEndpoint);
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent(
                "{\"error\":{\"message\":\"Missing API key.\"}}",
                Encoding.UTF8,
                "application/json")
        });

        // A rejected credential is reported as an authentication failure, which lets the translation
        // chain skip the remaining models of this provider instead of failing them one by one.
        var exception = await Assert.ThrowsAsync<TranslationAuthenticationException>(
            () => _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None));

        Assert.Contains("Unauthorized", exception.Message);
        Assert.Contains("Missing API key", exception.Message);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task TranslateAsync_ServiceUnavailable_IsRetried()
    {
        UseSettings(BaseEndpoint);
        SetupResponseSequence(
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                Content = new StringContent("{\"error\":\"busy\"}", Encoding.UTF8, "application/json")
            },
            ChatResponse("Hola"));

        var result = await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Equal("Hola", result);
        VerifyRequestsSent(2);
    }

    [Fact]
    public async Task TranslateAsync_MissingEndpoint_Throws()
    {
        UseSettings(endpoint: string.Empty);
        SetupResponse(() => ChatResponse("Hola"));

        // Reported as a configuration failure so the translation chain skips this provider instead of
        // repeating the same unusable request for every subtitle line.
        await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None));

        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task TranslateAsync_MissingModel_Throws()
    {
        UseSettings(BaseEndpoint, model: string.Empty);
        SetupResponse(() => ChatResponse("Hola"));

        await Assert.ThrowsAsync<TranslationConfigurationException>(
            () => _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None));

        VerifyRequestsSent(0);
    }

    [Fact]
    public async Task TranslateBatchAsync_Unauthorized_ReportsAuthenticationFailure()
    {
        UseSettings(BaseEndpoint);
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent(
                "{\"error\":{\"message\":\"Missing API key.\"}}",
                Encoding.UTF8,
                "application/json")
        });

        // The batch path has to keep the classification too, otherwise the chain would retry every
        // model of this provider with the same rejected key.
        await Assert.ThrowsAsync<TranslationAuthenticationException>(
            () => _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None));

        // A rejected credential is not retried as a second, plain JSON request.
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task TranslateBatchAsync_RateLimited_IsRetried()
    {
        UseSettings(BaseEndpoint);
        SetupResponseSequence(
            new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.TooManyRequests,
                Content = new StringContent("{\"error\":\"slow down\"}", Encoding.UTF8, "application/json")
            },
            // Structured response, so the successful retry needs exactly one request.
            ChatResponse("{\"translations\":" + ValidJson + "}"));

        var result = await _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None);

        Assert.Equal("Hola", result[1]);
        VerifyRequestsSent(2);
    }

    [Fact]
    public async Task TranslateBatchAsync_StructuredOutput_ParsesWrapper()
    {
        UseSettings(BaseEndpoint);
        SetupResponse(() => ChatResponse("{\"translations\":" + ValidJson + "}"));

        var result = await _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Hola", result[1]);
        Assert.Equal("Mundo", result[2]);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task TranslateBatchAsync_FallsBackToJsonParsing()
    {
        UseSettings(BaseEndpoint);
        SetupResponseSequence(
            ChatResponse("not json at all"),
            ChatResponse(ValidJson));

        var result = await _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Mundo", result[2]);
        VerifyRequestsSent(2);
    }

    [Fact]
    public async Task TranslateBatchAsync_UnparsableResponses_RetryThenFail()
    {
        UseSettings(BaseEndpoint);
        SetupResponse(() => ChatResponse(MalformedJson));

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => _service.TranslateBatchAsync(Batch(), "en", "es", CancellationToken.None));

        Assert.Contains("Retry limit reached", exception.Message);
        Assert.IsType<TranslationParseException>(exception.InnerException);
        VerifyRequestsSent(6); // MaxRetries * (structured attempt + JSON fallback)
    }

    [Fact]
    public async Task ProofreadAsync_PostsProofreadPrompt()
    {
        UseSettings(BaseEndpoint);
        var bodies = CaptureRequestBodies(() => ChatResponse("Hola corregido"));

        var result = await _service.ProofreadAsync("Hello", "Hola", "en", "es", CancellationToken.None);

        Assert.Equal("Hola corregido", result);
        Assert.Single(bodies);
        using var document = JsonDocument.Parse(bodies[0]);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Contains("proofread the line", messages[0].GetProperty("content").GetString());
        Assert.Equal("Hola", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetModels_NotConfigured_ReturnsMessage()
    {
        _settingsMock.Setup(settingService => settingService.GetSetting(SettingKeys.Translation.Custom.Endpoint))
            .ReturnsAsync(string.Empty);

        var result = await _service.GetModels();

        Assert.NotNull(result.Message);
        Assert.Contains("Custom endpoint is not configured", result.Message);
        Assert.Empty(result.Options);
    }

    [Fact]
    public async Task GetModels_ReturnsCatalogue()
    {
        _settingsMock.Setup(settingService => settingService.GetSetting(SettingKeys.Translation.Custom.Endpoint))
            .ReturnsAsync(BaseEndpoint);
        SetupResponse(() => JsonResponse(new { data = new[] { new { id = "my-model" }, new { id = "another-model" } } }));

        var result = await _service.GetModels();

        Assert.Null(result.Message);
        Assert.Equal(2, result.Options.Count);
        VerifyRequestUri("http://localhost:8000/v1/models", Times.Once());
    }

    private static List<BatchSubtitleItem> Batch() =>
    [
        new() { Position = 1, Line = "Hello" },
        new() { Position = 2, Line = "World" }
    ];

    private static HttpResponseMessage ChatResponse(string translatedJson) =>
        JsonResponse(new { choices = new[] { new { message = new { content = translatedJson } } } });

    private static HttpResponseMessage JsonResponse(object body) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private void UseSettings(
        string endpoint,
        string model = "my-model",
        string? aiThinking = null,
        string? aiMaxTokens = null)
    {
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.Custom.Model, model },
            { SettingKeys.Translation.Custom.Endpoint, endpoint },
            { SettingKeys.Translation.Custom.ChatRequestTemplate, "" },
            { SettingKeys.Translation.AiPrompt, "Translate from {sourceLanguage} to {targetLanguage}" },
            { SettingKeys.Translation.AiUserPrompt, "{lineToTranslate}" },
            { SettingKeys.Translation.ProofreadPrompt, "Please proofread the line from {sourceLanguage} to {targetLanguage}" },
            { SettingKeys.Translation.ProofreadUserPrompt, "{translatedLine}" },
            { SettingKeys.Translation.RequestTimeout, "5" },
            { SettingKeys.Translation.MaxRetries, "3" },
            { SettingKeys.Translation.RetryDelay, "0" }, // No delay to keep the tests fast
            { SettingKeys.Translation.RetryDelayMultiplier, "1" },
            { SettingKeys.Translation.LanguageCodeFormat, "false" }
        };

        if (aiThinking != null)
        {
            settings[SettingKeys.Translation.AiThinking] = aiThinking;
        }

        if (aiMaxTokens != null)
        {
            settings[SettingKeys.Translation.AiMaxTokens] = aiMaxTokens;
        }

        _settingsMock.Setup(settingService => settingService.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(settings);
    }

    /// <summary>
    /// Answers every request with a freshly built response, so an attempt never reads content
    /// that a previous attempt already consumed.
    /// </summary>
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

    private void VerifyRequestUri(string expectedUri, Times times)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            times,
            ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.ToString() == expectedUri),
            ItExpr.IsAny<CancellationToken>());
    }

    private void VerifyAuthorization(string expectedHeader, Times times)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            times,
            ItExpr.Is<HttpRequestMessage>(request => HasAuthorization(request, expectedHeader)),
            ItExpr.IsAny<CancellationToken>());
    }

    private static bool HasAuthorization(HttpRequestMessage request, string expectedHeader)
    {
        if (request.Headers.TryGetValues("Authorization", out var values))
        {
            foreach (var value in values)
            {
                if (value == expectedHeader)
                {
                    return true;
                }
            }
        }

        return request.Headers.Authorization?.ToString() == expectedHeader;
    }
}
