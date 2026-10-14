using Lingarr.Server.Models.Integrations;

namespace Lingarr.Server.Interfaces.Services.Integration;

/// <summary>
/// Defines a service for interacting with the Radarr API.
/// </summary>
public interface IRadarrService
{
    /// <summary>
    /// Asynchronously retrieves a list of movies from the Radarr API.
    /// </summary>
    /// <returns>
    /// This method calls the Radarr API to fetch movies
    /// The task result contains a <see cref="List{T}"/> of <see cref="RadarrMovie"/>
    /// objects representing all movies, or <c>null</c> if the API call fails.
    /// </returns>
    Task<List<RadarrMovie>?> GetMovies();

    /// <summary>
    /// Asynchronously retrieves a movie from the Radarr API.
    /// </summary>
    /// <returns>
    /// This method calls the Radarr API to fetch a movie
    /// The task result contains a <see cref="RadarrMovie"/>
    /// objects representing the movie, or <c>null</c> if the API call fails.
    /// </returns>
    Task<RadarrMovie?> GetMovie(int MovieId);

    /// <summary>
    /// Looks up a movie in Radarr by TMDB id without fetching the whole library.
    /// </summary>
    Task<RadarrMovie?> GetMovieByTmdbId(int tmdbId);

    /// <summary>
    /// Looks up a movie in Radarr by IMDb id (e.g. <c>tt1234567</c>).
    /// Radarr cannot filter the movie list by imdbId, so this scans the library and matches <c>ImdbId</c>.
    /// </summary>
    Task<RadarrMovie?> GetMovieByImdbId(string imdbId);
}
