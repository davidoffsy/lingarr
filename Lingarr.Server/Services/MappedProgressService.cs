using Lingarr.Contracts.Models;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models;

namespace Lingarr.Server.Services;

/// <summary>
/// Remaps 0–100 progress into an offset/span window (extract 0–20, translate 20–100).
/// </summary>
public sealed class MappedProgressService : IProgressService
{
    private readonly IProgressService _inner;
    private readonly int _offset;
    private readonly int _span;
    private readonly string? _phase;

    public MappedProgressService(IProgressService inner, int offset, int span, string? phase = null)
    {
        _inner = inner;
        _offset = offset;
        _span = span;
        _phase = phase;
    }

    public Task Emit(TranslationRequest translationRequest, int progress)
        => Emit(translationRequest, progress, _phase);

    public Task Emit(TranslationRequest translationRequest, int progress, string? phase)
    {
        var mapped = _offset + (int)Math.Round(Math.Clamp(progress, 0, 100) * _span / 100.0);
        return _inner.Emit(translationRequest, mapped, phase ?? _phase);
    }

    public Task EmitLine(
        TranslationRequest request,
        int position,
        string source,
        string target,
        string? service = null,
        LanguagePair? pair = null,
        string? model = null)
        => _inner.EmitLine(request, position, source, target, service, pair, model);

    public Task EmitLines(TranslationRequest request, List<TranslatedLineData> lines)
        => _inner.EmitLines(request, lines);

    public Task EmitProofreadLine(
        TranslationRequest request,
        int position,
        string source,
        string target,
        string? service,
        string? model = null)
        => _inner.EmitProofreadLine(request, position, source, target, service, model);
}
