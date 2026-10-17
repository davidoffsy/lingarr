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
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

/// <summary>
/// Covers how the Anthropic retry loop handles classified failures: rejected credentials must surface
/// untouched and must not be retried, while throttling has to reach the retry branch.
/// </summary>
public class AnthropicServiceTests
{
    private readonly Mock<ISettingService> _settingsMock = new();
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock = new();
    private readonly HttpClient _httpClient;
    private readonly AnthropicService _service;

    public AnthropicServiceTests()
    {
        _settingsMock.Setup(settingService => settingService.GetEncryptedSetting(It.IsAny<string>()))
            .ReturnsAsync("test-api-key");

        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _service = new AnthropicService(
            _settingsMock.Object,
            _httpClient,
            Mock.Of<ILogger<AnthropicService>>(),
            new LanguageCodeService(),
            new RequestTemplateService());
    }

    [Fact]
    public async Task TranslateAsync_Unauthorized_DoesNotRetry()
    {
        UseSettings(maxRetries: 3);
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("{\"error\":\"invalid x-api-key\"}")
        });

        // The authentication failure has to survive the retry loop, otherwise the translation chain
        // cannot skip the remaining models of this provider.
        var exception = await Assert.ThrowsAsync<TranslationAuthenticationException>(
            () => _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None));

        Assert.Contains("Unauthorized", exception.Message);
        VerifyRequestsSent(1);
    }

    [Fact]
    public async Task TranslateAsync_TooManyRequests_IsRetriedUntilRetriesAreExhausted()
    {
        UseSettings(maxRetries: 2);
        SetupResponse(() => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            Content = new StringContent("{\"error\":\"rate limited\"}")
        });

        var exception = await Assert.ThrowsAsync<TranslationException>(
            () => _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None));

        Assert.Contains("Retry limit reached", exception.Message);
        var inner = Assert.IsType<TranslationResponseException>(exception.InnerException);
        Assert.Equal(HttpStatusCode.TooManyRequests, inner.StatusCode);
        VerifyRequestsSent(2); // MaxRetries
    }

    [Fact]
    public async Task TranslateAsync_Success_PostsOfficialMessagesEndpoint()
    {
        UseSettings(maxRetries: 1);
        SetupResponse(() => JsonResponse(new
        {
            stop_reason = "end_turn",
            content = new[] { new { type = "text", text = "Hola" } }
        }));

        var result = await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Equal("Hola", result);
        VerifyRequestUri("https://api.anthropic.com/v1/messages", Times.Once());
        VerifyHeader("x-api-key", "test-api-key", Times.Once());
        VerifyHeader("anthropic-version", "2023-06-01", Times.Once());
        VerifyHeader("User-Agent", $"{LingarrVersion.Name}/{LingarrVersion.Number}", Times.Once());
        VerifyHeaderAbsent("Authorization");
    }

    [Fact]
    public async Task TranslateAsync_DoesNotMutateSharedHttpClientTimeout()
    {
        var timeout = _httpClient.Timeout;
        UseSettings(maxRetries: 1);
        SetupResponse(() => JsonResponse(new
        {
            stop_reason = "end_turn",
            content = new[] { new { type = "text", text = "Hola" } }
        }));

        await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Equal(timeout, _httpClient.Timeout);
    }

    [Fact]
    public async Task GetModels_UsesMessagesCatalogueHeaders()
    {
        _settingsMock.Setup(settingService => settingService.GetSetting(SettingKeys.Translation.Anthropic.Version))
            .ReturnsAsync("2023-06-01");
        SetupResponse(() => JsonResponse(new { data = new[] { new { id = "claude-a" } } }));

        var result = await _service.GetModels();

        Assert.Null(result.Message);
        Assert.Single(result.Options);
        Assert.Equal("claude-a", result.Options[0].Value);
        VerifyRequestUri("https://api.anthropic.com/v1/models", Times.Once());
        VerifyHeader("x-api-key", "test-api-key", Times.Once());
        VerifyHeader("anthropic-version", "2023-06-01", Times.Once());
        VerifyHeader("User-Agent", $"{LingarrVersion.Name}/{LingarrVersion.Number}", Times.Once());
        VerifyHeaderAbsent("Authorization");
    }

    [Fact]
    public async Task TranslateAsync_TruncatedText_IsStillReturned()
    {
        // Official Anthropic keeps its historical behaviour: a max_tokens stop still yields the
        // partial text. Custom Messages treats the same payload as a failed candidate.
        UseSettings(maxRetries: 1);
        SetupResponse(() => JsonResponse(new
        {
            stop_reason = "max_tokens",
            content = new[] { new { type = "text", text = "Hol" } }
        }));

        var result = await _service.TranslateAsync("Hello", "en", "es", null, null, CancellationToken.None);

        Assert.Equal("Hol", result);
    }

    private void UseSettings(int maxRetries)
    {
        var settings = new Dictionary<string, string>
        {
            { SettingKeys.Translation.Anthropic.Model, "claude-3-5-sonnet-latest" },
            { SettingKeys.Translation.Anthropic.ApiKey, "test-api-key" },
            { SettingKeys.Translation.Anthropic.Version, "2023-06-01" },
            { SettingKeys.Translation.Anthropic.RequestTemplate, "" },
            { SettingKeys.Translation.AiPrompt, "Translate from {sourceLanguage} to {targetLanguage}" },
            { SettingKeys.Translation.AiUserPrompt, "{lineToTranslate}" },
            { SettingKeys.Translation.RequestTimeout, "5" },
            { SettingKeys.Translation.MaxRetries, maxRetries.ToString() },
            { SettingKeys.Translation.RetryDelay, "0" },
            { SettingKeys.Translation.RetryDelayMultiplier, "1" },
            { SettingKeys.Translation.LanguageCodeFormat, "false" }
        };

        _settingsMock.Setup(settingService => settingService.GetSettings(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(settings);
    }

    /// <summary>
    /// Answers every request with a freshly built response, so an attempt never reads content that a
    /// previous attempt already consumed.
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

    private static HttpResponseMessage JsonResponse(object body) => new()
    {
        StatusCode = HttpStatusCode.OK,
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private void VerifyRequestUri(string expectedUri, Times times)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            times,
            ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.ToString() == expectedUri),
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

    private void VerifyRequestsSent(int count)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(count),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
