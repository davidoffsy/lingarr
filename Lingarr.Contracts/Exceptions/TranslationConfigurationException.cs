namespace Lingarr.Contracts.Exceptions;

/// <summary>
/// Raised by a translation provider when its settings cannot produce a request at all: a required
/// endpoint or API key is missing, or the configured address is unusable. The translation chain treats
/// it as a provider-wide failure, like rejected credentials: the same settings fail for every model
/// of the provider, so the remaining candidates of that provider are skipped instead of being
/// retried once per subtitle line. A missing model on one chain row is not this exception — that only
/// skips the current candidate, so a later row of the same provider can still supply an override.
/// </summary>
public class TranslationConfigurationException : TranslationException
{
    public TranslationConfigurationException(string message, Exception? exception = null)
        : base(message, exception)
    {
    }
}