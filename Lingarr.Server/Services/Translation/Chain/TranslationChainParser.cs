using System.Text.Json;

namespace Lingarr.Server.Services.Translation.Chain;

/// <summary>
/// Reads the <c>service_type</c> setting into ordered <see cref="TranslationChainEntry"/> values.
/// Accepted shapes:
/// <list type="bullet">
/// <item>a single provider name, e.g. <c>openai</c> (the pre-array setting value)</item>
/// <item>a JSON array of provider names, e.g. <c>["openai","deepl"]</c></item>
/// <item>a JSON array of objects, e.g. <c>[{"provider":"custom","model":"model-a"}]</c></item>
/// <item>arrays mixing both element shapes</item>
/// <item>a single JSON object or JSON string, treated as a one-entry chain</item>
/// </list>
/// Unknown object properties are ignored while reading; <see cref="TranslationChainSerializer"/>
/// writes back only the supported fields.
/// This module is the read side of the chain contract; <see cref="TranslationChainResolver"/> applies
/// the runtime policy and every production entry point (factory, jobs, controllers, startup
/// normalisation) reads the chain through it.
/// </summary>
public static class TranslationChainParser
{
    /// <summary>
    /// Parses a raw setting value. Never throws; malformed or unusable parts are reported as issues
    /// instead of being replaced by a default provider.
    /// </summary>
    public static TranslationChainParseResult Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TranslationChainParseResult.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed[0] is not ('[' or '{' or '"'))
        {
            // Historical single-value form: the whole value is one provider identifier.
            return ParseSingleProvider(trimmed);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return new TranslationChainParseResult([], [
                new TranslationChainIssue(
                    TranslationChainIssueKind.MalformedJson,
                    -1,
                    "service_type is not valid JSON; the configured chain could not be read.")
            ]);
        }

        using (document)
        {
            var root = document.RootElement;
            // A documented value starting with '[', '{' or '"' can only be an array, an object or a
            // quoted provider name; the last two both describe a one-entry chain.
            return root.ValueKind == JsonValueKind.Array
                ? ParseArray(root)
                : ParseSingleElement(root, 0);
        }
    }

    private static TranslationChainParseResult ParseSingleProvider(string? provider)
    {
        var entries = new List<TranslationChainEntry>(1);
        var issues = new List<TranslationChainIssue>();
        AddEntry(provider, null, 0, entries, issues);
        return new TranslationChainParseResult(entries, issues);
    }

    private static TranslationChainParseResult ParseArray(JsonElement array)
    {
        var entries = new List<TranslationChainEntry>(array.GetArrayLength());
        var issues = new List<TranslationChainIssue>();
        var index = 0;

        foreach (var element in array.EnumerateArray())
        {
            ParseSingleElement(element, index, entries, issues);
            index++;
        }

        return new TranslationChainParseResult(entries, issues);
    }

    private static TranslationChainParseResult ParseSingleElement(JsonElement element, int index)
    {
        var entries = new List<TranslationChainEntry>(1);
        var issues = new List<TranslationChainIssue>();
        ParseSingleElement(element, index, entries, issues);
        return new TranslationChainParseResult(entries, issues);
    }

    private static void ParseSingleElement(
        JsonElement element,
        int index,
        List<TranslationChainEntry> entries,
        List<TranslationChainIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddEntry(element.GetString(), null, index, entries, issues);
                break;
            case JsonValueKind.Object:
                ParseObjectEntry(element, index, entries, issues);
                break;
            default:
                issues.Add(new TranslationChainIssue(
                    TranslationChainIssueKind.InvalidEntry,
                    index,
                    $"Chain entry must be a provider name or an object with a \"provider\" property; found {element.ValueKind}.",
                    element.GetRawText()));
                break;
        }
    }

    private static void ParseObjectEntry(
        JsonElement element,
        int index,
        List<TranslationChainEntry> entries,
        List<TranslationChainIssue> issues)
    {
        if (!element.TryGetProperty("provider", out var providerElement) ||
            providerElement.ValueKind != JsonValueKind.String)
        {
            issues.Add(new TranslationChainIssue(
                TranslationChainIssueKind.InvalidEntry,
                index,
                "Chain entry object must declare a string \"provider\" property.",
                element.GetRawText()));
            return;
        }

        string? model = null;
        if (element.TryGetProperty("model", out var modelElement))
        {
            if (modelElement.ValueKind == JsonValueKind.String)
            {
                model = modelElement.GetString();
            }
            else if (modelElement.ValueKind != JsonValueKind.Null)
            {
                // Keep the entry: an unusable model override degrades to the provider default
                // instead of dropping a provider the user configured.
                issues.Add(new TranslationChainIssue(
                    TranslationChainIssueKind.InvalidModel,
                    index,
                    $"Chain entry \"model\" must be a string; found {modelElement.ValueKind}. The provider default model will be used.",
                    modelElement.GetRawText()));
            }
        }

        AddEntry(providerElement.GetString(), model, index, entries, issues);
    }

    private static void AddEntry(
        string? provider,
        string? model,
        int index,
        List<TranslationChainEntry> entries,
        List<TranslationChainIssue> issues)
    {
        var trimmedProvider = provider?.Trim();
        if (string.IsNullOrEmpty(trimmedProvider))
        {
            issues.Add(new TranslationChainIssue(
                TranslationChainIssueKind.EmptyProvider,
                index,
                "Chain entry has no provider identifier."));
            return;
        }

        var trimmedModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        entries.Add(new TranslationChainEntry(trimmedProvider, trimmedModel));
    }
}