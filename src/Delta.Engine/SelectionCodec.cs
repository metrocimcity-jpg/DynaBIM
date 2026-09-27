using System.Text.Json;
using Autodesk.DesignScript.Runtime;

namespace Delta.Core;

/// <summary>
/// Encodes selected keys as a JSON string array so free-text service type values
/// can contain commas, quotes, or the old unit-separator character and still
/// round-trip through a DesignScript string literal and the .dyn file.
/// </summary>
[SupressImportIntoVM]
public static class SelectionCodec
{
    public static string Encode(IEnumerable<string> ids)
    {
        return JsonSerializer.Serialize(ids.ToArray());
    }

    public static List<string> Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(trimmed) ?? new List<string>();
            }
            catch (JsonException)
            {
                return new List<string>();
            }
        }

        // Graphs saved by an earlier build used U+001F between keys.
        return trimmed.Split('\u001F', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static HashSet<string> ToSet(string? text)
    {
        return new HashSet<string>(Decode(text), StringComparer.OrdinalIgnoreCase);
    }
}
