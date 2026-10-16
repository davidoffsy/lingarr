namespace Lingarr.Server.Models;

public class TranslationRequestSubtitleLines
{
    public int Position { get; set; }
    public required string Source { get; set; }
    public required string Target { get; set; }
    public string? Service { get; set; }
    /// <summary>Model of <see cref="Service"/> that produced the line, when the provider uses one.</summary>
    public string? Model { get; set; }
}