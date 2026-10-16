using Lingarr.Contracts.Models;
using Lingarr.Server.Interfaces.Services.Translation;

namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// One usable candidate for a concrete language pair: the chain entry, the language pair the
/// provider actually supports, and the position of the entry in the configured chain.
/// </summary>
/// <param name="Entry">Provider instance plus its batch capability and model override.</param>
/// <param name="Pair">Language pair the provider resolved for the requested source and target.</param>
/// <param name="ChainIndex">Zero-based position in the configured chain, used to keep configured order.</param>
public readonly record struct TranslationCandidate(TranslationServiceEntry Entry, LanguagePair Pair, int ChainIndex);
