namespace Lingarr.Server.Models;

/// <summary>
/// Suggestion returned by a single-line proofread, including the candidate that produced it so
/// applying the suggestion can attribute the line to that provider and model.
/// </summary>
public class ProofreadLineResponse
{
    public required string Revision { get; set; }
    public required string Service { get; set; }
    public string? Model { get; set; }
}
