using System;
using Lingarr.Contracts.Exceptions;
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
    [InlineData("http://localhost:8000/v1/messages", "http://localhost:8000/v1/chat/completions")]
    [InlineData("http://localhost:11434/api/generate", "http://localhost:11434/v1/chat/completions")]
    [InlineData("https://gateway.example.com/openai", "https://gateway.example.com/openai/v1/chat/completions")]
    [InlineData("https://gateway.example.com/openai/v1/chat/completions", "https://gateway.example.com/openai/v1/chat/completions")]
    public void ResolveChatCompletionsUrl_AppendsMissingPath(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(endpoint));
    }

    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/v1/responses")]
    [InlineData("http://localhost:8000/", "http://localhost:8000/v1/responses")]
    [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/responses")]
    [InlineData("http://localhost:8000/v1/", "http://localhost:8000/v1/responses")]
    [InlineData("http://localhost:8000/v1/responses", "http://localhost:8000/v1/responses")]
    [InlineData("http://localhost:8000/responses", "http://localhost:8000/v1/responses")]
    // A path of the other protocol is still an API path: it is replaced, never treated as a base URL.
    [InlineData("http://localhost:8000/v1/chat/completions", "http://localhost:8000/v1/responses")]
    [InlineData("http://localhost:8000/v1/messages", "http://localhost:8000/v1/responses")]
    [InlineData("https://gateway.example.com/openai", "https://gateway.example.com/openai/v1/responses")]
    [InlineData("https://gateway.example.com/openai/v1/responses", "https://gateway.example.com/openai/v1/responses")]
    public void ResolveResponsesUrl_AppendsMissingPath(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.ResolveResponsesUrl(endpoint));
    }

    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/v1/messages")]
    [InlineData("http://localhost:8000/", "http://localhost:8000/v1/messages")]
    [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/messages")]
    [InlineData("http://localhost:8000/v1/", "http://localhost:8000/v1/messages")]
    [InlineData("http://localhost:8000/v1/messages", "http://localhost:8000/v1/messages")]
    [InlineData("http://localhost:8000/messages", "http://localhost:8000/v1/messages")]
    // A path of another protocol is still an API path: it is replaced, never treated as a base URL.
    [InlineData("http://localhost:8000/v1/chat/completions", "http://localhost:8000/v1/messages")]
    [InlineData("http://localhost:8000/v1/responses", "http://localhost:8000/v1/messages")]
    [InlineData("https://gateway.example.com/anthropic/v1/messages", "https://gateway.example.com/anthropic/v1/messages")]
    public void ResolveMessagesUrl_AppendsMissingPath(string endpoint, string expected)
    {
        Assert.Equal(expected, OpenAiCompatibleEndpoint.ResolveMessagesUrl(endpoint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveMessagesUrl_Empty_Throws(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => OpenAiCompatibleEndpoint.ResolveMessagesUrl(endpoint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveResponsesUrl_Empty_Throws(string endpoint)
    {
        Assert.Throws<ArgumentException>(() => OpenAiCompatibleEndpoint.ResolveResponsesUrl(endpoint));
    }

    [Theory]
    [InlineData("http://localhost:8000", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1/chat/completions", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1/models", "http://localhost:8000/v1/models")]
    [InlineData("http://localhost:8000/v1/messages", "http://localhost:8000/v1/models")]
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
    [InlineData("ftp://localhost:8000")]
    [InlineData("localhost:8000")]
    [InlineData("not a url")]
    [InlineData("file:///tmp/api")]
    public void ResolveChatCompletionsUrl_NonHttp_ThrowsConfigurationException(string endpoint)
    {
        Assert.Throws<TranslationConfigurationException>(
            () => OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(endpoint));
    }

    [Fact]
    public void ResolveChatCompletionsUrl_PreservesQuery()
    {
        Assert.Equal(
            "http://localhost:8000/v1/chat/completions?api-version=1",
            OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(
                "http://localhost:8000/v1/chat/completions?api-version=1"));
        Assert.Equal(
            "http://localhost:8000/v1/chat/completions?api-version=1",
            OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl("http://localhost:8000/v1?api-version=1"));
    }

    [Fact]
    public void ResolveChatCompletionsUrl_AzureDeploymentPath_IsPreserved()
    {
        const string azure =
            "https://my.openai.azure.com/openai/deployments/gpt-4o/chat/completions?api-version=2024-02-15-preview";

        Assert.Equal(azure, OpenAiCompatibleEndpoint.ResolveChatCompletionsUrl(azure));
    }

    [Fact]
    public void ResolveResponsesUrl_PreservesQuery()
    {
        Assert.Equal(
            "http://localhost:8000/v1/responses?api-version=1",
            OpenAiCompatibleEndpoint.ResolveResponsesUrl("http://localhost:8000/v1/responses?api-version=1"));
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
