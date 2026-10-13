using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core;
using Lingarr.Server.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class GitHubLatestReleaseServiceTests
{
    [Fact]
    public async Task GetLatestVersion_ParsesTagName()
    {
        var handler = Handler(HttpStatusCode.OK, """{"tag_name":"v0.1.0"}""");
        var service = CreateService(handler.Object);

        var version = await service.GetLatestVersion();

        Assert.Equal("0.1.0", version);
        VerifyGithubLatestRelease(handler, Times.Once());
    }

    [Fact]
    public async Task GetLatestVersion_CachesSuccessfulLookup()
    {
        var handler = Handler(HttpStatusCode.OK, """{"tag_name":"v0.1.0"}""");
        var service = CreateService(handler.Object);

        var first = await service.GetLatestVersion();
        var second = await service.GetLatestVersion();

        Assert.Equal("0.1.0", first);
        Assert.Equal("0.1.0", second);
        VerifyGithubLatestRelease(handler, Times.Once());
    }

    [Fact]
    public async Task GetLatestVersion_NotFound_ReturnsNull()
    {
        var handler = Handler(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        var service = CreateService(handler.Object);

        var version = await service.GetLatestVersion();

        Assert.Null(version);
    }

    [Fact]
    public async Task GetLatestVersion_EmptyTag_ReturnsNull()
    {
        var handler = Handler(HttpStatusCode.OK, """{"tag_name":""}""");
        var service = CreateService(handler.Object);

        var version = await service.GetLatestVersion();

        Assert.Null(version);
    }

    private static GitHubLatestReleaseService CreateService(HttpMessageHandler handler)
    {
        return new GitHubLatestReleaseService(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GitHubLatestReleaseService>.Instance);
    }

    private static Mock<HttpMessageHandler> Handler(HttpStatusCode statusCode, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(body)
            });

        return handler;
    }

    private static void VerifyGithubLatestRelease(Mock<HttpMessageHandler> handler, Times times)
    {
        handler.Protected().Verify(
            "SendAsync",
            times,
            ItExpr.Is<HttpRequestMessage>(request =>
                request.Method == HttpMethod.Get
                && request.RequestUri != null
                && request.RequestUri.ToString() ==
                $"https://api.github.com/repos/{LingarrVersion.GitHubRepository}/releases/latest"
                && request.Headers.UserAgent.ToString().Contains(LingarrVersion.Name, StringComparison.Ordinal)),
            ItExpr.IsAny<CancellationToken>());
    }
}
