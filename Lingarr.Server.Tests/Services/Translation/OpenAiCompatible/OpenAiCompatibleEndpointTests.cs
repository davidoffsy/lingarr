using System;
using Lingarr.Server.Services.Translation.OpenAiCompatible;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation.OpenAiCompatible;

public class OpenAiCompatibleEndpointTests
{
    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:8000/", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:8000/v1/", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:8000/v1/chat/completions", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:11434/api/generate", "http://localhost:11434/v1/chat/completions")]
    [InlineData("https://gateway.example.com/openai", "https://gateway.example.com/openai/v1/chat/completions")]
    [InlineData("https://gateway.example.com/openai/v1/chat/completions", "https://gateway.example.com/openai/v1/chat/completions")]
    public void ResolveChatCompletionsUrl_AppendsMissingPath(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(endpoint));
    }

    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1/chat/completions", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1/models", "http://localhost:8000/v1/models")]
    [InlineData("https://gateway.example.com/openai/v1", "https://gateway.example.com/openai/v1/models")]
    public void ResolveModelsUrl_AppendsModelsPath(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.ResolveModelsUrl(endpoint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveChatCompletionsUrl_Empty_Throws(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(endpoint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveModelsUrl_Empty_Throws(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => OpenAiCompatibleEndpoint.ResolveModelsUrl(endpoint));
    }

    [Theory]
    [InlineData("http://localhost:8000/v1/chat/completions", true)]
    [InlineData("http://localhost:8000/v1", false)]
    [InlineData("http://localhost:8000", false)]
    public void LooksLikeChatCompletions_DetectsCompletionSuffix(string endpoint, bool expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.LooksLikeChatCompletions(endpoint));
    }
}
