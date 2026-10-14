using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Interfaces.Services.Integration;

/// <summary>
/// Defines a service for interacting with the Sonarr API.
/// </summary>
public interface ISonarrService
{
    /// <summary>
    /// Asynchronously retrieves a list of shows from the Sonarr API.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a <see cref="List{T}"/> of <see cref="SonarrShow"/> 
    /// objects representing all shows, or <c>null</c> if the API call fails.
    /// </returns>
    Task<List<SonarrShow>?> GetShows();

    /// <summary>
    /// Asynchronously retrieves a list of episodes for a specified series and season from the Sonarr API.
    /// </summary>
    /// <param name="seriesNumber">The ID of the series for which episodes are to be retrieved.</param>
    /// <param name="seasonNumber">The season number within the series for which episodes are to be retrieved.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a <see cref="List{T}"/> of <see cref="SonarrEpisode"/> 
    /// objects with the list of episodes, or <c>null</c> if the API call fails.
    /// </returns>
    Task<List<SonarrEpisode>?> GetEpisodes(int seriesNumber, int seasonNumber);

    /// <summary>
    /// Asynchronously retrieves an episode for a speicified episode id from the Sonarr API.
    /// </summary>
    /// <param name="episodeNumber">The ID of the episode for which the data are to be retrieved.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a <see cref="SonarrEpisode"/>
    /// object or <c>null</c> if the API call fails.
    /// </returns>
    Task<SonarrEpisode?> GetEpisode(int episodeNumber);

    /// <summary>
    /// Asynchronously retrieves the path information for a specified episode from the Sonarr API.
    /// </summary>
    /// <param name="episodeNumber">The ID of the episode for which the path information is to be retrieved.</param>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains a <see cref="SonarrEpisodePath"/> 
    /// with the path information of the episode, or <c>null</c> if the API call fails.
    /// </returns>
    Task<SonarrEpisodePath?> GetEpisodePath(int episodeNumber);

    /// <summary>
    /// Looks up a series in Sonarr by TVDB id without fetching the whole library.
    /// </summary>
    Task<SonarrShow?> GetShowByTvdbId(int tvdbId);

    /// <summary>
    /// Looks up a series in Sonarr by IMDb id (e.g. <c>tt1234567</c>).
    /// Sonarr cannot filter the series list by imdbId, so this scans the library and matches <c>ImdbId</c>.
    /// </summary>
    Task<SonarrShow?> GetShowByImdbId(string imdbId);
}
