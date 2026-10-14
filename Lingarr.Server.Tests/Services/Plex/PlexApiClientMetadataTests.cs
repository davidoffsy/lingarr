using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Services.Plex;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexApiClientMetadataTests
{
    [Fact]
    public async Task GetMetadata_ParsesSelectedPartFileAndGuids()
    {
        const string json = """
            {
              "MediaContainer": {
                "Metadata": [
                  {
                    "type": "movie",
                    "title": "Foo",
                    "year": 2021,
                    "Guid": [
                      { "id": "plex://movie/abc" },
                      { "id": "tmdb://99" }
                    ],
                    "Media": [
                      {
                        "selected": false,
                        "Part": [{ "file": "/ignored.mkv" }]
                      },
                      {
                        "selected": true,
                        "Part": [
                          { "selected": false, "file": "/not-this.mkv" },
                          { "selected": true, "file": "/mnt/plex/Foo/Foo.mkv" }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;
        var handler = new StubHandler { Status = HttpStatusCode.OK, Body = json };
        var client = CreateClient(handler);

        var metadata = await client.GetMetadata("http://plex:32400", "sign-in-token", "42");

        Assert.NotNull(metadata);
        Assert.Equal("movie", metadata!.Type);
        Assert.Equal("Foo", metadata.Title);
        Assert.Equal(2021, metadata.Year);
        Assert.Equal("/mnt/plex/Foo/Foo.mkv", metadata.File);
        Assert.Contains("tmdb://99", metadata.Guids);
        Assert.Equal("/library/metadata/42", handler.LastRequest?.RequestUri?.AbsolutePath);
        Assert.Equal("sign-in-token", handler.LastRequest?.Headers.GetValues("X-Plex-Token").First());
        Assert.Contains("application/json", handler.LastRequest?.Headers.GetValues("Accept") ?? []);
        Assert.Empty(metadata.SubtitleStreams);
    }

    [Fact]
    public async Task GetMetadata_ParsesOpenSubtitlesStreamAndIgnoresVideoAudio()
    {
        const string json = """
            {
              "MediaContainer": {
                "Metadata": [
                  {
                    "type": "movie",
                    "title": "Buddy",
                    "Media": [
                      {
                        "selected": true,
                        "Part": [
                          {
                            "selected": true,
                            "file": "/mnt/115pan/movie/Buddy (2026)/Buddy (2026) WEBRip-1080p.mp4",
                            "Stream": [
                              { "id": 1, "streamType": 1, "codec": "h264" },
                              { "id": 2, "streamType": 2, "codec": "aac" },
                              {
                                "id": 7701,
                                "streamType": 3,
                                "codec": "srt",
                                "format": "srt",
                                "language": "English",
                                "languageTag": "en",
                                "languageCode": "eng",
                                "title": "OpenSubtitles",
                                "key": "/library/streams/7701",
                                "forced": false,
                                "hearingImpaired": false
                              }
                            ]
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            }
            """;
        var handler = new StubHandler { Status = HttpStatusCode.OK, Body = json };
        var client = CreateClient(handler);

        var metadata = await client.GetMetadata("http://plex:32400", "token", "77015");

        Assert.NotNull(metadata);
        var stream = Assert.Single(metadata!.SubtitleStreams);
        Assert.Equal(7701, stream.Id);
        Assert.Equal(3, stream.StreamType);
        Assert.Equal("srt", stream.Codec);
        Assert.Equal("en", stream.LanguageTag);
        Assert.Equal("eng", stream.LanguageCode);
        Assert.Equal("OpenSubtitles", stream.Title);
        Assert.Equal("/library/streams/7701", stream.Key);
        Assert.False(stream.Forced);
    }

    [Fact]
    public async Task GetMetadata_GuidStringAndNumericKeys_Parses()
    {
        const string json = """
            {
              "MediaContainer": {
                "Metadata": [
                  {
                    "type": "episode",
                    "title": "Rage",
                    "index": 3,
                    "parentIndex": 23,
                    "grandparentRatingKey": 81189,
                    "grandparentTitle": "NCIS",
                    "guid": "plex://episode/5d776b9a9ab54400214e65b2",
                    "Guid": [
                      { "id": "tvdb://12345" },
                      { "id": "tmdb://678" }
                    ],
                    "Media": {
                      "Part": { "file": "/shows/NCIS/Rage.mkv" }
                    }
                  }
                ]
              }
            }
            """;
        var handler = new StubHandler { Status = HttpStatusCode.OK, Body = json };
        var client = CreateClient(handler);

        var metadata = await client.GetMetadata("http://plex:32400", "token", "99");

        Assert.NotNull(metadata);
        Assert.Equal("episode", metadata!.Type);
        Assert.Equal("Rage", metadata.Title);
        Assert.Equal("NCIS", metadata.GrandparentTitle);
        Assert.Equal("81189", metadata.GrandparentRatingKey);
        Assert.Equal(23, metadata.ParentIndex);
        Assert.Equal(3, metadata.Index);
        Assert.Equal("/shows/NCIS/Rage.mkv", metadata.File);
        Assert.Contains("plex://episode/5d776b9a9ab54400214e65b2", metadata.Guids);
        Assert.Contains("tvdb://12345", metadata.Guids);
        Assert.Contains("tmdb://678", metadata.Guids);
    }

    [Fact]
    public async Task GetMetadata_NotFound_ReturnsNull()
    {
        var handler = new StubHandler { Status = HttpStatusCode.NotFound, Body = "{}" };
        var client = CreateClient(handler);

        var metadata = await client.GetMetadata("http://plex:32400", "token", "missing");

        Assert.Null(metadata);
    }

    [Fact]
    public async Task GetMetadata_Unauthorized_Throws()
    {
        var handler = new StubHandler { Status = HttpStatusCode.Unauthorized, Body = "{}" };
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetMetadata("http://plex:32400", "bad-token", "1"));
    }

    [Fact]
    public async Task DownloadStream_ReturnsBytes()
    {
        var handler = new StubHandler { Status = HttpStatusCode.OK, Body = "1\n00:00:01,000 --> 00:00:02,000\nHi\n" };
        var client = CreateClient(handler);

        var bytes = await client.DownloadStream("http://plex:32400", "token", "/library/streams/7701");

        Assert.NotNull(bytes);
        Assert.Equal(handler.Body, Encoding.UTF8.GetString(bytes!));
        Assert.Equal("/library/streams/7701", handler.LastRequest?.RequestUri?.AbsolutePath);
        Assert.Equal("token", handler.LastRequest?.Headers.GetValues("X-Plex-Token").First());
    }

    [Fact]
    public async Task DownloadStream_UnsafeKey_DoesNotSendRequest()
    {
        var handler = new StubHandler { Status = HttpStatusCode.OK, Body = "nope" };
        var client = CreateClient(handler);

        var bytes = await client.DownloadStream("http://plex:32400", "token", "http://evil.example/x");

        Assert.Null(bytes);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task GetMetadata_Timeout_Throws()
    {
        var handler = new StubHandler { Throw = new TaskCanceledException("timed out") };
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.GetMetadata("http://plex:32400", "token", "1"));
    }

    private static IPlexApiClient CreateClient(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Plex"))
            .Returns(() => new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(10)
            });
        return new PlexApiClient(factory.Object, NullLogger<PlexApiClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "{}";
        public Exception? Throw { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (Throw != null)
            {
                throw Throw;
            }

            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
