using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Exchange;
namespace BuildStandard.Engine.Reports;

public sealed class ExternalCheckImport
{
    private readonly byte[] _original;
    internal ExternalCheckImport(ExternalCheckResults evidence, byte[] original)
    { Evidence = evidence; _original = original.ToArray(); OriginalSha256 = Convert.ToHexString(SHA256.HashData(_original)).ToLowerInvariant(); }
    public ExternalCheckResults Evidence { get; }
    public string OriginalSha256 { get; }
    public string AssuranceNotice => "Untrusted external source claims. Integrity validates supplied bytes, not authenticity or whether checks ran. These results cannot satisfy native assessment, deployment, recovery or job completion.";
    public byte[] GetOriginalBytes() => _original.ToArray();
}

/// <summary>INT-138 neutral interchange only; no arbitrary vendor JSON, code, file references or network reads.</summary>
public static class ExternalCheckResultsSchema
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    public static ExternalCheckImport ReadFile(string file, string expectedTenantId)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaximumBytes) throw Bad("The source exceeds the 8 MiB limit.");
        using var reader = new BinaryReader(stream);
        return Read(reader.ReadBytes(MaximumBytes + 1), expectedTenantId);
    }
    public static ExternalCheckImport Read(byte[] bytes, string expectedTenantId)
    {
        if (bytes is null || bytes.Length > MaximumBytes) throw Bad("The source exceeds the 8 MiB limit or is missing.");
        var original = bytes.ToArray();
        try
        {
            var json = new UTF8Encoding(false, true).GetString(original);
            if (json.Length > 0 && json[0] == '\uFEFF') json = json[1..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            ExchangeCaptureSchema.RejectDuplicates(document.RootElement, StringComparer.OrdinalIgnoreCase);
            var root = JsonNode.Parse(json) as JsonObject ?? throw Bad("Supply one neutral external-check-results object; vendor formats require deliberate conversion.");
            Fields(root, "kind", "schemaVersion", "toolName", "toolVersion", "sourceRunId", "tenantId", "performedAt", "sourceState", "rows", "integrityDigest");
            if (Text(root, "kind", 200) != "external-check-results" || root["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var number) || number != 1)
                throw Bad("Unsupported kind or version; convert to external-check-results version 1 explicitly.");
            var tenant = Text(root, "tenantId", 200);
            if (!Guid.TryParseExact(tenant, "D", out var claimed) || claimed == Guid.Empty
                || !Guid.TryParseExact(expectedTenantId, "D", out var expected) || expected == Guid.Empty || claimed != expected)
                throw Bad("The claimed tenant does not match the deliberately selected client.");
            var performed = Text(root, "performedAt", 200);
            string[] formats = ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"];
            if (!DateTimeOffset.TryParseExact(performed, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) || at.Offset != TimeSpan.Zero)
                throw Bad("The source performedAt timestamp must be explicit UTC ISO 8601.");
            var state = Text(root, "sourceState", 200);
            if (state is not ("Complete" or "Partial" or "Failed" or "Cancelled" or "Unknown")) throw Bad("Source completeness must be explicit; unknown state values are not success.");
            var rows = root["rows"] as JsonArray ?? throw Bad("Rows are required, including an explicit empty array where supplied.");
            if (rows.Count > 5000) throw Bad("The source exceeds 5,000 rows.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var admitted = new List<ExternalCheckResultRow>();
            foreach (var node in rows)
            {
                var row = node as JsonObject ?? throw Bad("Each source row must be an object.");
                Fields(row, "testId", "outcome", "title", "reason", "reference");
                var id = Text(row, "testId", 200);
                if (!ids.Add(id)) throw Bad("Duplicate source test identities are ambiguous.");
                admitted.Add(new(id, Text(row, "outcome", 200), Text(row, "title", 4096), Optional(row, "reason"), Optional(row, "reference")));
            }
            var tool = Text(root, "toolName", 200); var toolVersion = Text(root, "toolVersion", 200); var run = Text(root, "sourceRunId", 200);
            var digest = Text(root, "integrityDigest", 64);
            if (digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigitLower(c))) throw Bad("A full lowercase SHA-256 integrity digest is required.");
            root["integrityDigest"] = "";
            if (CanonicalJson.Sha256(root) != digest) throw Bad("The canonical source digest does not match the supplied content.");
            return new(new("external-check-results", 1, tool, toolVersion, run, tenant, performed, state, admitted.AsReadOnly(), digest), original);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        { throw new ConfigurationException("External results: invalid UTF-8, JSON structure or depth (maximum 16).", ex); }
    }
    private static void Fields(JsonObject node, params string[] allowed)
    {
        if (node.Any(p => !allowed.Contains(p.Key, StringComparer.Ordinal))) throw Bad("Unknown structural fields are refused; executable or vendor-specific content is not imported.");
    }
    private static string Text(JsonObject node, string key, int maximum)
    {
        if (node[key] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)
            || text.Length > maximum || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw Bad($"{key} must be bounded plain text (maximum {maximum} characters).");
        return text;
    }
    private static string? Optional(JsonObject node, string key) => node[key] is null ? null : Text(node, key, 4096);
    private static ConfigurationException Bad(string message) => new("External results: " + message);
}
