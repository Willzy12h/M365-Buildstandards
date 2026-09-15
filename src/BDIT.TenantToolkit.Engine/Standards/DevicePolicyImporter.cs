using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Standards;

public sealed record DevicePolicyImport(StandardCatalogue Standard, string SourceDigest, IReadOnlyList<string> RemovedProperties);

/// <summary>Imports one supported Graph policy as a local candidate catalogue. Does not save files or call Graph.</summary>
public static class DevicePolicyImporter
{
    private static readonly string[] SupportedControls = { "CFG-WIN-002", "SEC-WIN-001", "SEC-WIN-002", "SEC-WIN-003" };
    private static readonly string[] Metadata = { "id", "createdDateTime", "lastModifiedDateTime", "version", "@odata.context", "@odata.etag",
        "assignments", "groupAssignments", "_assignments", "isAssigned", "roleScopeTagIds", "supportsScopeTags", "description" };

    public static DevicePolicyImport Import(StandardCatalogue baseline, string controlId, string json, string candidateName)
    {
        if (!SupportedControls.Contains(controlId, StringComparer.Ordinal)) throw new ConfigurationException("Import supports the reviewed LAPS, Defender Antivirus, firewall and Defender EDR controls only.");
        if (Encoding.UTF8.GetByteCount(json) > 256 * 1024) throw new ConfigurationException("Policy import exceeds 256 KiB.");
        if (string.IsNullOrWhiteSpace(candidateName) || candidateName.Length > 200 || candidateName.Any(char.IsControl) || candidateName.Contains("{{", StringComparison.Ordinal))
            throw new ConfigurationException("Provide a candidate name of 1 to 200 characters without control characters or template expressions.");
        JsonObject source;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            AssertUniqueKeys(document.RootElement);
            source = JsonNode.Parse(json) as JsonObject ?? throw new ConfigurationException("Import exactly one Graph policy object, not a list or script.");
        }
        catch (JsonException) { throw new ConfigurationException("Policy import is not valid JSON (maximum depth 32)."); }
        var control = baseline.FindControl(controlId) ?? throw new ConfigurationException("Control is missing from the loaded standard.");
        var template = control.Payload ?? throw new ConfigurationException("The loaded standard has no supported recipe for this control.");
        var definition = baseline.FindCollection(control.Collection) ?? throw new ConfigurationException("The control collection is missing.");
        if (definition.BasePath != "/deviceManagement/deviceConfigurations" || !definition.Assignments || !definition.Writable)
            throw new SafetyViolationException("Imports require an assignment-aware device configuration collection.");
        if (source["@odata.type"]?.ToString() != template["@odata.type"]?.ToString())
            throw new ConfigurationException("Export type differs from the supported recipe. Settings Catalogue and endpoint-security template exports require a separate adapter.");
        var removed = new List<string>();
        foreach (var key in source.Select(p => p.Key).ToList())
            if (Metadata.Contains(key, StringComparer.Ordinal)) { source.Remove(key); removed.Add(key); }
        source["displayName"] = candidateName.Trim();
        // Descriptions can contain tenant-specific identifiers. Retain the reviewed generic description instead.
        if (template["description"] is not null) source["description"] = template["description"]!.DeepClone();
        ValidateShape(source, template);
        WritePayloadGuard.Assert(definition, source);
        if (controlId == "SEC-WIN-002" && source["advancedThreatProtectionAutoPopulateOnboardingBlob"]?.ToString() != "true")
            throw new SafetyViolationException("EDR imports must obtain onboarding data from the target tenant. Supplied onboarding/offboarding blobs are not imported.");
        var catalogue = ToolkitJson.Deserialize<StandardCatalogue>(ToolkitJson.Serialize(baseline));
        catalogue.Release = "import";
        catalogue.Status = "Candidate";
        var imported = catalogue.FindControl(controlId)!;
        imported.Payload = source;
        imported.DocumentationNotes = "Imported settings require engineer review. Source export SHA-256: " + CanonicalJson.Sha256Hex(json)
            + ". Export assignments, object IDs and role scope tags are not reused. " + control.DocumentationNotes;
        catalogue.IntegrityDigest = "";
        catalogue.Release = "import-" + CanonicalJson.Sha256Value(catalogue)[..24];
        StandardsLoader.Validate(catalogue);
        return new DevicePolicyImport(catalogue, CanonicalJson.Sha256Hex(json), removed.AsReadOnly());
    }

    private static void AssertUniqueKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ConfigurationException("Duplicate JSON property in policy import.");
                AssertUniqueKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) AssertUniqueKeys(item);
    }

    private static void ValidateShape(JsonObject source, JsonObject template)
    {
        if (source.Count != template.Count || source.Any(p => !template.ContainsKey(p.Key)))
            throw new ConfigurationException("Export contains missing or unsupported settings. Import only the supported recipe shape; no settings are silently discarded.");
        foreach (var (key, expected) in template)
        {
            if (key == "omaSettings")
            {
                if (source[key] is not JsonArray settings || expected is not JsonArray known || settings.Count != known.Count)
                    throw new ConfigurationException("Import must contain every supported OMA setting exactly once.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in settings)
                {
                    if (item is not JsonObject setting || setting["omaUri"] is not JsonValue uri || !uri.TryGetValue<string>(out var path) || !seen.Add(path))
                        throw new ConfigurationException("Invalid or duplicate OMA setting URI.");
                    var match = known.OfType<JsonObject>().SingleOrDefault(x => x["omaUri"]?.ToString() == path)
                        ?? throw new ConfigurationException("Export contains an unsupported OMA setting URI.");
                    ValidateShape(setting, match);
                }
                continue;
            }
            var value = source[key];
            if (expected is not JsonValue || value is not JsonValue || expected.GetValueKind() != value.GetValueKind())
                throw new ConfigurationException("Imported setting has an unsupported value type or a missing value.");
            if (key is "@odata.type" or "omaUri" && value.ToJsonString() != expected.ToJsonString())
                throw new ConfigurationException("Imported Graph types and OMA routes must match the supported recipe.");
            if (value.GetValueKind() == JsonValueKind.Number && !value.AsValue().TryGetValue<int>(out _))
                throw new ConfigurationException("OMA integer settings require a 32-bit integer.");
            if (value.GetValueKind() == JsonValueKind.String && value.GetValue<string>().Contains("{{", StringComparison.Ordinal))
                throw new ConfigurationException("Import cannot introduce template expressions.");
        }
    }
}
