using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Naming;

/// <summary>Projection over supplied evidence only. No collection, rename or evidence mutation entry point.</summary>
public static class NamingAudit
{
    public const string Managed = "Toolkit-managed: creation and captured identity corroborated";
    public const string Unmapped = "No toolkit mapping for this object";
    public const string OwnershipUnknown = "Toolkit ownership unable to confirm";
    public sealed record ObjectResult(string Collection, string ObjectId, string Name, string Ownership,
        string OwnershipReason, NamingConvention.Check Naming);
    public sealed record CollectionResult(string Collection, string ReadState, int ReturnedObjects, string Detail);
    public sealed record Result(string TenantId, string CaptureId, string CapturedAt, string SourceIntegrity,
        IReadOnlyList<CollectionResult> Collections, IReadOnlyList<ObjectResult> Objects);

    public static Result Review(TenantSnapshot capture, ManagedObjectMappings mappings, IReadOnlyList<DeploymentRun> runs)
    {
        if (capture.Collections is null || mappings.ByControl is null || mappings.ByControl.Values.Any(m => m is null)
            || runs.Any(r => r is null || r.Results is null))
            throw new ConfigurationException("Naming audit evidence has missing collections, mappings or run results.");
        if (!Guid.TryParse(capture.TenantId, out _) || !Guid.TryParse(capture.Id, out _) || !Timestamps.TryParse(capture.CapturedAt, out var captureTime))
            throw new ConfigurationException("Naming audit requires the original capture identity and time.");
        if (!string.Equals(capture.TenantId, mappings.TenantId, StringComparison.OrdinalIgnoreCase)
            || runs.Any(r => !string.Equals(r.TenantId, capture.TenantId, StringComparison.OrdinalIgnoreCase)))
            throw new TenantMismatchException("Naming ownership evidence belongs to another tenant.");
        var integrity = AssessmentEngine.IntegrityOf(capture);
        if (integrity == SnapshotIntegrityState.Modified) throw new IntegrityException("Naming audit refuses a capture changed after hashing.");
        var objects = new List<ObjectResult>(); var collections = new List<CollectionResult>();
        foreach (var key in NamingConvention.Collections.Order(StringComparer.Ordinal))
        {
            if (!capture.Collections.TryGetValue(key, out var c))
            { collections.Add(new(key, CaptureStatus.NotAttempted, 0, "No capture for this object type; not an empty successful read.")); continue; }
            if (c is null || c.Items is null || c.Items.Any(i => i is null) || c.Count != c.Items.Count)
                throw new ConfigurationException("Naming capture has invalid object counts or missing objects for " + key + ".");
            if (c.Status != CaptureStatus.Collected)
            { collections.Add(new(key, c.Status, 0, c.Error ?? "Objects could not be checked.")); continue; }
            collections.Add(new(key, c.DetailIncomplete || !string.IsNullOrEmpty(c.Error) ? "Partial" : CaptureStatus.Collected, c.Items.Count,
                c.DetailIncomplete || !string.IsNullOrEmpty(c.Error) ? "Returned names only; collection details are incomplete. " + c.Error
                    : c.Items.Count == 0 ? "Checked successfully; no objects returned." : "Names checked from this capture; no live reads."));
            var duplicateIds = c.Items.Select(o => Text(o, "id")).Where(id => id is not null)
                .GroupBy(id => id!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in c.Items)
            {
                var id = Text(item, "id");
                var name = Text(item, "displayName") ?? Text(item, "name");
                var naming = NamingConvention.CheckName(name, key);
                var ownership = Unmapped; var reason = "No mapping matches both this collection and exact object ID. A name cannot establish ownership.";
                if (string.IsNullOrWhiteSpace(id) || duplicateIds.Contains(id))
                {
                    ownership = OwnershipUnknown; reason = "The capture has a missing or duplicate object ID; identity cannot be established.";
                    naming = naming with { State = NamingConvention.Unknown, Reason = reason };
                }
                else
                {
                    var mapped = mappings.ByControl.Values.Where(m => m.Collection == key && string.Equals(m.ObjectId, id, StringComparison.OrdinalIgnoreCase)).ToList();
                    // Overlapping collections read the same Graph objects, so a mapping recorded under one of them names this object too.
                    var overlapping = NamingConvention.OverlappingCollections(key);
                    var mappedElsewhere = mappings.ByControl.Values.Where(m => m.Collection is not null && overlapping.Contains(m.Collection)
                        && string.Equals(m.ObjectId, id, StringComparison.OrdinalIgnoreCase)).Select(m => m.Collection!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    if (mapped.Count > 0)
                    {
                        ownership = OwnershipUnknown; reason = "Mapping alone is insufficient: valid original creation/run/readback evidence at or before this capture is required.";
                        if (mappedElsewhere.Count > 0)
                            reason = "This object ID is also mapped under the overlapping collection " + string.Join(", ", mappedElsewhere) + ", which reads the same Graph objects; the mapping history is ambiguous.";
                        else if (mapped.Count == 1 && integrity == SnapshotIntegrityState.Intact && Corroborates(mapped[0], runs, captureTime))
                        { ownership = Managed; reason = "Verified recorded creation, matching payload/readback and exact captured collection/object identity; no claim of current live ownership or effective policy."; }
                    }
                    else if (mappedElsewhere.Count > 0)
                    {
                        ownership = OwnershipUnknown;
                        reason = "Mapped under " + string.Join(", ", mappedElsewhere) + ", an overlapping collection that reads the same Graph objects; ownership is assessed there, not under " + key + ".";
                    }
                }
                objects.Add(new(key, id ?? "", name ?? "", ownership, reason, naming));
            }
        }
        return new(capture.TenantId, capture.Id, capture.CapturedAt, integrity, collections, objects);
    }

    private static bool Corroborates(ManagedObjectMapping mapping, IReadOnlyList<DeploymentRun> runs, DateTimeOffset captureTime)
    {
        if (mapping.LastApplied is null || mapping.LastAppliedDigest != CanonicalJson.Sha256(mapping.LastApplied)) return false;
        var candidates = runs.Where(r => r.Id == mapping.RunId).ToList();
        if (candidates.Count != 1 || !EvidenceIntegrity.Verify(candidates[0], candidates[0].IntegrityDigest)) return false;
        var results = candidates[0].Results.Where(r => r.ControlId == mapping.ControlId && r.Collection == mapping.Collection
            && string.Equals(r.ObjectId, mapping.ObjectId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (results.Count != 1) return false;
        var result = results[0];
        return result.PlannedAction == "Create" && result.WriteAcceptance == WriteAcceptance.Accepted
            && result.Configuration == ConfigurationVerification.Pass && result.WrittenPayload is not null && result.AfterObject is not null
            && result.PayloadDigest == mapping.LastAppliedDigest && result.PayloadDigest == CanonicalJson.Sha256(result.WrittenPayload)
            && result.ReadbackDigest == CanonicalJson.Sha256(result.AfterObject)
            && string.Equals(Text(result.AfterObject, "id"), mapping.ObjectId, StringComparison.OrdinalIgnoreCase)
            && CanonicalJson.IsSubset(result.AfterObject, mapping.LastApplied)
            && Timestamps.TryParse(result.WrittenAt, out var writtenAt) && writtenAt <= captureTime;
    }

    private static string? Text(JsonObject item, string key) => item[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
