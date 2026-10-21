using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Engine.Exchange;
namespace BuildStandard.Engine.Reports;

/// <summary>Portable engineer-entered references, never assessment, ownership or execution evidence.</summary>
public static class ExternalControlReferenceDocument
{
    public static byte[] Write(ExternalCheckImport source, IReadOnlyList<ExternalControlReference> references)
    {
        if (references is null || references.Count > 5000) throw Bad("At most 5,000 explicit references may be retained.");
        var rows = new JsonArray();
        foreach (var link in references)
        {
            if (link is null) throw Bad("Reference rows cannot be null.");
            rows.Add(new JsonObject { ["sourceSha256"] = link.SourceSha256, ["sourceTestId"] = link.SourceTestId,
                ["standardRelease"] = link.StandardRelease, ["standardSha256"] = link.StandardSha256, ["controlId"] = link.ControlId });
        }
        var root = new JsonObject { ["kind"] = "external-control-references", ["schemaVersion"] = 1,
            ["sourceSha256"] = source.OriginalSha256, ["references"] = rows };
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString()); _ = Read(bytes, source); return bytes;
    }
    public static IReadOnlyList<ExternalControlReference> Read(byte[] bytes, ExternalCheckImport source)
    {
        if (bytes is null || bytes.Length > ExternalCheckResultsSchema.MaximumBytes) throw Bad("Reference document exceeds 8 MiB or is missing.");
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
            using var parsed = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            ExchangeCaptureSchema.RejectDuplicates(parsed.RootElement, StringComparer.OrdinalIgnoreCase);
            var root = JsonNode.Parse(text) as JsonObject ?? throw Bad("Expected one explicit reference document.");
            Fields(root, "kind", "schemaVersion", "sourceSha256", "references");
            if (Text(root, "kind") != "external-control-references" || root["schemaVersion"] is not JsonValue version
                || !version.TryGetValue<int>(out var number) || number != 1) throw Bad("Unsupported reference kind or version.");
            if (Text(root, "sourceSha256") != source.OriginalSha256) throw Bad("Load the exact original external results; references are not rebound to another run.");
            if (root["references"] is not JsonArray rows || rows.Count > 5000) throw Bad("Expected at most 5,000 reference rows.");
            var result = new List<ExternalControlReference>(); var targets = new HashSet<(string, string, string)>();
            var sourceIds = source.Evidence.Rows.Select(row => row.TestId).ToHashSet(StringComparer.Ordinal);
            foreach (var item in rows)
            {
                var row = item as JsonObject ?? throw Bad("Reference rows must be explicit objects.");
                Fields(row, "sourceSha256", "sourceTestId", "standardRelease", "standardSha256", "controlId");
                var link = new ExternalControlReference(Text(row, "sourceSha256"), Text(row, "sourceTestId"),
                    Text(row, "standardRelease"), Text(row, "standardSha256"), Text(row, "controlId"));
                if (link.SourceSha256 != source.OriginalSha256 || !sourceIds.Contains(link.SourceTestId)) throw Bad("Reference source identity is unavailable or changed.");
                if (link.StandardSha256.Length != 64 || link.StandardSha256.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                    throw Bad("Reference catalogue digest must be an exact lowercase SHA-256.");
                if (!targets.Add((link.SourceTestId, link.StandardRelease, link.StandardSha256)))
                    throw Bad("Duplicate or conflicting references for this source test and catalogue are not admitted.");
                result.Add(link);
            }
            return result.AsReadOnly();
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException)
        { throw Bad("Invalid reference JSON or UTF-8. No reference was admitted."); }
    }
    private static string Text(JsonObject value, string key)
    {
        if (value[key] is not JsonValue scalar || !scalar.TryGetValue<string>(out var text)
            || string.IsNullOrWhiteSpace(text) || text.Length > 200 || text.Any(char.IsControl)) throw Bad("Missing or invalid reference field: " + key);
        return text;
    }
    private static void Fields(JsonObject value, params string[] names)
    { if (value.Count != names.Length || value.Any(pair => !names.Contains(pair.Key, StringComparer.Ordinal))) throw Bad("Unsupported or missing reference fields; verdict translations are not accepted."); }
    private static ConfigurationException Bad(string reason) => new(reason);
}
