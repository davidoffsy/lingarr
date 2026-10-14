using System.Globalization;
using System.Text.Json;
using Lingarr.Server.Models.Plex;

namespace Lingarr.Server.Services.Plex;

internal static class PlexJsonElement
{
    public static JsonElement GetProperty(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        if (obj.TryGetProperty(name, out var exact))
        {
            return exact;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return default;
    }

    public static bool TryGetExact(JsonElement obj, string name, out JsonElement element)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out element))
        {
            return true;
        }

        element = default;
        return false;
    }

    public static string? GetString(JsonElement obj, string name)
    {
        return FlexibleString(GetProperty(obj, name));
    }

    public static string? GetExactString(JsonElement obj, string name)
    {
        return TryGetExact(obj, name, out var element) ? FlexibleString(element) : null;
    }

    public static bool GetBool(JsonElement obj, string name)
    {
        var element = GetProperty(obj, name);
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var value) && value,
            JsonValueKind.Number => element.TryGetInt64(out var number) && number != 0,
            _ => false
        };
    }

    public static long GetInt64(JsonElement obj, string name)
    {
        var element = GetProperty(obj, name);
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var number) => number,
            JsonValueKind.String when long.TryParse(
                element.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => 0
        };
    }

    public static int? GetInt32(JsonElement obj, string name)
    {
        var element = GetProperty(obj, name);
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                element.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };
    }

    public static JsonElement Clone(JsonElement element)
    {
        return element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? default
            : element.Clone();
    }

    public static IReadOnlyList<string> CollectGuidIds(JsonElement metadata)
    {
        var ids = new List<string>();
        AddGuidValues(ids, GetExactOrDefault(metadata, "guid"));
        AddGuidValues(ids, GetExactOrDefault(metadata, "Guid"));
        return ids;
    }

    public static List<PlexGuid>? ReadGuidList(JsonElement metadata)
    {
        if (!TryGetExact(metadata, "Guid", out var guidElement) &&
            !TryGetExact(metadata, "guid", out guidElement))
        {
            return null;
        }

        var list = new List<PlexGuid>();
        AddGuidObjects(list, guidElement);
        return list;
    }

    public static List<PlexMedia>? ReadMedia(JsonElement metadata)
    {
        var mediaElement = GetProperty(metadata, "Media");
        if (mediaElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var list = new List<PlexMedia>();
        foreach (var item in EnumerateObjectOrArray(mediaElement))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new PlexMedia
            {
                Selected = GetBool(item, "selected"),
                Part = ReadParts(item)
            });
        }

        return list;
    }

    private static List<PlexPart>? ReadParts(JsonElement media)
    {
        var partElement = GetProperty(media, "Part");
        if (partElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var list = new List<PlexPart>();
        foreach (var item in EnumerateObjectOrArray(partElement))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            list.Add(new PlexPart
            {
                Selected = GetBool(item, "selected"),
                File = GetString(item, "file")
            });
        }

        return list;
    }

    private static IEnumerable<JsonElement> EnumerateObjectOrArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    private static JsonElement GetExactOrDefault(JsonElement obj, string name)
    {
        return TryGetExact(obj, name, out var element) ? element : default;
    }

    private static void AddGuidValues(List<string> ids, JsonElement element)
    {
        foreach (var id in EnumerateGuidIds(element))
        {
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(id);
            }
        }
    }

    private static void AddGuidObjects(List<PlexGuid> list, JsonElement element)
    {
        foreach (var id in EnumerateGuidIds(element))
        {
            list.Add(new PlexGuid { Id = id });
        }
    }

    private static IEnumerable<string> EnumerateGuidIds(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
            {
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }

                break;
            }
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var id in EnumerateGuidIds(item))
                    {
                        yield return id;
                    }
                }

                break;
            case JsonValueKind.Object:
            {
                var id = GetString(element, "id");
                if (!string.IsNullOrWhiteSpace(id))
                {
                    yield return id;
                }

                break;
            }
        }
    }

    private static string? FlexibleString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
