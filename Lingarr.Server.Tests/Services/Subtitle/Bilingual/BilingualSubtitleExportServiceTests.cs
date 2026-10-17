using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.FileSystem;
using Lingarr.Server.Services.Subtitle.Bilingual;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle.Bilingual;

public class BilingualSubtitleExportServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly Mock<ISettingService> _settings = new();

    public BilingualSubtitleExportServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "lingarr-bilingual-" + Guid.NewGuid().ToString("N"));
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
    public async Task ExportAsync_SettingOff_DoesNotWriteSidecar()
    {
        _settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.ExportBilingualAss))
            .ReturnsAsync("false");
        var monolingual = Path.Combine(_directory, "Movie.zh.srt");
        await File.WriteAllTextAsync(monolingual, "1\n");

        await CreateService().ExportAsync([Cue(["Hello"], ["你好"])], monolingual);

        Assert.False(File.Exists(Path.Combine(_directory, "Movie.zh.bilingual.ass")));
        Assert.Equal("1\n", await File.ReadAllTextAsync(monolingual));
    }

    [Fact]
    public async Task ExportAsync_MissingSetting_DoesNotWriteSidecar()
    {
        _settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.ExportBilingualAss))
            .ReturnsAsync((string?)null);
        var monolingual = Path.Combine(_directory, "Movie.zh.srt");
        await File.WriteAllTextAsync(monolingual, "kept");

        await CreateService().ExportAsync([Cue(["Hello"], ["你好"])], monolingual);

        Assert.False(File.Exists(Path.Combine(_directory, "Movie.zh.bilingual.ass")));
        Assert.Equal("kept", await File.ReadAllTextAsync(monolingual));
    }

    [Fact]
    public async Task ExportAsync_SettingOn_WritesSidecarNextToMonolingual()
    {
        EnableExport();
        var monolingual = Path.Combine(_directory, "Movie.zh.srt");
        await File.WriteAllTextAsync(monolingual, "monolingual");

        await CreateService().ExportAsync([Cue(["Hello"], ["你好"])], monolingual);

        var sidecar = Path.Combine(_directory, "Movie.zh.bilingual.ass");
        Assert.True(File.Exists(sidecar));
        var body = await File.ReadAllTextAsync(sidecar);
        Assert.Contains("ScriptType: v4.00+", body);
        Assert.Contains("你好\\N{\\rLingarrSource}Hello", body);
        Assert.Equal("monolingual", await File.ReadAllTextAsync(monolingual));
        Assert.False(File.Exists(sidecar + ".tmp"));
    }

    [Fact]
    public async Task ExportAsync_DoesNotMutateInputItems()
    {
        EnableExport();
        var plaintext = new List<string> { "Hello" };
        var translated = new List<string> { "你好" };
        var item = new SubtitleItem
        {
            StartTime = 0,
            EndTime = 1000,
            Lines = ["Hello"],
            PlaintextLines = plaintext,
            TranslatedLines = translated
        };

        await CreateService().ExportAsync([item], Path.Combine(_directory, "Movie.zh.srt"));

        Assert.Same(plaintext, item.PlaintextLines);
        Assert.Same(translated, item.TranslatedLines);
    }

    [Fact]
    public async Task TryRegenerateAsync_AlignsByPosition()
    {
        EnableExport();
        var source = new SubtitleItem
        {
            Position = 1,
            StartTime = 1000,
            EndTime = 4000,
            Lines = ["Hello"],
            PlaintextLines = ["Hello"]
        };
        var target = new SubtitleItem
        {
            Position = 1,
            StartTime = 1000,
            EndTime = 4000,
            Lines = ["旧译文"],
            PlaintextLines = ["旧译文"],
            TranslatedLines = ["新译文"]
        };
        var monolingual = Path.Combine(_directory, "Movie.zh.srt");
        await File.WriteAllTextAsync(monolingual, "target");

        await CreateService().TryRegenerateAsync([source], [target], monolingual);

        var body = await File.ReadAllTextAsync(Path.Combine(_directory, "Movie.zh.bilingual.ass"));
        Assert.Contains("新译文\\N{\\rLingarrSource}Hello", body);
        Assert.Equal(["Hello"], source.PlaintextLines);
        Assert.Equal(["新译文"], target.TranslatedLines);
    }

    [Fact]
    public async Task TryRegenerateAsync_NoPositionOverlap_SkipsWithoutWriting()
    {
        EnableExport();
        var source = new SubtitleItem { Position = 1, PlaintextLines = ["Hello"], Lines = ["Hello"] };
        var target = new SubtitleItem { Position = 99, Lines = ["你好"], TranslatedLines = ["你好"] };
        var monolingual = Path.Combine(_directory, "Movie.zh.srt");
        await File.WriteAllTextAsync(monolingual, "target");

        await CreateService().TryRegenerateAsync([source], [target], monolingual);

        Assert.False(File.Exists(Path.Combine(_directory, "Movie.zh.bilingual.ass")));
        Assert.Equal("target", await File.ReadAllTextAsync(monolingual));
    }

    private void EnableExport()
    {
        _settings
            .Setup(service => service.GetSetting(SettingKeys.Translation.ExportBilingualAss))
            .ReturnsAsync("true");
    }

    private BilingualSubtitleExportService CreateService() =>
        new(_settings.Object, NullLogger<BilingualSubtitleExportService>.Instance);

    private static SubtitleItem Cue(List<string> source, List<string> target) => new()
    {
        StartTime = 1000,
        EndTime = 4000,
        Lines = [..source],
        PlaintextLines = [..source],
        TranslatedLines = [..target]
    };
}
