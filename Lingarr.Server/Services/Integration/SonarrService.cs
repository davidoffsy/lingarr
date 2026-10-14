using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Services.Integration;

public class SonarrService : ISonarrService
{
    private static readonly IntegrationSettingKeys SonarrKeys = new()
    {
        Url = "sonarr_url",
        ApiKey = "sonarr_api_key"
    };

    private readonly IIntegrationService _integrationService;

    public SonarrService(IIntegrationService integrationService)
    {
        _integrationService = integrationService;
    }
    
    /// <inheritdoc />
    public async Task<List<SonarrShow>?> GetShows()
    {
        return await _integrationService.GetApiResponse<List<SonarrShow>>(
            "/api/v3/series/",
            SonarrKeys);
    }
    
    /// <inheritdoc />
    public async Task<List<SonarrEpisode>?> GetEpisodes(int seriesNumber, int seasonNumber)
    {
        return await _integrationService.GetApiResponse<List<SonarrEpisode>>(
            $"/api/v3/episode?seriesId={seriesNumber}&seasonNumber={seasonNumber}&includeImages=true",
            SonarrKeys);
    }

    /// <inheritdoc />
    public async Task<SonarrEpisode?> GetEpisode(int episodeNumber)
    {
        return await _integrationService.GetApiResponse<SonarrEpisode>(
            $"/api/v3/episode/{episodeNumber}?includeImages=true",
            SonarrKeys);
    }
    
    /// <inheritdoc />
    public async Task<SonarrEpisodePath?> GetEpisodePath(int episodeId)
    {
        return await _integrationService.GetApiResponse<SonarrEpisodePath>(
            $"/api/v3/episode/{episodeId}",
            SonarrKeys);
    }

    /// <inheritdoc />
    public async Task<SonarrShow?> GetShowByTvdbId(int tvdbId)
    {
        var shows = await _integrationService.GetApiResponse<List<SonarrShow>>(
            $"/api/v3/series?tvdbId={tvdbId}",
            SonarrKeys);
        return shows?.FirstOrDefault(show => show.TvdbId == tvdbId);
    }

    /// <inheritdoc />
    public async Task<SonarrShow?> GetShowByImdbId(string imdbId)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return null;
        }

        // Sonarr's series list endpoint does not filter on imdbId; unknown query
        // params are ignored and the whole library is returned.
        var shows = await GetShows();
        return shows?.FirstOrDefault(show =>
            string.Equals(show.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));
    }
}
