using Lingarr.Contracts.Exceptions;

namespace Lingarr.Server.Exceptions;

/// <summary>
/// Raised when a provider returned a terminal response that must not be written back as a
/// translation and must not be retried: a refusal, a failed or incomplete payload, a non-final
/// state, an error object, an empty body, or an event stream. Throttling and gateway errors stay
/// on <see cref="TranslationResponseException"/>, which callers retry with backoff.
/// </summary>
public class TranslationUnusableResponseException : TranslationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationUnusableResponseException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="exception">The exception that caused the failure, if any.</param>
    public TranslationUnusableResponseException(string message, Exception? exception = null)
        : base(message, exception)
    {
    }
}
