export interface Statistics {
    totalLinesTranslated: number
    totalFilesTranslated: number
    totalCharactersTranslated: number
    totalMovies: number
    totalEpisodes: number
    totalSubtitles: number
    translationsByMediaType: Record<string, number>
    /**
     * Finished files per provider. A file that fell back across models is counted once under the
     * provider that translated most of its lines, matching the historical "files translated" metric.
     */
    translationsByService: Record<string, number>
    subtitlesByLanguage: Record<string, number>
    /**
     * Finished files per dominant model, keyed by backend `TranslationSource.DisplayName`
     * (`provider:model`). Counts files, not lines; a file that fell back across models is counted
     * once. Line-level fallback detail lives in `linesBySource`.
     */
    translationsByModel: Record<string, number>
    /**
     * Translated lines per source, keyed by backend `TranslationSource.DisplayName` (`provider:model`,
     * or a bare provider when there is no model). Unlike `translationsByService` and
     * `translationsByModel`, which count finished files, this counts the lines each source really
     * translated, so fallbacks show up per source.
     */
    linesBySource: Record<string, number>
}

export interface DailyStatistic {
    date: string
    translationCount: number
}
