namespace Lingarr.Server.Services.Translation.Attribution;

/// <summary>Number of lines one source translated.</summary>
/// <param name="Source">Provider and model that produced the lines.</param>
/// <param name="Lines">Translated lines attributed to <paramref name="Source"/>.</param>
public sealed record TranslationSourceCount(TranslationSource Source, int Lines);

/// <summary>
/// Aggregated attribution of a finished translation: how many lines each provider/model produced.
/// A single-source run has one entry; a run that fell back to another provider or model has several,
/// which is what the subtitle translator tag and the statistics report.
/// </summary>
/// <param name="Counts">
/// Per source line counts, most lines first (ties broken by source name) so <see cref="Dominant"/> is
/// the source that did most of the work.
/// </param>
public sealed record TranslationSourceSummary(IReadOnlyList<TranslationSourceCount> Counts)
{
    /// <summary>Attribution of a run that produced no line.</summary>
    public static readonly TranslationSourceSummary Empty = new([]);

    /// <summary>Aggregates one source per successfully translated line.</summary>
    public static TranslationSourceSummary From(IEnumerable<TranslationSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var counts = new Dictionary<TranslationSource, int>();
        foreach (var source in sources)
        {
            counts[source] = counts.GetValueOrDefault(source) + 1;
        }

        return new TranslationSourceSummary(counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key.Provider, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Model, StringComparer.Ordinal)
            .Select(entry => new TranslationSourceCount(entry.Key, entry.Value))
            .ToList());
    }

    public int TotalLines => Counts.Sum(count => count.Lines);

    public bool IsEmpty => Counts.Count == 0;

    /// <summary>True when more than one provider/model produced lines in the same run.</summary>
    public bool IsMixed => Counts.Count > 1;

    /// <summary>Source that produced most lines, or null when nothing was translated.</summary>
    public TranslationSourceCount? Dominant => Counts.Count == 0 ? null : Counts[0];

    /// <summary>All sources rendered as <c>custom:model-a, openai</c>.</summary>
    public string Describe() => string.Join(", ", Counts.Select(count => count.Source.DisplayName));
}
