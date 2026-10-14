namespace Lingarr.Server.Services.Plex;

public readonly record struct PlexExternalIds(string? TmdbId, string? ImdbId, string? TvdbId)
{
    public bool HasMovieIds => !string.IsNullOrEmpty(TmdbId) || !string.IsNullOrEmpty(ImdbId);
    public bool HasShowIds => !string.IsNullOrEmpty(TvdbId) || !string.IsNullOrEmpty(ImdbId);
}

public static class PlexGuidParser
{
    public static PlexExternalIds Parse(IEnumerable<string?>? guids)
    {
        string? tmdb = null;
        string? imdb = null;
        string? tvdb = null;
        if (guids == null)
        {
            return default;
        }

        foreach (var raw in guids)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var guid = raw.Trim();
            if (guid.StartsWith("tmdb://", StringComparison.OrdinalIgnoreCase))
            {
                tmdb ??= EmptyToNull(guid["tmdb://".Length..]);
            }
            else if (guid.StartsWith("imdb://", StringComparison.OrdinalIgnoreCase))
            {
                imdb ??= EmptyToNull(guid["imdb://".Length..]);
            }
            else if (guid.StartsWith("tvdb://", StringComparison.OrdinalIgnoreCase))
            {
                tvdb ??= EmptyToNull(guid["tvdb://".Length..]);
            }
        }

        return new PlexExternalIds(tmdb, imdb, tvdb);
    }

    private static string? EmptyToNull(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
