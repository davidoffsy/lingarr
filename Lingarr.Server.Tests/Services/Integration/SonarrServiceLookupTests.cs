using System.Collections.Generic;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;
using Lingarr.Server.Services.Integration;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Integration;

public class SonarrServiceLookupTests
{
    private readonly Mock<IIntegrationService> _integration = new();
    private readonly SonarrService _service;

    public SonarrServiceLookupTests()
    {
        _service = new SonarrService(_integration.Object);
    }

    [Fact]
    public async Task GetShowByTvdbId_DoesNotReturnFirstUnrelatedShow()
    {
        _integration.Setup(s => s.GetApiResponse<List<SonarrShow>>(
                "/api/v3/series?tvdbId=81189",
                It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync([
                CreateShow(1, tvdbId: 100, imdbId: "tt100"),
                CreateShow(2, tvdbId: 81189, imdbId: "tt0903747")
            ]);

        var show = await _service.GetShowByTvdbId(81189);

        Assert.NotNull(show);
        Assert.Equal(2, show!.Id);
        Assert.Equal(81189, show.TvdbId);
    }

    [Fact]
    public async Task GetShowByImdbId_DoesNotUseImdbQuery_AndFiltersLibrary()
    {
        _integration.Setup(s => s.GetApiResponse<List<SonarrShow>>(
                It.Is<string>(url => url.Contains("imdbId", System.StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IntegrationSettingKeys>()))
            .ThrowsAsync(new System.InvalidOperationException("imdbId query must not be used"));
        _integration.Setup(s => s.GetApiResponse<List<SonarrShow>>(
                "/api/v3/series/",
                It.IsAny<IntegrationSettingKeys>()))
            .ReturnsAsync([
                CreateShow(1, tvdbId: 100, imdbId: "tt100"),
                CreateShow(2, tvdbId: 81189, imdbId: "tt0903747")
            ]);

        var show = await _service.GetShowByImdbId("tt0903747");

        Assert.NotNull(show);
        Assert.Equal(2, show!.Id);
        _integration.Verify(
            s => s.GetApiResponse<List<SonarrShow>>(
                It.Is<string>(url => url.Contains("imdbId", System.StringComparison.OrdinalIgnoreCase)),
                It.IsAny<IntegrationSettingKeys>()),
            Times.Never);
    }

    private static SonarrShow CreateShow(int id, int tvdbId, string imdbId) =>
        new()
        {
            Id = id,
            Title = $"Show {id}",
            Path = $"/shows/{id}",
            Added = "2020-01-01",
            SeasonFolder = true,
            TvdbId = tvdbId,
            ImdbId = imdbId
        };
}
