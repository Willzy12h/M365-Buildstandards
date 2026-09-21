using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Planning;

/// <summary>
/// Converts what a person types into the node a recipe expects, and back again for display.
///
/// Client inputs used to be entered as one hand-written JSON object, so an engineer had to know the key names, the
/// types and JSON punctuation, and found out about a mistake only when a plan refused to build. Parsing per input
/// means a list can be typed the way people write lists, and a wrong value is explained where it was entered.
/// </summary>
public static class PolicyInputParser
{
    private static readonly char[] Separators = { ',', ';', '\r', '\n', ' ', '\t' };

    /// <summary>
    /// True when <paramref name="text"/> converts to a value of <paramref name="type"/>. Empty text is "not supplied":
    /// it returns false with no problem, because leaving a field blank is a legitimate choice, not an error.
    /// </summary>
    public static bool TryRead(string type, string text, out JsonNode? node, out string problem)
    {
        node = null;
        problem = "";
        text = text.Trim();
        if (text.Length == 0) return false;

        switch (type)
        {
            case "guid":
                if (!ProfileValidator.IsGuid(text)) return Fail(out problem, "Enter one object ID, for example 00000000-0000-0000-0000-000000000000.");
                node = JsonValue.Create(text);
                return true;

            case "guidList":
            {
                // An explicit empty list is different from leaving an optional input unspecified.
                if (text == "[]") { node = new JsonArray(); return true; }
                var ids = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                if (ids.Length == 0) return Fail(out problem, "Enter object IDs, [] for an empty list, or leave the field blank.");
                var bad = ids.FirstOrDefault(id => !ProfileValidator.IsGuid(id));
                if (bad is not null) return Fail(out problem, $"'{bad}' is not an object ID. Separate identifiers with commas or new lines.");
                var array = new JsonArray();
                foreach (var id in ids) array.Add(id);
                node = array;
                return true;
            }

            case "integer":
                if (!int.TryParse(text, out var number)) return Fail(out problem, "Enter a whole number.");
                node = JsonValue.Create(number);
                return true;

            case "boolean":
                if (!bool.TryParse(text, out var flag)) return Fail(out problem, "Enter true or false.");
                node = JsonValue.Create(flag);
                return true;

            case "jsonArray":
                try
                {
                    if (JsonNode.Parse(text) is not JsonArray parsed) return Fail(out problem, "Enter a JSON array, starting with [ and ending with ].");
                    node = parsed;
                    return true;
                }
                catch (JsonException ex) { return Fail(out problem, "This is not valid JSON: " + ex.Message); }

            default:
                node = JsonValue.Create(text);
                return true;
        }
    }

    /// <summary>Renders a stored value the way it is typed, so a saved list comes back as a list and not as JSON.</summary>
    public static string Render(string type, JsonNode? node) => node switch
    {
        null => "",
        JsonArray array when type == "guidList" && array.Count > 0 && array.All(i => i is JsonValue v && v.TryGetValue<string>(out _)) =>
            string.Join(", ", array.Select(i => i!.GetValue<string>())),
        JsonArray array => ToolkitJson.Serialize(array),
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString()
    };

    /// <summary>Merge edits to displayed fields without removing inputs belonging to another catalogue release.</summary>
    public static Dictionary<string, JsonNode?> MergeInputs(IReadOnlyDictionary<string, JsonNode?> stored,
        IReadOnlyDictionary<string, JsonNode?> edits)
    {
        var result = stored.ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.Ordinal);
        foreach (var (key, value) in edits)
        {
            if (value is null) result.Remove(key);
            else result[key] = value.DeepClone();
        }
        return result;
    }

    private static bool Fail(out string problem, string reason) { problem = reason; return false; }
}
