using Lingarr.Server.Services.Translation.LocalAi;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.LocalAi;

public class OpenCodeGoHeadersTests
{
    [Theory]
    [InlineData("https://opencode.ai/zen/go/v1")]
    [InlineData("https://opencode.ai/zen/go/v1/")]
    [InlineData("https://opencode.ai/zen/go/v1/chat/completions")]
    [InlineData("https://opencode.ai/zen/go")]
    [InlineData("https://api.opencode.ai/zen/go/v1/chat/completions")]
    [InlineData("https://OPENCODE.AI/ZEN/GO/v1")]
    public void Matches_OpenCodeGoUrls(string url)
    {
        Assert.True(OpenCodeGoHeaders.Matches(url));
    }

    [Theory]
    [InlineData("https://opencode.ai/zen/v1")]
    [InlineData("https://opencode.ai/zen/v1/chat/completions")]
    [InlineData("https://opencode.ai/zen/google/v1")]
    [InlineData("http://ollama:11434/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://not-opencode.ai/zen/go/v1")]
    [InlineData("https://opencode.ai.example.com/zen/go/v1")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a url")]
    public void DoesNotMatch_OtherUrls(string? url)
    {
        Assert.False(OpenCodeGoHeaders.Matches(url));
    }

    [Fact]
    public void NewSessionId_IsStableFormatAndUnique()
    {
        var first = OpenCodeGoHeaders.NewSessionId();
        var second = OpenCodeGoHeaders.NewSessionId();

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.NotEqual(first, second);
        Assert.Matches("^[0-9a-f]{32}$", first);
    }
}
