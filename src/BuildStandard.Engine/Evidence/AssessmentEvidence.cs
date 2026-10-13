using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Evidence;

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

    /// <summary>Compatibility listing. Use LoadAssessmentHistory when presenting history so unreadable files remain visible.</summary>
    public IReadOnlyList<StoredAssessment> LoadAssessments(string tenantId) => LoadAssessmentHistory(tenantId).Assessments;

    /// <summary>Read-only history with explicit failures; it never repairs or removes original evidence.</summary>
    public (IReadOnlyList<StoredAssessment> Assessments, IReadOnlyList<UnreadableRecord> Unreadable) LoadAssessmentHistory(string tenantId)
    {
        var dir = AssessmentsDirectory(tenantId);
        var list = new List<StoredAssessment>();
        var unreadable = new List<UnreadableRecord>();
        if (!Directory.Exists(dir)) return (list, unreadable);
        string[] files;
        try { files = Directory.GetFiles(dir, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            unreadable.Add(new(dir, "Assessment history could not be listed. Check local access and preserve the original evidence."));
            _log.Warn("Evidence", "Assessment history could not be listed.", tenantId);
            return (list, unreadable);
        }
        foreach (var file in files)
        {
            try
            {
                var stored = ReadAssessment(file);
                if (!string.Equals(stored.Result.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    unreadable.Add(new(file, "Stored assessment belongs to another client and cannot be cited here."));
                    _log.Warn("Evidence", "Assessment history contains another client's record.", tenantId);
                }
                else list.Add(stored);
            }
            catch (Exception ex) when (ex is ConfigurationException or IOException or UnauthorizedAccessException)
            {
                unreadable.Add(new(file, "Assessment file could not be read. Preserve it and inspect the original locally."));
                _log.Warn("Evidence", "Assessment history contains an unreadable record.", tenantId);
            }
        }
        return (list.OrderByDescending(a => a.Result.AssessedAt, StringComparer.Ordinal).ToList(), unreadable);
    }

    private static StoredAssessment ReadAssessment(string file)
    {
        try
        {
            var text = File.ReadAllText(file);
            var result = ToolkitJson.Deserialize<AssessmentResult>(text);
            return new StoredAssessment(result, CanonicalJson.Sha256(JsonNode.Parse(text)));
        }
        catch (Exception ex) when (ex is JsonException or ConfigurationException or ArgumentException)
        {
            throw new ConfigurationException($"Stored data is invalid: {Path.GetFileName(file)} ({ex.Message})", ex);
        }
    }
}
