using System.Linq;
using Lingarr.Server.Services.Media;
using Xunit;

namespace Lingarr.Server.Tests.Services.Media;

public class SourceSubtitleRankerTests
{
    [Fact]
    public void Rank_PrefersSidecarThenNonForcedThenNonSdhThenLowerOrder()
    {
        var items = new[]
        {
            new SourceSubtitleRank(true, false, false, 2),
            new SourceSubtitleRank(false, true, false, 0),
            new SourceSubtitleRank(false, false, true, 0),
            new SourceSubtitleRank(false, false, false, 1)
        };

        var selected = SourceSubtitleRanker.Rank(items, rank => rank).First();

        Assert.Equal(new SourceSubtitleRank(false, false, false, 1), selected);
    }
}
