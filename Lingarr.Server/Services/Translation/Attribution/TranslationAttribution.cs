using Lingarr.Contracts.Models;

namespace Lingarr.Server.Services.Translation.Attribution;

/// <summary>
/// What a translated position is attributed to: the provider and model that produced it, plus the
/// language pair that was actually used (which differs from the requested one when the provider only
/// supports a variant of the requested codes).
/// </summary>
/// <param name="Source">Provider and model that produced the line.</param>
/// <param name="Pair">Language pair the provider translated with, or null when it is not tracked.</param>
public readonly record struct TranslationAttribution(TranslationSource Source, LanguagePair? Pair);
