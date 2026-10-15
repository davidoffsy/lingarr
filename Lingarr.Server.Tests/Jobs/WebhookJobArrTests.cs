using System;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Core.Interfaces;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Jobs;
using Lingarr.Server.Models.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Jobs;

public class WebhookJobArrTests
{
    private readonly LingarrDbContext _db;
    private readonly Mock<IMediaService> _media = new();
    private readonly Mock<IMediaSubtitleProcessor> _processor = new();
    private readonly Mock<ISettingService> _settings = new();
    private readonly WebhookJob _job;

    public WebhookJobArrTests()
    {
        _db = new LingarrDbContext(new DbContextOptionsBuilder<LingarrDbContext>()
            .UseInMemoryDatabase($"arr-webhook-{Guid.NewGuid()}").Options);
        _job = new WebhookJob(_db, _media.Object, _processor.Object,
            Mock.Of<IPlexWebhookParser>(), Mock.Of<IPlexRaterFilter>(),
            Mock.Of<IPlexMediaResolver>(), Mock.Of<IBackgroundJobClient>(),
            _settings.Object, NullLogger<WebhookJob>.Instance);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task RadarrDownload_SyncsAndOnlyTranslatesWhenEnabled(string setting, bool translate)
    {
        var movie = new Movie { RadarrId = 12, Title = "Movie", Path = "/movies", FileName = "movie.mkv", DateAdded = DateTimeOffset.UtcNow };
        _db.Movies.Add(movie);
        await _db.SaveChangesAsync();
        _media.Setup(m => m.GetMovieIdOrSyncFromRadarrMovieId(12)).ReturnsAsync(movie.Id);
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.ArrWebhookAutoTranslate)).ReturnsAsync(setting);

        await _job.ProcessRadarrWebhook(new RadarrWebhookPayload
        {
            EventType = "Download", Movie = new RadarrWebhookMovie { Id = 12, Title = "Movie" }
        });

        _media.Verify(m => m.GetMovieIdOrSyncFromRadarrMovieId(12), Times.Once);
        _processor.Verify(p => p.ProcessMedia(It.IsAny<IMedia>(), MediaType.Movie),
            translate ? Times.Once : Times.Never);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task SonarrDownload_SyncsEveryEpisodeAndOnlyTranslatesWhenEnabled(string setting, bool translate)
    {
        var show = new Show { SonarrId = 5, Title = "Show", Path = "/shows", DateAdded = DateTimeOffset.UtcNow };
        var season = new Season { SeasonNumber = 1, Show = show };
        var episodes = new[] { 21, 22 }.Select(id => new Episode
        {
            SonarrId = id, EpisodeNumber = id, Title = $"Episode {id}", Season = season
        }).ToArray();
        _db.Episodes.AddRange(episodes);
        await _db.SaveChangesAsync();
        foreach (var episode in episodes)
            _media.Setup(m => m.GetEpisodeIdOrSyncFromSonarrEpisodeId(episode.SonarrId)).ReturnsAsync(episode.Id);
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.ArrWebhookAutoTranslate)).ReturnsAsync(setting);

        await _job.ProcessSonarrWebhook(new SonarrWebhookPayload
        {
            EventType = "Download", Series = new SonarrWebhookSeries { Id = 5, Title = "Show" },
            Episodes = episodes.Select(e => new SonarrWebhookEpisode { Id = e.SonarrId }).ToList()
        });

        foreach (var episode in episodes)
            _media.Verify(m => m.GetEpisodeIdOrSyncFromSonarrEpisodeId(episode.SonarrId), Times.Once);
        _processor.Verify(p => p.ProcessMedia(It.IsAny<IMedia>(), MediaType.Episode),
            translate ? Times.Exactly(2) : Times.Never());
    }

    [Fact]
    public async Task NonDownloadEvent_DoesNotSyncOrTranslate()
    {
        await _job.ProcessRadarrWebhook(new RadarrWebhookPayload
        {
            EventType = "Rename", Movie = new RadarrWebhookMovie { Id = 12 }
        });

        _media.Verify(m => m.GetMovieIdOrSyncFromRadarrMovieId(It.IsAny<int>()), Times.Never);
        _processor.Verify(p => p.ProcessMedia(It.IsAny<IMedia>(), It.IsAny<MediaType>()), Times.Never);
    }
}
