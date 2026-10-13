using Lingarr.Contracts.Exceptions;

namespace Lingarr.Server.Services.Translation.Batch;

/// <summary>
/// The current request returned no usable translations for two or more positions.
/// Callers may still try a sibling half before failing the service.
/// </summary>
public sealed class BatchZeroHitException : TranslationException
{
    public BatchZeroHitException(int count)
        : base($"Batch translation returned no results for {count} positions.")
    {
    }
}
