using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Standards;

/// <summary>Imports candidate settings only. Foreign object references must be explicitly replaced, never adopted.</summary>
public static class PolicyImporter
{
    private static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
    { "id", "@odata.context", "@odata.etag", "createdDateTime", "lastModifiedDateTime", "version", "assignments", "groupAssignments",
      "targetAssignments", "_assignments", "_settings", "_toolkitAssignments", "roleScopeTagIds", "isAssigned", "settingCount", "creationSource",
      "supportsScopeTags", "uploadState", "publishingState", "dependentAppCount", "supersedingAppCount", "supersededAppCount", "description" };
    public static DevicePolicyImport Import(StandardCatalogue baseline, string controlId, string json, string name, IReadOnlyDictionary<string, string>? replacements = null)
    {
        if (Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new ConfigurationException("Import exceeds 1 MiB.");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.Any(char.IsControl) || name.Contains("{{", StringComparison.Ordinal))
            throw new ConfigurationException("Provide a candidate name of 1 to 200 plain-text characters.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 48 });
        Unique(document.RootElement);
        var obj = JsonNode.Parse(json) as JsonObject ?? throw new ConfigurationException("Import one policy object.");
        var control = baseline.FindControl(controlId) ?? throw new ConfigurationException("Unknown control.");
        var def = baseline.FindCollection(control.Collection) ?? throw new ConfigurationException("No collection for this control.");
        var template = control.Payload ?? throw new ConfigurationException("This control needs a dedicated setup or package workflow.");
        // Some products are unavailable through Intune's Store source. Keep the same control
        // and ownership workflow while requiring an explicit, complete Win32 metadata export.
        if (controlId is "APP-WIN-003" or "APP-WIN-004" or "APP-WIN-005" or "APP-WIN-006"
            && obj["@odata.type"]?.ToString() == "#microsoft.graph.win32LobApp")
            template = baseline.FindControl("APP-WIN-007")?.Payload
                ?? throw new ConfigurationException("This standard has no supported Win32 recipe.");
        if (!def.Writable || !def.Assignments || ConditionalAccessSafety.IsConditionalAccess(def))
            throw new ConfigurationException("This importer supports Intune candidates with assignment-aware collection only.");
        if (obj["@odata.type"]?.ToString() != template["@odata.type"]?.ToString())
            throw new ConfigurationException("Import type must match the selected control. Choose the matching Graph export.");
        var removed = new List<string>();
        foreach (var key in obj.Select(p => p.Key).ToArray())
            if (Metadata.Contains(key)) { obj.Remove(key); removed.Add(key); }
        obj[def.NameProperty] = name.Trim();
        obj["description"] = "M365 BuildStandard imported candidate; explicit review required.";
        if (def.BasePath == "/deviceManagement/configurationPolicies")
        {
            if (obj["settings"] is not JsonArray settings) throw new ConfigurationException("Include the separate settings collection in the export.");
            foreach (var setting in settings.OfType<JsonObject>()) { setting.Remove("id"); setting.Remove("settingDefinitions"); }
            obj["templateReference"] = new JsonObject { ["templateId"] = "" };
        }
        var allowed = template.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        if (obj.Any(p => !allowed.Contains(p.Key))) throw new ConfigurationException("Export contains unsupported properties. No unknown configuration is silently discarded.");
        if (template.Any(p => !obj.ContainsKey(p.Key))) throw new ConfigurationException("Export is missing required recipe properties.");
        ReplaceReferences(obj, replacements ?? new Dictionary<string, string>());
        WritePayloadGuard.Assert(def, obj);
        var candidate = ToolkitJson.Deserialize<StandardCatalogue>(ToolkitJson.Serialize(baseline));
        candidate.Release = "import"; candidate.Status = "Candidate";
        var imported = candidate.FindControl(controlId)!;
        imported.Payload = obj;
        imported.DocumentationNotes = "Imported Graph settings. Source SHA-256 " + CanonicalJson.Sha256Hex(json) + ". Explicit foreign-reference replacements were required. Review device behaviour before activation.";
        candidate.Release = "import-" + CanonicalJson.Sha256Value(candidate)[..24];
        StandardsLoader.Validate(candidate);
        return new(candidate, CanonicalJson.Sha256Hex(json), removed);
    }
    private static void ReplaceReferences(JsonNode node, IReadOnlyDictionary<string, string> replacements)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToArray())
            {
                if (obj[key] is JsonValue v && v.TryGetValue<string>(out var value)) obj[key] = Replace(value, replacements);
                else if (obj[key] is JsonNode child) ReplaceReferences(child, replacements);
            }
        }
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                if (array[i] is JsonValue v && v.TryGetValue<string>(out var value)) array[i] = Replace(value, replacements);
                else if (array[i] is JsonNode child) ReplaceReferences(child, replacements);
    }
    private static string Replace(string value, IReadOnlyDictionary<string, string> replacements)
    {
        if (value.Contains("{{", StringComparison.Ordinal)) throw new ConfigurationException("Imported template expressions are not allowed.");
        return Regex.Replace(value, @"(?i)(?<![0-9a-f])[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?![0-9a-f])", match =>
        {
            if (!replacements.TryGetValue(match.Value, out var replacement) || !ProfileValidator.IsGuid(replacement))
                throw new ConfigurationException("Foreign object or tenant ID requires an explicit target-tenant replacement: " + match.Value);
            return replacement;
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static void Unique(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in node.EnumerateObject()) { if (!keys.Add(p.Name)) throw new ConfigurationException("Duplicate JSON property."); Unique(p.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var child in node.EnumerateArray()) Unique(child);
    }
}
