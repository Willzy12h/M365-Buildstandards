using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Workflow;

/// <summary>What an engineer asks to record for one requirement instance.</summary>
public sealed class ObservationRequest
{
    public string SemanticId { get; set; } = "";
    public string ControlId { get; set; } = "";
    /// <summary>Defaults to the control ID when the control is not repeated.</summary>
    public string InstanceKey { get; set; } = "";
    public string Status { get; set; } = ObservationStatus.Pending;
    public string Reason { get; set; } = "";
    public DateTimeOffset ReviewDueAt { get; set; }
    /// <summary>The saved capture the outcome was observed in, if any. Its integrity digest is pinned.</summary>
    public string? SnapshotId { get; set; }
    /// <summary>Exact object IDs observed in that capture. Each must be present in it.</summary>
    public List<string> ObservedObjectIds { get; set; } = new();
    /// <summary>The current observation this one revises. Required when the requirement already has one.</summary>
    public string? SupersedesId { get; set; }
}

/// <summary>
/// INT-049 workflow: opens jobs and records observations against them. Nothing here reads or writes the tenant, and a
/// job confers no authority to execute: deployment still needs its own live, fresh, integrity and ownership checks and
/// a newly reviewed approval.
/// </summary>
public sealed class JobWorkflow
{
    private readonly EvidenceStore _store;
    private readonly IClock _clock;

    public JobWorkflow(EvidenceStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public TenantJob OpenJob(TenantProfile profile, StandardCatalogue standard, string intention, string owner, string notes, string actor, string? snapshotId = null)
    {
        RequireVerifiedStandard(standard);
        var now = Timestamps.Format(_clock.UtcNow);
        var job = new TenantJob
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = profile.TenantId.ToLowerInvariant(),
            ProfileId = profile.Id.ToLowerInvariant(),
            ClientScopeDigest = ReviewedClientScope.Digest(profile),
            StandardRelease = standard.Release,
            StandardDigest = standard.IntegrityDigest,
            Intention = intention,
            Owner = owner.Trim(),
            Notes = notes.Trim(),
            Actor = actor.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        if (snapshotId is not null) job.Evidence.Add(SnapshotReference(job.TenantId, snapshotId).Reference);
        _store.CreateJob(job);
        return job;
    }

    /// <summary>
    /// Writes the observation as a new immutable record, then attaches it to its job. If the attachment is
    /// interrupted the observation stays visibly unattached and the job's prior history remains current.
    /// </summary>
    public TenantObservation Record(string tenantId, string jobId, TenantProfile profile, StandardCatalogue standard, ObservationRequest request, string actor)
    {
        RequireVerifiedStandard(standard);
        var job = _store.RequireJob(tenantId, jobId);
        if (!string.Equals(profile.TenantId, job.TenantId, StringComparison.OrdinalIgnoreCase) || !string.Equals(profile.Id, job.ProfileId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("This client profile is not the one the job was opened for.");
        var instance = string.IsNullOrWhiteSpace(request.InstanceKey) ? request.ControlId : request.InstanceKey;
        if (ControlInstances.Find(standard, profile, instance) is null)
            throw new ConfigurationException($"{instance} is not a requirement of standard {standard.Release} for this client.");

        var observation = new TenantObservation
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = job.TenantId,
            JobId = job.Id,
            SemanticId = request.SemanticId.Trim(),
            ControlId = request.ControlId.Trim(),
            InstanceKey = instance.Trim(),
            StandardRelease = standard.Release,
            StandardDigest = standard.IntegrityDigest,
            ProfileId = job.ProfileId,
            ClientScopeDigest = ReviewedClientScope.Digest(profile),
            Status = request.Status,
            Reason = request.Reason.Trim(),
            Actor = actor.Trim(),
            RecordedAt = Timestamps.Format(_clock.UtcNow),
            ReviewDueAt = Timestamps.Format(request.ReviewDueAt),
            ObservedObjectIds = request.ObservedObjectIds.Select(id => id.Trim()).ToList(),
            SupersedesId = request.SupersedesId?.ToLowerInvariant()
        };
        if (request.SnapshotId is not null)
        {
            var (reference, snapshot) = SnapshotReference(job.TenantId, request.SnapshotId);
            observation.Evidence.Add(reference);
            if (observation.ObservedObjectIds.Count > 0)
                observation.MaterialDigest = ObservedMaterial.Digest(snapshot, observation.ObservedObjectIds)
                    ?? throw new ConfigurationException("An observed object is not in the referenced capture. Record only objects the capture contains.");
        }
        else if (observation.ObservedObjectIds.Count > 0)
            throw new ConfigurationException("Observed objects need the capture they were observed in.");

        WorkflowRecordRules.ValidateObservation(observation);
        CheckSupersession(job, observation);
        _store.CreateObservation(observation);

        job.ObservationIds.Add(observation.Id);
        job.UpdatedAt = observation.RecordedAt;
        _store.ReplaceJob(job);
        return observation;
    }

    /// <summary>A subject keeps one line of history: a revision must supersede its current record and nothing else.</summary>
    private void CheckSupersession(TenantJob job, TenantObservation observation)
    {
        var attached = job.ObservationIds.Select(id => _store.LoadObservation(job.TenantId, id)
            ?? throw new ConfigurationException("An attached observation is missing. Review the job before recording more.")).ToList();
        var sameSubject = attached.Where(o => SameSubject(o, observation)).ToList();
        if (observation.SupersedesId is null)
        {
            if (sameSubject.Count > 0) throw new SafetyViolationException("This requirement already has an observation. Record a revision that supersedes the current one.");
            return;
        }
        var predecessor = attached.FirstOrDefault(o => string.Equals(o.Id, observation.SupersedesId, StringComparison.OrdinalIgnoreCase))
            ?? throw new SafetyViolationException("An observation can only supersede an existing record attached to the same job.");
        if (!SameSubject(predecessor, observation))
            throw new SafetyViolationException("An observation can only supersede a record for the same requirement and instance.");
        if (attached.Any(o => string.Equals(o.SupersedesId, predecessor.Id, StringComparison.OrdinalIgnoreCase)))
            throw new SafetyViolationException("That observation has already been revised. Supersede the current record instead.");
    }

    public static bool SameSubject(TenantObservation a, TenantObservation b) =>
        string.Equals(a.SemanticId, b.SemanticId, StringComparison.Ordinal)
        && string.Equals(a.ControlId, b.ControlId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.InstanceKey, b.InstanceKey, StringComparison.OrdinalIgnoreCase);

    private (EvidenceReference Reference, TenantSnapshot Snapshot) SnapshotReference(string tenantId, string snapshotId)
    {
        var snapshot = _store.LoadSnapshot(tenantId, snapshotId) ?? throw new ConfigurationException("The referenced capture is not saved for this tenant.");
        if (!_store.SnapshotIntegrityIntact(snapshot)) throw new IntegrityException("The referenced capture failed its integrity check. Use an intact capture.");
        return (new EvidenceReference { Kind = EvidenceKind.Snapshot, Id = snapshot.Id.ToLowerInvariant(), Sha256 = snapshot.IntegrityDigest }, snapshot);
    }

    private static void RequireVerifiedStandard(StandardCatalogue standard)
    {
        if (string.IsNullOrEmpty(standard.IntegrityDigest))
            throw new IntegrityException("Records bind a verified standard. Load the release through its manifest first.");
    }
}

/// <summary>Digest of exactly the observed objects as one capture recorded them.</summary>
public static class ObservedMaterial
{
    /// <summary>Returns null when any object is absent from the capture.</summary>
    public static string? Digest(TenantSnapshot snapshot, IEnumerable<string> objectIds)
    {
        var found = new JsonArray();
        foreach (var id in objectIds.Select(i => i.ToLowerInvariant()).Distinct().OrderBy(i => i, StringComparer.Ordinal))
        {
            var match = snapshot.Collections.OrderBy(c => c.Key, StringComparer.Ordinal)
                .SelectMany(c => c.Value.Items.Select(item => (c.Key, item)))
                .FirstOrDefault(x => x.item["id"] is JsonValue value && value.TryGetValue<string>(out var text)
                    && string.Equals(text, id, StringComparison.OrdinalIgnoreCase));
            if (match.item is null) return null;
            found.Add(new JsonObject { ["collection"] = match.Key, ["object"] = match.item.DeepClone() });
        }
        return CanonicalJson.Sha256(found);
    }
}
