using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Lingarr.Core.Entities;

public class Statistics : BaseEntity
{
    public long TotalLinesTranslated { get; set; }
    public long TotalFilesTranslated { get; set; }
    public long TotalCharactersTranslated { get; set; }
    public int TotalMovies { get; set; }
    public int TotalEpisodes { get; set; }
    public int TotalSubtitles { get; set; }

    public string TranslationsByMediaTypeJson { get; set; } = "{}";
    public string TranslationsByServiceJson { get; set; } = "{}";
    public string SubtitlesByLanguageJson { get; set; } = "{}";
    public string TranslationsByModelJson { get; set; } = "{}";
    public string? LinesBySourceJson { get; set; } = "{}";

    [NotMapped]
    public Dictionary<string, int> TranslationsByMediaType
    {
        get => JsonSerializer.Deserialize<Dictionary<string, int>>(TranslationsByMediaTypeJson) ?? new();
        set => TranslationsByMediaTypeJson = JsonSerializer.Serialize(value);
    }

    [NotMapped]
    public Dictionary<string, int> TranslationsByService
    {
        get => JsonSerializer.Deserialize<Dictionary<string, int>>(TranslationsByServiceJson) ?? new();
        set => TranslationsByServiceJson = JsonSerializer.Serialize(value);
    }

    [NotMapped]
    public Dictionary<string, int> SubtitlesByLanguage
    {
        get => JsonSerializer.Deserialize<Dictionary<string, int>>(SubtitlesByLanguageJson) ?? new();
        set => SubtitlesByLanguageJson = JsonSerializer.Serialize(value);
    }

    [NotMapped]
    public Dictionary<string, int> TranslationsByModel
    {
        get => JsonSerializer.Deserialize<Dictionary<string, int>>(TranslationsByModelJson) ?? new();
        set => TranslationsByModelJson = JsonSerializer.Serialize(value);
    }

    /// <summary>
    /// Number of translated lines per source, keyed by <c>provider:model</c> (bare provider when it
    /// has no model). Unlike <see cref="TranslationsByService"/> and <see cref="TranslationsByModel"/>,
    /// which count finished files, this counts the lines each source really translated, so a run that
    /// fell back to another model is reflected per source.
    /// </summary>
    [NotMapped]
    public Dictionary<string, int> LinesBySource
    {
        get => string.IsNullOrEmpty(LinesBySourceJson)
            ? new Dictionary<string, int>()
            : JsonSerializer.Deserialize<Dictionary<string, int>>(LinesBySourceJson) ?? new();
        set => LinesBySourceJson = JsonSerializer.Serialize(value);
    }
}