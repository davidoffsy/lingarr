using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Writes a translation chain back into the canonical <c>service_type</c> form: a JSON array of
/// objects carrying only the supported fields (<c>provider</c>, optional <c>model</c>).
/// Properties that were present in a hand-written value but are not part of the contract are
/// dropped instead of round-tripped. Entries are written as configured (apart from trimming);
/// call <see cref="TranslationChainValidator"/> first when the value must be known-good.
/// </summary>
public static class TranslationChainSerializer
{
    /// <summary>
    /// Wire shape written to the setting; keep in sync with <see cref="TranslationChainParser"/>.
    /// </summary>
    private sealed record ChainEntryPayload
    {
        [JsonPropertyName("provider")]
        public required string Provider { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }
    }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes <paramref name="entries"/> to the object-array form. A null model override is
    /// omitted rather than written as <c>null</c>.
    /// </summary>
    public static string Serialize(IReadOnlyList<TranslationChainEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var payload = new List<ChainEntryPayload>(entries.Count);
        foreach (var entry in entries)
        {
            payload.Add(new ChainEntryPayload
            {
                Provider = (entry.Provider ?? string.Empty).Trim(),
                Model = entry.ModelKey
            });
        }

        return JsonSerializer.Serialize(payload, WriteOptions);
    }
}