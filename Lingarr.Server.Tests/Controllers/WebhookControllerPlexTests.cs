using System.IO;
using System.Text;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Core.Configuration;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Services.Plex;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class WebhookControllerPlexTests
{
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<ISettingService> _settings = new();
    private readonly PlexWebhookParser _parser = new();

    public WebhookControllerPlexTests()
    {
        _jobs.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.OnboardingCompleted))
            .ReturnsAsync("true");
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexMinRating))
            .ReturnsAsync("10");
    }

    [Fact]
    public async Task PlexWebhook_WrongSecret_Returns401()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("expected-secret");

        var controller = CreateController("wrong-secret", """{"event":"media.rate"}""");
        var result = await controller.PlexWebhook("wrong-secret");

        Assert.IsType<UnauthorizedObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task PlexWebhook_EmptySecret_Returns401()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync(string.Empty);

        var controller = CreateController("anything", """{"event":"media.rate"}""");
        var result = await controller.PlexWebhook("anything");

        Assert.IsType<UnauthorizedObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task PlexWebhook_OnboardingIncomplete_Returns403()
    {
        _settings.Setup(s => s.GetSetting(SettingKeys.Authentication.OnboardingCompleted))
            .ReturnsAsync("false");
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("expected-secret");

        var controller = CreateController("expected-secret", """{"event":"media.rate"}""");
        var result = await controller.PlexWebhook("expected-secret");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task PlexWebhook_ValidJson_Returns200AndEnqueues()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("expected-secret");

        var json = """{"event":"media.rate","rating":10,"Metadata":{"type":"movie"}}""";
        var controller = CreateController("expected-secret", json);
        var result = await controller.PlexWebhook("expected-secret");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task PlexWebhook_MediaPlay_Returns200WithoutEnqueue()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("expected-secret");

        var json = """{"event":"media.play","rating":10,"Metadata":{"type":"movie"}}""";
        var controller = CreateController("expected-secret", json);
        var result = await controller.PlexWebhook("expected-secret");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode ?? StatusCodes.Status200OK);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task PlexWebhook_LowRating_Returns200WithoutEnqueue()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("expected-secret");

        var json = """{"event":"media.rate","rating":6,"Metadata":{"type":"movie"}}""";
        var controller = CreateController("expected-secret", json);
        var result = await controller.PlexWebhook("expected-secret");

        Assert.IsType<OkObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task PlexWebhook_InvalidPayload_Returns400()
    {
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexWebhookSecret))
            .ReturnsAsync("expected-secret");

        var controller = CreateController("expected-secret", "{not-json");
        var result = await controller.PlexWebhook("expected-secret");

        Assert.IsType<BadRequestObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    private WebhookController CreateController(string token, string body)
    {
        var controller = new WebhookController(
            _jobs.Object,
            _settings.Object,
            _parser,
            Mock.Of<IPlexWebhookInbox>(),
            Mock.Of<IArrWebhookSecretService>(),
            NullLogger<WebhookController>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?token={token}");
        context.Request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
