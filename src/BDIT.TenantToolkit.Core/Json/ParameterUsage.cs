using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Json;

/// <summary>
/// Which client parameters a payload actually references.
///
/// Five places needed this answer and each worked it out by serialising the payload and searching the text for
/// <c>{{key}}</c>. That was wrong twice over. It could serialise the same payload once per parameter, which on the
/// client-document path meant one full serialisation for every parameter of every control. And searching serialised
/// text asks a different question from the one <see cref="CanonicalJson.Resolve"/> answers: the resolver substitutes
/// only inside string values, so a property *named* <c>{{officeIpRanges}}</c> would be reported as a used parameter
/// and then never resolved.
///
/// This walks the node tree the same way the resolver does, so what is reported as used is exactly what would be
/// substituted. Callers that need several parameters should call <see cref="Keys"/> once and filter the result.
/// </summary>
public static class ParameterUsage
{
    /// <summary>Parameter keys this payload references. Walks once; callers filter the result.</summary>
    public static IReadOnlySet<string> Keys(JsonNode? payload)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        Walk(payload, keys);
        return keys;
    }

    /// <summary>True when the payload references this parameter. Prefer <see cref="Keys"/> when asking about several.</summary>
    public static bool Uses(JsonNode? payload, string key) => Keys(payload).Contains(key);

    /// <summary>Parameter keys referenced by any of these payloads, walked once each.</summary>
    public static IReadOnlySet<string> KeysAcross(IEnumerable<JsonNode?> payloads)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var payload in payloads) Walk(payload, keys);
        return keys;
    }

    private static void Walk(JsonNode? node, HashSet<string> keys)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array) Walk(item, keys);
                break;
            case JsonObject obj:
                // Property names are not resolved, so they are not scanned. Only the values can carry a placeholder.
                foreach (var pair in obj) Walk(pair.Value, keys);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                Scan(text, keys);
                break;
        }
    }

    /// <summary>Mirrors the resolver's embedded-placeholder scan, which also covers a whole-string placeholder.</summary>
    private static void Scan(string text, HashSet<string> keys)
    {
        var i = 0;
        while (i < text.Length)
        {
            var start = text.IndexOf("{{", i, StringComparison.Ordinal);
            if (start < 0) return;
            var end = text.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0) return;
            var key = text[(start + 2)..end];
            // A malformed placeholder is the resolver's error to raise, not this one's to guess at.
            if (CanonicalJson.IsIdentifier(key)) keys.Add(key);
            i = end + 2;
        }
    }
}
