using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Webhooks;
using Xunit;

namespace Lingarr.Server.Tests.Services.Webhooks;

public class ArrWebhookPayloadFilterTests
{
    [Fact]
    public void ShouldIgnoreRadarr_NullPayload_IsEmpty()
    {
        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreRadarr(null, out var reason));
        Assert.Equal("empty payload", reason);
    }

    [Fact]
    public void ShouldIgnoreRadarr_TestEvent_IsTrue()
    {
        var payload = new RadarrWebhookPayload { EventType = "Test" };

        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreRadarr(payload, out var reason));
        Assert.Equal("test event", reason);
    }

    [Fact]
    public void ShouldIgnoreRadarr_MissingMovie_IsTrue()
    {
        var payload = new RadarrWebhookPayload { EventType = "Download" };

        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreRadarr(payload, out var reason));
        Assert.Contains("movie", reason);
    }

    [Fact]
    public void ShouldIgnoreRadarr_ValidMovie_IsFalse()
    {
        var payload = new RadarrWebhookPayload
        {
            EventType = "Download",
            Movie = new RadarrWebhookMovie { Id = 12, Title = "Movie" }
        };

        Assert.False(ArrWebhookPayloadFilter.ShouldIgnoreRadarr(payload, out _));
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Grab")]
    [InlineData(null)]
    public void ShouldIgnoreRadarr_NonDownload_IsTrue(string? eventType)
    {
        var payload = new RadarrWebhookPayload
        {
            EventType = eventType,
            Movie = new RadarrWebhookMovie { Id = 12 }
        };

        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreRadarr(payload, out var reason));
        Assert.Equal("not a download event", reason);
    }

    [Fact]
    public void ShouldIgnoreSonarr_TestEvent_IsTrue()
    {
        var payload = new SonarrWebhookPayload { EventType = "Test" };

        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreSonarr(payload, out var reason));
        Assert.Equal("test event", reason);
    }

    [Fact]
    public void ShouldIgnoreSonarr_MissingEpisodes_IsTrue()
    {
        var payload = new SonarrWebhookPayload
        {
            EventType = "Download",
            Series = new SonarrWebhookSeries { Id = 1, Title = "Show" },
            Episodes = []
        };

        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreSonarr(payload, out var reason));
        Assert.Contains("episode", reason);
    }

    [Fact]
    public void ShouldIgnoreSonarr_ValidEpisode_IsFalse()
    {
        var payload = new SonarrWebhookPayload
        {
            EventType = "Download",
            Series = new SonarrWebhookSeries { Id = 1, Title = "Show" },
            Episodes = [new SonarrWebhookEpisode { Id = 99 }]
        };

        Assert.False(ArrWebhookPayloadFilter.ShouldIgnoreSonarr(payload, out _));
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Grab")]
    [InlineData(null)]
    public void ShouldIgnoreSonarr_NonDownload_IsTrue(string? eventType)
    {
        var payload = new SonarrWebhookPayload
        {
            EventType = eventType,
            Series = new SonarrWebhookSeries { Id = 1 },
            Episodes = [new SonarrWebhookEpisode { Id = 99 }]
        };

        Assert.True(ArrWebhookPayloadFilter.ShouldIgnoreSonarr(payload, out var reason));
        Assert.Equal("not a download event", reason);
    }
}
