using Lingarr.Contracts.Translation;

namespace Lingarr.Server.Interfaces.Services.Translation;

/// <summary>
/// One runnable candidate of a translation chain: the provider instance plus the batch capability
/// and model override it was built with.
/// </summary>
/// <param name="Name">Provider identifier as built (lower case), for example <c>custom</c>.</param>
/// <param name="Service">Provider instance dedicated to this candidate.</param>
/// <param name="BatchService">Batch capability of <paramref name="Service"/>, when it has one.</param>
/// <param name="Model">
/// Model override this candidate was built with, or <c>null</c> when the provider's configured
/// default model applies. The effective model of a provider is
/// <see cref="ITranslationService.ModelName"/> once it has been initialised.
/// </param>
public readonly record struct TranslationServiceEntry(
    string Name,
    ITranslationService Service,
    IBatchTranslationService? BatchService,
    string? Model = null)
{
    /// <summary>
    /// Human readable source of this candidate, e.g. <c>custom (model-a)</c>. Returns an empty string
    /// for a default value (no provider), so log statements and status responses never print "null"
    /// and never have to null-check.
    /// </summary>
    public string Describe() => Name is null
        ? string.Empty
        : Model is null
            ? Name
            : $"{Name} ({Model})";
}
