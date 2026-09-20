using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

/// <summary>
/// Recognises the pre-preview.9 catalogue representation for read-only reconciliation of historical accepted writes.
/// New approvals and every execution gate continue to require the current digest. Original evidence is never rewritten.
/// </summary>
public static class StandardDigestCompatibility
{
    public static bool MatchesForReadOnlyVerification(StandardCatalogue standard, string recordedDigest)
    {
        if (CanonicalJson.Sha256Value(standard) == recordedDigest) return true;

        // These shipped schema-3 releases predate default/exclusion-role semantics. Do not interpret an arbitrary
        // newer or imported catalogue using the old model, even when it happens to have empty metadata.
        if (standard.SchemaVersion != 3 || standard.Release is not
            ("2026.09.3" or "2026.09.4" or "2026.09.5" or "2026.09.6" or "2026.09.7" or "2026.09.8")) return false;
        var legacy = ToolkitJson.ToNode(standard)!.AsObject();
        foreach (var parameter in legacy["parameters"]!.AsArray().OfType<JsonObject>())
        {
            if (parameter["default"] is not null || !Empty(parameter["reviewedOn"])) return false;
            parameter.Remove("reviewedOn");
        }
        foreach (var control in legacy["controls"]!.AsArray().OfType<JsonObject>())
        {
            if (!Empty(control["exclusionRole"]) || control["prerequisites"] is not null) return false;
            control.Remove("exclusionRole");
        }
        return CanonicalJson.Sha256(legacy) == recordedDigest;
    }

    private static bool Empty(JsonNode? node) => node is null || node is JsonValue value
        && value.TryGetValue<string>(out var text) && text.Length == 0;
}
