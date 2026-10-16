using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Contracts.Exceptions;
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
    private readonly AnthropicService _service;

    public AnthropicServiceTests()
    {
        _settingsMock.Setup(settingService => settingService.GetEncryptedSetting(It.IsAny<string>()))
            .ReturnsAsync("test-api-key");

        _service = new AnthropicService(
            _settingsMock.Object,
            new HttpClient(_httpMessageHandlerMock.Object),
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

    private void VerifyRequestsSent(int count)
    {
        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(count),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
