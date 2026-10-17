using Lingarr.Server.Models.FileSystem;

namespace Lingarr.Server.Services.Media;

/// <summary>
/// Translation-source quality order: sidecar over embedded, non-forced over forced,
/// non-SDH/HI/CC over hearing-impaired, then stream index / sidecar order.
/// MKV Default is ignored because release groups often mark SDH as default.
/// LINQ to Objects is stable among equal ranks.
/// </summary>
public readonly record struct SourceSubtitleRank(
    bool IsEmbedded,
    bool IsForced,
    bool IsHearingImpaired,
    int Order) : IComparable<SourceSubtitleRank>
{
    public int CompareTo(SourceSubtitleRank other)
    {
        var embedded = IsEmbedded.CompareTo(other.IsEmbedded);
        if (embedded != 0)
        {
            return embedded;
        }

        var forced = IsForced.CompareTo(other.IsForced);
        if (forced != 0)
        {
            return forced;
        }

        var hearingImpaired = IsHearingImpaired.CompareTo(other.IsHearingImpaired);
        if (hearingImpaired != 0)
        {
            return hearingImpaired;
        }

        return Order.CompareTo(other.Order);
    }

    public static SourceSubtitleRank ForSubtitles(Subtitles subtitle) =>
        new(
            subtitle.IsEmbedded,
            SubtitleCaptionFlags.IsForced(subtitle.Caption),
            SubtitleCaptionFlags.IsHearingImpaired(subtitle.Caption),
            subtitle.IsEmbedded ? subtitle.TrackIndex ?? int.MaxValue : 0);
}

public static class SourceSubtitleRanker
{
    public static IOrderedEnumerable<T> Rank<T>(
        IEnumerable<T> items,
        Func<T, SourceSubtitleRank> keySelector) =>
        items.OrderBy(keySelector);
}
