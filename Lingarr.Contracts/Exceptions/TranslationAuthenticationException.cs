namespace Lingarr.Contracts.Exceptions;

/// <summary>
/// Raised by a translation provider when the request was rejected because the configured
/// credentials are not accepted (typically HTTP 401). The translation chain treats it as a
/// provider-wide failure: retrying another model of the same provider cannot succeed, so the
/// remaining candidates of that provider are skipped and the chain continues with the next
/// provider.
/// </summary>
public class TranslationAuthenticationException : TranslationException
{
    public TranslationAuthenticationException(string message, Exception? exception = null)
        : base(message, exception)
    {
    }
}
