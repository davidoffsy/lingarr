using Lingarr.Contracts.Models;
using Lingarr.Core.Entities;
using Lingarr.Server.Models;

namespace Lingarr.Server.Interfaces.Services;

public interface IProgressService
{
    /// <summary>
    /// Emits a progress update for a specific job and updates the database if the job is completed.
    /// </summary>
    /// <param name="translationRequest"></param>
    /// <param name="progress">The current progress of the job (0-100).</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Emit(TranslationRequest translationRequest, int progress);

    /// <summary>
    /// Emits progress with an optional phase (<c>extract</c> / <c>translate</c>) for the UI.
    /// Unknown phases are ignored by older clients.
    /// </summary>
    Task Emit(TranslationRequest translationRequest, int progress, string? phase);

    /// <summary>
    /// Saves a translated line to the database and broadcasts it via SignalR.
    /// </summary>
    /// <param name="request">The translation request the line belongs to.</param>
    /// <param name="position">Position of the line in the subtitle file.</param>
    /// <param name="source">Source text of the line.</param>
    /// <param name="target">Translated text of the line.</param>
    /// <param name="service">Provider that produced the translation.</param>
    /// <param name="pair">Language pair the provider used, when it differs from the requested one.</param>
    /// <param name="model">Model that produced the translation, when the provider uses one.</param>
    Task EmitLine(
        TranslationRequest request,
        int position,
        string source,
        string target,
        string? service = null,
        LanguagePair? pair = null,
        string? model = null);

    /// <summary>
    /// Saves multiple translated lines to the database and broadcasts them via SignalR.
    /// </summary>
    Task EmitLines(TranslationRequest request, List<TranslatedLineData> lines);

    /// <summary>
    /// Applies a proofread revision to the stored line for a position and broadcasts it via SignalR.
    /// When a proofreading service is supplied, the line is attributed to it with the model it used.
    /// </summary>
    Task EmitProofreadLine(
        TranslationRequest request,
        int position,
        string source,
        string target,
        string? service,
        string? model = null);
}