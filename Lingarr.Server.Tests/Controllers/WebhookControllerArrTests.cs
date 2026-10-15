using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Server.Controllers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Plex;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Controllers;

public class WebhookControllerArrTests
{
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<IArrWebhookSecretService> _arrSecrets = new();

    public WebhookControllerArrTests()
    {
        _jobs.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");
    }

    [Fact]
    public async Task SonarrWebhook_Unauthorized_Returns401WithoutReadingBody()
    {
        _arrSecrets.Setup(s => s.AuthenticateAsync("bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrWebhookAuthResult.Unauthorized("Invalid webhook token"));

        var controller = CreateController("bad", """{"eventType":"Download","series":{"id":1}}""");
        var result = await controller.SonarrWebhook("bad");

        Assert.IsType<UnauthorizedObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
        Assert.Equal(0, controller.HttpContext.Request.Body.Position);
    }

    [Fact]
    public async Task SonarrWebhook_OnboardingRequired_Returns403()
    {
        _arrSecrets.Setup(s => s.AuthenticateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrWebhookAuthResult.RequireOnboarding());

        var controller = CreateController("secret", """{"eventType":"Test"}""");
        var result = await controller.SonarrWebhook("secret");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task SonarrWebhook_TestPayload_Returns200WithoutEnqueue()
    {
        AllowToken();

        var controller = CreateController("secret", Json(new SonarrWebhookPayload { EventType = "Test" }));
        var result = await controller.SonarrWebhook("secret");

        Assert.IsType<OkObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task SonarrWebhook_ValidEpisode_Enqueues()
    {
        AllowToken();

        var payload = new SonarrWebhookPayload
        {
            EventType = "Download",
            Series = new SonarrWebhookSeries { Id = 7, Title = "Show" },
            Episodes = [new SonarrWebhookEpisode { Id = 21 }]
        };

        var controller = CreateController("secret", Json(payload));
        var result = await controller.SonarrWebhook("secret");

        Assert.IsType<OkObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task RadarrWebhook_TestPayload_Returns200WithoutEnqueue()
    {
        AllowToken();

        var controller = CreateController("secret", Json(new RadarrWebhookPayload { EventType = "Test" }));
        var result = await controller.RadarrWebhook("secret");

        Assert.IsType<OkObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task RadarrWebhook_ValidMovie_Enqueues()
    {
        AllowToken();

        var payload = new RadarrWebhookPayload
        {
            EventType = "Download",
            Movie = new RadarrWebhookMovie { Id = 3, Title = "Movie" }
        };

        var controller = CreateController("secret", Json(payload));
        var result = await controller.RadarrWebhook("secret");

        Assert.IsType<OkObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task RadarrWebhook_InvalidJson_Returns400()
    {
        AllowToken();

        var controller = CreateController("secret", "{not-json");
        var result = await controller.RadarrWebhook("secret");

        Assert.IsType<BadRequestObjectResult>(result);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    private void AllowToken()
    {
        _arrSecrets.Setup(s => s.AuthenticateAsync("secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrWebhookAuthResult.Allowed());
    }

    private WebhookController CreateController(string token, string json)
    {
        var controller = new WebhookController(
            _jobs.Object,
            Mock.Of<ISettingService>(),
            new PlexWebhookParser(),
            Mock.Of<IPlexWebhookInbox>(),
            _arrSecrets.Object,
            NullLogger<WebhookController>.Instance);

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?token={token}");
        context.Request.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private static string Json(object payload) => JsonSerializer.Serialize(payload);
}
