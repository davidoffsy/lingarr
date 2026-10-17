using System.Net;
using System.Net.Http;
using Lingarr.Contracts.Exceptions;
using Lingarr.Server.Exceptions;
using Lingarr.Server.Services.Translation;
using Xunit;

namespace Lingarr.Server.Tests.Services.Translation;

public class HttpFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void IsTransient_RetryableStatusCodes(HttpStatusCode statusCode)
    {
        Assert.True(HttpFailure.IsTransient(statusCode));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void IsTransient_NonRetryableStatusCodes(HttpStatusCode statusCode)
    {
        Assert.False(HttpFailure.IsTransient(statusCode));
    }

    [Fact]
    public void FromResponse_BadGateway_IsResponseException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway);
        var exception = HttpFailure.FromResponse("OpenAI", response, "bad gateway");

        var classified = Assert.IsType<TranslationResponseException>(exception);
        Assert.Equal(HttpStatusCode.BadGateway, classified.StatusCode);
    }

    [Fact]
    public void FromResponse_Unauthorized_IsAuthenticationException()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var exception = HttpFailure.FromResponse("OpenAI", response, "nope");

        Assert.IsType<TranslationAuthenticationException>(exception);
    }
}
