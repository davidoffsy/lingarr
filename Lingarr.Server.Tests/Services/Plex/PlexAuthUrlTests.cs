using Lingarr.Server.Services.Plex;
using Xunit;

namespace Lingarr.Server.Tests.Services.Plex;

public class PlexAuthUrlTests
{
    [Fact]
    public void Build_EncodesOfficialHashFragment()
    {
        var url = PlexAuthUrl.Build("client-uuid-1", "ABCD");

        Assert.Equal(
            "https://app.plex.tv/auth#?clientID=client-uuid-1&code=ABCD&context%5Bdevice%5D%5Bproduct%5D=Lingarr",
            url);
    }

    [Fact]
    public void Build_EscapesClientIdAndCode()
    {
        var url = PlexAuthUrl.Build("id with space", "a&b");

        Assert.Contains("clientID=id%20with%20space", url);
        Assert.Contains("code=a%26b", url);
        Assert.StartsWith(PlexAuthUrl.Prefix, url);
    }
}
