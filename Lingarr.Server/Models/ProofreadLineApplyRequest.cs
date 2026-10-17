using Lingarr.Core.Enum;

namespace Lingarr.Server.Models;

public class ProofreadLineApplyRequest
{
    public int Id { get; set; }
    public int Position { get; set; }
    public required string Target { get; set; }
    public ProofreadLineOrigin? Origin { get; set; }
    /// <summary>Provider that produced the suggestion when <see cref="Origin"/> is Proofread.</summary>
    public string? Service { get; set; }
    /// <summary>Model of <see cref="Service"/> that produced the suggestion, when it uses one.</summary>
    public string? Model { get; set; }
}
