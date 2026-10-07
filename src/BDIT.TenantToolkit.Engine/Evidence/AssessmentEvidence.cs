using System.Text.Json;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Evidence;

/// <summary>
/// A stored assessment and the SHA-256 of its file as stored (canonical JSON), which is what a workflow record pins.
/// Assessments carry no integrity digest of their own, so any change to the stored file changes this digest.
/// </summary>
public sealed record StoredAssessment(AssessmentResult Result, string Sha256);

public sealed partial class EvidenceStore
{
    /// <summary>Returns null when no assessment with this ID is stored for the tenant.</summary>
    public StoredAssessment? LoadAssessment(string tenantId, string assessmentId)
    {
        var id = SafeId(assessmentId);
        var dir = AssessmentsDirectory(tenantId);
        if (!Directory.Exists(dir)) return null;
        var matches = Directory.EnumerateFiles(dir, $"*-{id}.json").Take(2).ToList();
        if (matches.Count > 1) throw new ConfigurationException("Multiple files use this assessment ID. Reconcile the evidence before relying on it.");
        var file = matches.SingleOrDefault();
        if (file is null) return null;
        var stored = ReadAssessment(file);
        AssertTenant(tenantId, stored.Result.TenantId, "Assessment");
        if (!string.Equals(stored.Result.Id, assessmentId, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The stored assessment ID does not match the requested evidence.");
        return stored;
    }

    /// <summary>The tenant's stored assessments, newest first. A file that cannot be read is logged and left out.</summary>
    public IReadOnlyList<StoredAssessment> LoadAssessments(string tenantId)
    {
        var dir = AssessmentsDirectory(tenantId);
        var list = new List<StoredAssessment>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var stored = ReadAssessment(file);
                if (string.Equals(stored.Result.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) list.Add(stored);
            }
            catch (ConfigurationException ex) { _log.Warn("Evidence", ex.Message, tenantId); }
        }
        return list.OrderByDescending(a => a.Result.AssessedAt, StringComparer.Ordinal).ToList();
    }

    private static StoredAssessment ReadAssessment(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            var result = ToolkitJson.Deserialize<AssessmentResult>(text);
            return new StoredAssessment(result, CanonicalJson.Sha256(JsonNode.Parse(text)));
        }
        catch (Exception ex) when (ex is JsonException or ConfigurationException)
        {
            throw new ConfigurationException($"Stored data is invalid: {Path.GetFileName(file)} ({ex.Message})", ex);
        }
    }
}
