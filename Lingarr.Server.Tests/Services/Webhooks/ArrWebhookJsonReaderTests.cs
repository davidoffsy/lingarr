using System.IO;
using System.Text;
using System.Threading.Tasks;
using Lingarr.Server.Models.Webhooks;
using Lingarr.Server.Services.Webhooks;
using Xunit;

namespace Lingarr.Server.Tests.Services.Webhooks;

public class ArrWebhookJsonReaderTests
{
    [Fact]
    public async Task Read_EmptyBody_IsOkWithNullPayload()
    {
        var result = await ArrWebhookJsonReader.ReadAsync<RadarrWebhookPayload>(new MemoryStream());

        Assert.True(result.Ok);
        Assert.Null(result.Payload);
    }

    [Fact]
    public async Task Read_ValidJson_DeserializesEventType()
    {
        var json = """{"eventType":"Download","movie":{"id":9,"title":"Movie"}}""";
        var result = await ArrWebhookJsonReader.ReadAsync<RadarrWebhookPayload>(StreamOf(json));

        Assert.True(result.Ok);
        Assert.Equal("Download", result.Payload?.EventType);
        Assert.Equal(9, result.Payload?.Movie?.Id);
    }

    [Fact]
    public async Task Read_InvalidJson_IsInvalid()
    {
        var result = await ArrWebhookJsonReader.ReadAsync<RadarrWebhookPayload>(StreamOf("{not-json"));

        Assert.False(result.Ok);
        Assert.True(result.IsInvalidJson);
    }

    [Fact]
    public async Task Read_TooLarge_IsTooLarge()
    {
        var oversized = new byte[ArrWebhookJsonReader.MaxRequestBytes + 1];
        var result = await ArrWebhookJsonReader.ReadAsync<RadarrWebhookPayload>(new MemoryStream(oversized));

        Assert.False(result.Ok);
        Assert.True(result.IsTooLarge);
    }

    private static MemoryStream StreamOf(string json) => new(Encoding.UTF8.GetBytes(json));
}
