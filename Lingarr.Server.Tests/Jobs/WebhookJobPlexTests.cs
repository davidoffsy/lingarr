using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Plex;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class WebhookJobPlexTests
{
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<IMediaSubtitleProcessor> _processor = new();
    private readonly Mock<IPlexRaterFilter> _rater = new();
    private readonly Mock<IPlexMediaResolver> _resolver = new();
    private readonly Mock<IBackgroundJobClient> _jobs = new();
    private readonly Mock<ISettingService> _settings = new();
    private readonly PlexWebhookParser _parser = new();
    private readonly WebhookJob _job;

    public WebhookJobPlexTests()
    {
        var options = new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase("plex-webhook-job-" + System.Guid.NewGuid())
            .Options;
        var db = new LingarrDbContext(options);
        _jobs.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("job-id");
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexMinRating))
            .ReturnsAsync("10");
        _rater.Setup(r => r.IsAllowedAsync(It.IsAny<PlexWebhookAccount?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<PlexWebhookPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlexMediaResolveResult.Miss("not in Lingarr library"));
        _job = new WebhookJob(
            db,
            _media.Object,
            _processor.Object,
            _parser,
            _rater.Object,
            _resolver.Object,
            _jobs.Object,
            _settings.Object,
            NullLogger<WebhookJob>.Instance);
    }

    [Fact]
    public async Task ProcessPlexWebhook_MediaPlay_DoesNotAskRater()
    {
        await _job.ProcessPlexWebhook("""{"event":"media.play","rating":10,"Metadata":{"type":"movie"}}""");

        _rater.Verify(
            r => r.IsAllowedAsync(It.IsAny<PlexWebhookAccount?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _resolver.Verify(
            r => r.ResolveAsync(It.IsAny<PlexWebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _processor.Verify(
            p => p.ProcessMedia(It.IsAny<Lingarr.Core.Interfaces.IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPlexWebhook_LowRating_DoesNotAskRater()
    {
        await _job.ProcessPlexWebhook("""{"event":"media.rate","rating":6,"Metadata":{"type":"movie"}}""");

        _rater.Verify(
            r => r.IsAllowedAsync(It.IsAny<PlexWebhookAccount?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _resolver.Verify(
            r => r.ResolveAsync(It.IsAny<PlexWebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPlexWebhook_TypeShow_DoesNotAskRater()
    {
        await _job.ProcessPlexWebhook("""{"event":"media.rate","rating":10,"Metadata":{"type":"show"}}""");

        _rater.Verify(
            r => r.IsAllowedAsync(It.IsAny<PlexWebhookAccount?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _resolver.Verify(
            r => r.ResolveAsync(It.IsAny<PlexWebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessPlexWebhook_PassingFilters_DoesNotExtract()
    {
        await _job.ProcessPlexWebhook(
            """{"event":"media.rate","rating":10,"Account":{"id":1,"title":"Owner"},"Metadata":{"type":"movie","ratingKey":"1"}}""");

        _rater.Verify(
            r => r.IsAllowedAsync(It.IsAny<PlexWebhookAccount?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _resolver.Verify(
            r => r.ResolveAsync(It.IsAny<PlexWebhookPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _processor.Verify(
            p => p.ProcessMedia(It.IsAny<Lingarr.Core.Interfaces.IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
        _jobs.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    [Fact]
    public async Task ProcessPlexWebhook_ResolvedMedia_EnqueuesTranslationQueueJob()
    {
        var resolved = PlexMediaResolveResult.Hit(MediaType.Movie, 9, "/movies/Foo/Foo.mkv", "1", "Foo");
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<PlexWebhookPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolved);

        await _job.ProcessPlexWebhook(
            """{"event":"media.rate","rating":10,"Account":{"id":1,"title":"Owner"},"Metadata":{"type":"movie","ratingKey":"1"}}""");

        _jobs.Verify(
            c => c.Create(
                It.Is<Job>(job =>
                    job.Type == typeof(PlexRatingTriggerJob)
                    && Equals(job.Args[0], MediaType.Movie)
                    && Equals(job.Args[1], 9)),
                It.IsAny<IState>()),
            Times.Once);
        _processor.Verify(
            p => p.ProcessMedia(It.IsAny<Lingarr.Core.Interfaces.IMedia>(), It.IsAny<MediaType>()),
            Times.Never);
    }
}
