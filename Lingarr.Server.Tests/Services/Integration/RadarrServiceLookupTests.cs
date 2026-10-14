using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services.Integration;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Integration;

public class RadarrServiceLookupTests
{
    private readonly Mock<IIntegrationService> _integration = new();
    private readonly RadarrService _service;

    public RadarrServiceLookupTests()
    {
        _service = new RadarrService(_integration.Object);
    }

    [Fact]
    public async Task GetMovieByTmdbId_DoesNotReturnFirstUnrelatedMovie()
    {
        _integration.Setup(s => s.GetApiResponse<List<RadarrMovie>>(
                "/api/v3/movie?tmdbId=222",
                It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync([
                CreateMovie(1, tmdbId: 111, imdbId: "tt111"),
                CreateMovie(2, tmdbId: 222, imdbId: "tt222")
            ]);

        var movie = await _service.GetMovieByTmdbId(222);

        Assert.NotNull(movie);
        Assert.Equal(2, movie!.Id);
        Assert.Equal(222, movie.TmdbId);
    }

    [Fact]
    public async Task GetMovieByImdbId_DoesNotUseImdbQuery_AndFiltersLibrary()
    {
        _integration.Setup(s => s.GetApiResponse<List<RadarrMovie>>(
                It.Is<string>(url => url.Contains("imdbId", System.StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IntegrationSettingKeys>()))
            .ThrowsAsync(new System.InvalidOperationException("imdbId query must not be used"));
        _integration.Setup(s => s.GetApiResponse<List<RadarrMovie>>(
                "/api/v3/movie/",
                It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync([
                CreateMovie(1, tmdbId: 111, imdbId: "tt111"),
                CreateMovie(2, tmdbId: 222, imdbId: "tt222")
            ]);

        var movie = await _service.GetMovieByImdbId("tt222");

        Assert.NotNull(movie);
        Assert.Equal(2, movie!.Id);
        _integration.Verify(
            s => s.GetApiResponse<List<RadarrMovie>>(
                It.Is<string>(url => url.Contains("imdbId", System.StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IntegrationSettingKeys>()),
            Times.Never);
    }

    private static RadarrMovie CreateMovie(int id, int tmdbId, string imdbId) =>
        new()
        {
            Id = id,
            Title = $"Movie {id}",
            Path = $"/movies/{id}",
            RootFolderPath = "/movies",
            Added = "2020-01-01",
            HasFile = true,
            TmdbId = tmdbId,
            ImdbId = imdbId
        };
}
