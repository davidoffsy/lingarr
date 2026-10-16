namespace Lingarr.Server.Services.Translation.Attribution;

/// <summary>
/// Records which provider and model produced each translated position of a running translation.
/// <para>
/// One entry per position: a position that is translated again (retry, batch fallback, reuse) keeps
/// the source of the attempt that actually filled it, so summaries built from the tracker count every
/// successful line exactly once and resume runs do not double count.
/// </para>
/// </summary>
public sealed class TranslationSourceTracker
{
    private readonly Dictionary<int, TranslationAttribution> _byPosition = [];

    /// <summary>Attribution per successfully translated position, keyed by subtitle position.</summary>
    public IReadOnlyDictionary<int, TranslationAttribution> ByPosition => _byPosition;

    public bool IsEmpty => _byPosition.Count == 0;

    /// <summary>Attributes <paramref name="position"/> to the candidate that filled it.</summary>
    public void Record(int position, TranslationAttribution attribution) => _byPosition[position] = attribution;

    public bool TryGet(int position, out TranslationAttribution attribution) =>
        _byPosition.TryGetValue(position, out attribution);

    /// <summary>Line counts per source over every recorded position.</summary>
    public TranslationSourceSummary Summarize() => Summarize(_byPosition.Keys);

    /// <summary>
    /// Line counts per source restricted to <paramref name="positions"/>, used when only part of a run
    /// is translated for the first time (resume) or newly written (incremental statistics).
    /// </summary>
    public TranslationSourceSummary Summarize(IEnumerable<int> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var sources = new List<TranslationSource>();
        foreach (var position in positions)
        {
            if (_byPosition.TryGetValue(position, out var attribution))
            {
                sources.Add(attribution.Source);
            }
        }

        return TranslationSourceSummary.From(sources);
    }
}
