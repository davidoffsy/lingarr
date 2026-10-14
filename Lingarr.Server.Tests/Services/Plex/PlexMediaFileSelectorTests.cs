using Lingarr.Server.Models.Plex;
using Lingarr.Server.Services.Plex;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexMediaFileSelectorTests
{
    [Fact]
    public void SelectFile_PrefersSelectedMediaAndSelectedPart()
    {
        var media = new[]
        {
            new PlexMedia
            {
                Selected = false,
                Part =
                [
                    new PlexPart { Selected = true, File = "/first/unselected-media.mkv" }
                ]
            },
            new PlexMedia
            {
                Selected = true,
                Part =
                [
                    new PlexPart { Selected = false, File = "/selected/unselected-part.mkv" },
                    new PlexPart { Selected = true, File = "/selected/selected-part.mkv" }
                ]
            }
        };

        Assert.Equal("/selected/selected-part.mkv", PlexMediaFileSelector.SelectFile(media));
    }

    [Fact]
    public void SelectFile_WhenNothingSelected_UsesFirstMediaAndFirstPart()
    {
        var media = new[]
        {
            new PlexMedia
            {
                Part =
                [
                    new PlexPart { File = "/first.mkv" },
                    new PlexPart { File = "/second.mkv" }
                ]
            },
            new PlexMedia
            {
                Part = [new PlexPart { File = "/other.mkv" }]
            }
        };

        Assert.Equal("/first.mkv", PlexMediaFileSelector.SelectFile(media));
    }

    [Fact]
    public void SelectSubtitleStreams_PrefersSelectedPartAndFallsBackToAllParts()
    {
        var english = new PlexSubtitleStream { Id = 1, Key = "/library/streams/1", Codec = "srt" };
        var french = new PlexSubtitleStream { Id = 2, Key = "/library/streams/2", Codec = "srt" };
        var media = new[]
        {
            new PlexMedia
            {
                Selected = true,
                Part =
                [
                    new PlexPart { Selected = true, File = "/selected.mp4", Stream = [english] },
                    new PlexPart { Selected = false, File = "/other.mp4", Stream = [french] }
                ]
            }
        };

        var streams = PlexMediaFileSelector.SelectSubtitleStreams(media);
        Assert.Equal(english, Assert.Single(streams));
    }

    [Fact]
    public void SelectFile_EmptyOrMissingParts_ReturnsNull()
    {
        Assert.Null(PlexMediaFileSelector.SelectFile(null));
        Assert.Null(PlexMediaFileSelector.SelectFile([]));
        Assert.Null(PlexMediaFileSelector.SelectFile([new PlexMedia { Part = [] }]));
        Assert.Null(PlexMediaFileSelector.SelectFile([
            new PlexMedia { Part = [new PlexPart { File = "   " }] }
        ]));
    }
}
