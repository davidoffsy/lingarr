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
