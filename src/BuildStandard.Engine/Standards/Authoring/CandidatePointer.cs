using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
namespace BuildStandard.Engine.Standards.Authoring;

/// <summary>Strict object-only RFC6901 paths; arrays are edited as whole values.</summary>
public static class CandidatePointer
{
    public static IReadOnlyList<string> Parse(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 2048 || path[0] != '/' || path.Any(char.IsControl))
            throw Bad("Use a bounded absolute JSON pointer.");
        var parts = path[1..].Split('/');
        if (parts.Length > 48 || parts.Any(p => p.Length == 0)) throw Bad("Empty or excessively deep paths are unsupported.");
        for (var i = 0; i < parts.Length; i++)
        {
            var raw = parts[i];
            for (var j = 0; j < raw.Length; j++)
                if (raw[j] == '~' && (++j >= raw.Length || raw[j] is not ('0' or '1')))
                    throw Bad("Invalid RFC6901 escape.");
            parts[i] = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        }
        return parts;
    }

    public static JsonNode? Read(JsonObject root, string path)
    {
        var (parent, key) = Locate(root, path);
        return parent[key]?.DeepClone();
    }

    public static void Replace(JsonObject root, string path, JsonNode? expected, JsonNode? proposed)
    {
        var (parent, key) = Locate(root, path);
        if (CanonicalJson.Serialize(parent[key]) != CanonicalJson.Serialize(expected))
            throw Bad("The expected original value does not match the pinned baseline.");
        parent[key] = proposed?.DeepClone();
    }

    public static void RequireNonOverlapping(IEnumerable<string> paths)
    {
        var seen = new List<IReadOnlyList<string>>();
        foreach (var path in paths)
        {
            var next = Parse(path);
            foreach (var previous in seen)
                if (Enumerable.Range(0, Math.Min(previous.Count, next.Count))
                    .All(i => string.Equals(previous[i], next[i], StringComparison.OrdinalIgnoreCase)))
                    throw Bad("Duplicate or ancestor/descendant changes must be resolved explicitly.");
            seen.Add(next);
        }
    }

    private static (JsonObject Parent, string Key) Locate(JsonObject root, string path)
    {
        var parts = Parse(path);
        var current = root;
        for (var i = 0; i < parts.Count - 1; i++)
            current = current[parts[i]] as JsonObject ?? throw Bad("The path does not address an existing object; arrays are atomic.");
        if (!current.ContainsKey(parts[^1])) throw Bad("The path does not address an existing property.");
        return (current, parts[^1]);
    }

    private static ConfigurationException Bad(string message) => new("Candidate change: " + message);
}
