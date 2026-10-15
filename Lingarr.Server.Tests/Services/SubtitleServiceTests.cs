using System;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

public class SubtitleServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly SubtitleService _service = new(
        NullLogger<SubtitleService>.Instance,
        new LanguageCodeService());

    public SubtitleServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lingarr-subtitle-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task GetAllSubtitles_UntaggedFile_IsUnknown()
    {
        Touch("Reporting Live (2026) WEBRip-1080p.srt");

        var subtitles = await _service.GetAllSubtitles(_directory);

        var subtitle = Assert.Single(subtitles);
        Assert.Equal("unknown", subtitle.Language);
        Assert.Equal("Reporting Live (2026) WEBRip-1080p", subtitle.FileName);
    }

    [Fact]
    public async Task GetAllSubtitles_TaggedFile_ExtractsLanguage()
    {
        Touch("Reporting Live (2026) WEBRip-1080p.en.srt");

        var subtitles = await _service.GetAllSubtitles(_directory);

        var subtitle = Assert.Single(subtitles);
        Assert.Equal("en", subtitle.Language);
    }

    [Fact]
    public void CreateFilePath_UntaggedSource_AddsTargetLanguage()
    {
        var original = Path.Combine(_directory, "Reporting Live (2026) WEBRip-1080p.srt");

        var path = _service.CreateFilePath(original, "zh-CN", "");

        Assert.Equal("Reporting Live (2026) WEBRip-1080p.zh-cn.srt", Path.GetFileName(path));
    }

    [Fact]
    public void SelectSourceSubtitle_PrefersConfiguredLanguageOverUntagged()
    {
        var selected = _service.SelectSourceSubtitle(
            [
                Sidecar("Movie.srt", "unknown"),
                Sidecar("Movie.en.srt", "en")
            ],
            ["en"],
            "true");

        Assert.NotNull(selected);
        Assert.Equal("en", selected.SourceLanguage);
        Assert.Equal("Movie.en.srt", Path.GetFileName(selected.Subtitle.Path));
    }

    [Fact]
    public void SelectSourceSubtitle_FallsBackToUntaggedWhenNoConfiguredSourceExists()
    {
        var selected = _service.SelectSourceSubtitle(
            [Sidecar("Reporting Live (2026) WEBRip-1080p.srt", "unknown")],
            ["en"],
            "true");

        Assert.NotNull(selected);
        Assert.Equal("unknown", selected.SourceLanguage);
        Assert.Equal("Reporting Live (2026) WEBRip-1080p.srt", Path.GetFileName(selected.Subtitle.Path));
    }

    [Fact]
    public void SelectSourceSubtitle_ReturnsNullWhenNoSourceOrUntaggedFile()
    {
        var selected = _service.SelectSourceSubtitle(
            [Sidecar("Movie.fr.srt", "fr")],
            ["en"],
            "true");

        Assert.Null(selected);
    }

    private void Touch(string fileName) =>
        File.WriteAllText(Path.Combine(_directory, fileName), "1\n00:00:01,000 --> 00:00:02,000\nHi\n");

    private static Subtitles Sidecar(string fileName, string language) => new()
    {
        Path = $"/movies/{fileName}",
        FileName = Path.GetFileNameWithoutExtension(fileName),
        Language = language,
        Caption = "",
        Format = ".srt"
    };
}
