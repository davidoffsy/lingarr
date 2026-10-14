using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Plex;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexSubtitleImporterTests : IDisposable
{
    private readonly Mock<ISettingService> _settings = new();
    private readonly Mock<IPlexApiClient> _client = new();
    private readonly PlexSubtitleImporter _importer;
    private readonly string _tempDir;

    public PlexSubtitleImporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lingarr-plex-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settings.Setup(s => s.GetSetting(SettingKeys.Integration.PlexUrl))
            .ReturnsAsync("http://plex:32400");
        _settings.Setup(s => s.GetEncryptedSetting(SettingKeys.Integration.PlexToken))
            .ReturnsAsync("token");
        _importer = new PlexSubtitleImporter(
            _settings.Object,
            _client.Object,
            new LanguageCodeService(),
            NullLogger<PlexSubtitleImporter>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ListAsync_OpenSubtitlesSrt_MapsEnglishAndSkipsPgs()
    {
        _client.Setup(c => c.GetMetadata("http://plex:32400", "token", "77015", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexMetadata
            {
                SubtitleStreams =
                [
                    new PlexSubtitleStream
                    {
                        Id = 1,
                        StreamType = 3,
                        Key = "/library/streams/101",
                        Codec = "srt",
                        Format = "srt",
                        LanguageTag = "en",
                        LanguageCode = "eng",
                        Title = "OpenSubtitles"
                    },
                    new PlexSubtitleStream
                    {
                        Id = 2,
                        StreamType = 3,
                        Key = "/library/streams/102",
                        Codec = "pgs",
                        LanguageTag = "en"
                    }
                ]
            });

        var candidates = await _importer.ListAsync("77015", MediaType.Movie);

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.IsPlexStream);
        Assert.Equal("en", candidate.Language);
        Assert.Equal(".srt", candidate.Format);
        Assert.Equal("/library/streams/101", candidate.StreamKey);
        Assert.False(candidate.IsForced);
        Assert.False(candidate.IsHearingImpaired);
    }

    [Fact]
    public async Task ListAsync_LanguageCodeEngWithoutTag_NormalizesToEn()
    {
        _client.Setup(c => c.GetMetadata("http://plex:32400", "token", "1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlexMetadata
            {
                SubtitleStreams =
                [
                    new PlexSubtitleStream
                    {
                        Id = 9,
                        StreamType = 3,
                        Key = "/library/streams/9",
                        Codec = "srt",
                        LanguageCode = "eng"
                    }
                ]
            });

        var candidates = await _importer.ListAsync("1", MediaType.Movie);

        Assert.Equal("en", Assert.Single(candidates).Language);
    }

    [Fact]
    public async Task ListAsync_MissingRatingKey_DoesNotCallPlex()
    {
        var candidates = await _importer.ListAsync(null, MediaType.Movie);

        Assert.Empty(candidates);
        _client.Verify(
            c => c.GetMetadata(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MaterializeAsync_DownloadsToCanonicalSidecar()
    {
        var bytes = "1\n00:00:01,000 --> 00:00:02,000\nHi\n"u8.ToArray();
        _client.Setup(c => c.DownloadStream(
                "http://plex:32400",
                "token",
                "/library/streams/101",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var path = await _importer.MaterializeAsync(
            PlexCandidate(),
            _tempDir,
            "Buddy (2026) WEBRip-1080p");

        var expected = Path.Combine(_tempDir, "Buddy (2026) WEBRip-1080p.en.srt");
        Assert.Equal(expected, path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(expected));
    }

    [Fact]
    public async Task MaterializeAsync_ExistingSidecar_SkipsDownload()
    {
        var expected = Path.Combine(_tempDir, "Buddy (2026) WEBRip-1080p.en.srt");
        await File.WriteAllTextAsync(expected, "already here");

        var path = await _importer.MaterializeAsync(
            PlexCandidate(),
            _tempDir,
            "Buddy (2026) WEBRip-1080p");

        Assert.Equal(expected, path);
        _client.Verify(
            c => c.DownloadStream(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MaterializeAsync_HearingImpaired_UsesSdhFileName()
    {
        var bytes = "1\n00:00:01,000 --> 00:00:02,000\nHi\n"u8.ToArray();
        _client.Setup(c => c.DownloadStream(
                "http://plex:32400",
                "token",
                "/library/streams/101",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var path = await _importer.MaterializeAsync(
            new PlexSourceSubtitleCandidate
            {
                Path = string.Empty,
                Language = "en",
                Caption = "sdh",
                Format = ".srt",
                IsPlexStream = true,
                StreamKey = "/library/streams/101"
            },
            _tempDir,
            "Foo");

        var expected = Path.Combine(_tempDir, "Foo.en.sdh.srt");
        Assert.Equal(expected, path);
        Assert.True(File.Exists(expected));
    }

    private static PlexSourceSubtitleCandidate PlexCandidate() => new()
    {
        Path = string.Empty,
        Language = "en",
        Format = ".srt",
        IsPlexStream = true,
        StreamKey = "/library/streams/101"
    };
}
