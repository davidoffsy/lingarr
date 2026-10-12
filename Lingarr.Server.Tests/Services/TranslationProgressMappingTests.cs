using Lingarr.Server.Services;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class TranslationProgressMappingTests
{
    [Fact]
    public void FromExtractPercent_StartsAtOneSoTheBarAppears()
    {
        Assert.Equal(1, TranslationProgressMapping.FromExtractPercent(0));
        Assert.Equal(1, TranslationProgressMapping.FromExtractPercent(-5));
    }

    [Theory]
    [InlineData(25, 5)]
    [InlineData(50, 10)]
    [InlineData(100, 20)]
    public void FromExtractPercent_MapsOntoZeroToTwenty(int extractPercent, int expected)
    {
        Assert.Equal(expected, TranslationProgressMapping.FromExtractPercent(extractPercent));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(50, 60)]
    [InlineData(100, 100)]
    public void FromTranslatePercent_MapsOntoTwentyToHundred(int translatePercent, int expected)
    {
        Assert.Equal(expected, TranslationProgressMapping.FromTranslatePercent(translatePercent));
    }
}
