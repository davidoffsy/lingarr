using Lingarr.Server.Services.Translation.LocalAi;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.LocalAi;

public class LocalAiEndpointResolverTests
{
    [Theory]
    [InlineData("https://opencode.ai/zen/go/v1", "https://opencode.ai/zen/go/v1/chat/completions")]
    [InlineData("https://opencode.ai/zen/go/v1/", "https://opencode.ai/zen/go/v1/chat/completions")]
    [InlineData("https://opencode.ai/zen/go/v1/chat/completions", "https://opencode.ai/zen/go/v1/chat/completions")]
    [InlineData("http://ollama:11434", "http://ollama:11434/v1/chat/completions")]
    [InlineData("http://ollama:11434/v1/chat/completions", "http://ollama:11434/v1/chat/completions")]
    [InlineData("http://ollama:11434/api/generate", "http://ollama:11434/v1/chat/completions")]
    public void ResolveCompletionEndpoint_OpenAiChat_AppendsChatPath(string endpoint, string expected)
    {
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(endpoint, LocalAiProtocol.OpenAiChat);

        Assert.True(resolved.IsChat);
        Assert.Equal(expected, resolved.Url);
    }

    [Theory]
    [InlineData("http://ollama:11434", "http://ollama:11434/api/generate")]
    [InlineData("http://ollama:11434/api/generate", "http://ollama:11434/api/generate")]
    [InlineData("http://ollama:11434/v1", "http://ollama:11434/api/generate")]
    public void ResolveCompletionEndpoint_OllamaGenerate_AppendsGeneratePath(string endpoint, string expected)
    {
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(endpoint, LocalAiProtocol.OllamaGenerate);

        Assert.False(resolved.IsChat);
        Assert.Equal(expected, resolved.Url);
    }

    [Fact]
    public void ResolveCompletionEndpoint_Auto_PreservesFullChatUrl()
    {
        var endpoint = "http://ollama:11434/v1/chat/completions";
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(endpoint, LocalAiProtocol.Auto);

        Assert.True(resolved.IsChat);
        Assert.Equal(endpoint, resolved.Url);
    }

    [Fact]
    public void ResolveCompletionEndpoint_Auto_PreservesGenerateUrl()
    {
        var endpoint = "http://ollama:11434/api/generate";
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(endpoint, LocalAiProtocol.Auto);

        Assert.False(resolved.IsChat);
        Assert.Equal(endpoint, resolved.Url);
    }

    [Fact]
    public void ResolveCompletionEndpoint_Auto_GatewayPathIsPostedAsIs()
    {
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(
            "https://opencode.ai/zen/go/v1",
            LocalAiProtocol.Auto);

        Assert.False(resolved.IsChat);
        Assert.Equal("https://opencode.ai/zen/go/v1", resolved.Url);
    }

    [Theory]
    [InlineData("http://ollama:11434", "http://ollama:11434/api/generate")]
    [InlineData("http://ollama:11434/", "http://ollama:11434/api/generate")]
    [InlineData("http://ollama:11434/v1", "http://ollama:11434/api/generate")]
    public void ResolveCompletionEndpoint_Auto_BareHost_UsesGenerate(string endpoint, string expected)
    {
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(endpoint, LocalAiProtocol.Auto);

        Assert.False(resolved.IsChat);
        Assert.Equal(expected, resolved.Url);
    }

    [Fact]
    public void ResolveCompletionEndpoint_Auto_CustomGeneratePath_IsPreserved()
    {
        var endpoint = "http://custom:8080/my/generate";
        var resolved = LocalAiEndpointResolver.ResolveCompletionEndpoint(endpoint, LocalAiProtocol.Auto);

        Assert.False(resolved.IsChat);
        Assert.Equal(endpoint, resolved.Url);
    }

    [Theory]
    [InlineData("https://opencode.ai/zen/go/v1", "https://opencode.ai/zen/go/v1/models")]
    [InlineData("https://opencode.ai/zen/go/v1/chat/completions", "https://opencode.ai/zen/go/v1/models")]
    [InlineData("http://ollama:11434", "http://ollama:11434/v1/models")]
    public void ResolveModelsUrl_OpenAiChat_UsesModelsPath(string endpoint, string expected)
    {
        Assert.Equal(expected, LocalAiEndpointResolver.ResolveModelsUrl(endpoint, LocalAiProtocol.OpenAiChat));
    }

    [Theory]
    [InlineData("http://ollama:11434", "http://ollama:11434/api/tags")]
    [InlineData("http://ollama:11434/api/generate", "http://ollama:11434/api/tags")]
    [InlineData("http://ollama:11434/v1", "http://ollama:11434/api/tags")]
    public void ResolveModelsUrl_OllamaGenerate_UsesTagsPath(string endpoint, string expected)
    {
        Assert.Equal(expected, LocalAiEndpointResolver.ResolveModelsUrl(endpoint, LocalAiProtocol.OllamaGenerate));
    }

    [Fact]
    public void ResolveModelsUrl_Auto_ChatUrlUsesModels()
    {
        var url = LocalAiEndpointResolver.ResolveModelsUrl(
            "http://ollama:11434/v1/chat/completions",
            LocalAiProtocol.Auto);

        Assert.Equal("http://ollama:11434/v1/models", url);
    }

    [Theory]
    [InlineData("http://ollama:11434", "http://ollama:11434/api/tags")]
    [InlineData("http://ollama:11434/v1", "http://ollama:11434/api/tags")]
    public void ResolveModelsUrl_Auto_BareHostUsesTags(string endpoint, string expected)
    {
        Assert.Equal(expected, LocalAiEndpointResolver.ResolveModelsUrl(endpoint, LocalAiProtocol.Auto));
    }

    [Fact]
    public void ParseProtocol_Unknown_IsAuto()
    {
        Assert.Equal(LocalAiProtocol.Auto, LocalAiProtocolParser.Parse(null));
        Assert.Equal(LocalAiProtocol.Auto, LocalAiProtocolParser.Parse(""));
        Assert.Equal(LocalAiProtocol.OpenAiChat, LocalAiProtocolParser.Parse("openai_chat"));
        Assert.Equal(LocalAiProtocol.OllamaGenerate, LocalAiProtocolParser.Parse("ollama_generate"));
    }
}
