using System.Net;

namespace Lingarr.Server.Exceptions;

/// <summary>
/// Raised when a provider answered with a response that is worth retrying: throttling or a gateway
/// error, or a response that carried no usable content. Retryable by design, so callers keep it apart
/// from <see cref="Lingarr.Contracts.Exceptions.TranslationException"/>, which means "this candidate
/// failed, try the next one".
/// </summary>
public class TranslationResponseException : Exception
{
    /// <summary>HTTP status code that produced the failure, when the failure came from a response.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationResponseException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="exception">Inner exception that caused the failure, if any.</param>
    /// <param name="statusCode">Status code of the retryable response, if any.</param>
    public TranslationResponseException(string message, Exception? exception = null,
        HttpStatusCode? statusCode = null) : base(message, exception)
    {
        StatusCode = statusCode;
    }
}
