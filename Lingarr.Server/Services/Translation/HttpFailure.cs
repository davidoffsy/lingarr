using System.Net;
using Lingarr.Contracts.Exceptions;
using Lingarr.Server.Exceptions;

namespace Lingarr.Server.Services.Translation;

/// <summary>
/// Classifies non-success HTTP responses of translation providers, so the translation chain can tell
/// "this provider will reject every model" apart from "this request failed, try the next candidate".
/// </summary>
public static class HttpFailure
{
    /// <summary>
    /// Returns the exception to throw for a non-success <paramref name="response"/>:
    /// <see cref="TranslationAuthenticationException"/> when the provider rejected the credentials,
    /// <see cref="TranslationResponseException"/> when the status code is worth retrying, otherwise a
    /// plain <see cref="TranslationException"/>. Every variant carries
    /// <paramref name="generalMessage"/>, so the provider specific error text does not change with the
    /// classification.
    /// </summary>
    /// <param name="provider">Provider label used in the authentication message.</param>
    /// <param name="response">The non-success response.</param>
    /// <param name="generalMessage">
    /// Provider specific message describing the failed request.
    /// </param>
    public static Exception FromResponse(string provider, HttpResponseMessage response, string generalMessage) =>
        IsAuthenticationFailure(response)
            ? AuthenticationFailure(provider, generalMessage)
            : IsTransient(response.StatusCode)
                ? new TranslationResponseException(generalMessage, statusCode: response.StatusCode)
                : new TranslationException(generalMessage);

    /// <summary>True when <paramref name="response"/> means the provider rejected the credentials.</summary>
    public static bool IsAuthenticationFailure(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.Unauthorized;

    /// <summary>
    /// True when the status code means "try again later" rather than "this request cannot succeed":
    /// throttling, request timeouts, and gateway failures.
    /// </summary>
    public static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    /// Exception for a provider that rejected the request as unauthorized, for providers that keep
    /// their own failure type for every other status.
    /// </summary>
    /// <param name="provider">Provider label, used in the message when the caller passes none.</param>
    /// <param name="message">Provider specific error text.</param>
    public static TranslationAuthenticationException AuthenticationFailure(string provider, string? message) =>
        new(string.IsNullOrWhiteSpace(message)
            ? $"{provider} rejected the request as Unauthorized (401); check the configured API key or token."
            : message);
}
