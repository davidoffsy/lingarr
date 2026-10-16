export interface Statistics {
    totalLinesTranslated: number
    totalFilesTranslated: number
    totalCharactersTranslated: number
    totalMovies: number
    totalEpisodes: number
    totalSubtitles: number
    translationsByMediaType: Record<string, number>
    translationsByService: Record<string, number>
    subtitlesByLanguage: Record<string, number>
    /**
     * Translated lines per source, keyed by `provider (model)` with a bare provider when the candidate
     * has no model override. Unlike `translationsByService`, which counts finished files, this counts
     * the lines each source really translated, so fallbacks show up per source.
     */
    linesBySource: Record<string, number>
}

export interface DailyStatistic {
    date: string
    translationCount: number
}
