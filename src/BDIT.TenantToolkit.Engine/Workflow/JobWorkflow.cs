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
    /// <summary>
    /// A stored assessment of the observed capture, under the same standard, cited as evidence. Its capture is pinned with it,
    /// so it may be given without <see cref="SnapshotId"/>; given both, they must agree.
    /// </summary>
    public string? AssessmentId { get; set; }
}

/// <summary>What an engineer decides about one legacy or missing requirement instance (INT-050).</summary>
public sealed class DispositionRequest
{
    public string SemanticId { get; set; } = "";
    public string ControlId { get; set; } = "";
    /// <summary>Defaults to the control ID when the control is not repeated.</summary>
    public string InstanceKey { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset ReviewDueAt { get; set; }
    /// <summary>The saved capture the decision relied on, if any. Its integrity digest is pinned.</summary>
    public string? SnapshotId { get; set; }
    /// <summary>Exact object IDs the decision is about. Each must be present in the capture.</summary>
    public List<string> ObservedObjectIds { get; set; } = new();
    /// <summary>For an approved departure: the existing same-tenant deviation it relies on.</summary>
    public string? DeviationId { get; set; }
    /// <summary>Optional cutover case in the same job, for the same requirement instance.</summary>
    public string? CaseId { get; set; }
    /// <summary>The current disposition this one revises. Required when the requirement already has one.</summary>
    public string? SupersedesId { get; set; }
}

/// <summary>
/// INT-049 and INT-050 workflow: opens jobs and records observations and dispositions against them. Nothing here reads
/// or writes the tenant, creates a managed-object mapping or a deviation, and a job confers no authority to execute:
/// deployment still needs its own live, fresh, integrity and ownership checks and a newly reviewed approval.
/// </summary>
public sealed partial class JobWorkflow
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
        var job = BoundJob(tenantId, jobId, profile, standard);
        var instance = Instance(standard, profile, request.ControlId, request.InstanceKey);
        var observation = new TenantObservation
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = job.TenantId,
            JobId = job.Id,
            SemanticId = request.SemanticId.Trim(),
            ControlId = request.ControlId.Trim(),
            InstanceKey = instance,
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
        var assessment = string.IsNullOrWhiteSpace(request.AssessmentId) ? default
            : AssessmentReference(job.TenantId, request.AssessmentId.Trim(), request.SnapshotId, standard, instance, request.Status);
        observation.MaterialDigest = PinEvidence(job.TenantId, assessment.SnapshotId ?? request.SnapshotId, observation.Evidence, observation.ObservedObjectIds).MaterialDigest;
        if (assessment.Reference is not null) observation.Evidence.Add(assessment.Reference);

        WorkflowRecordRules.ValidateObservation(observation);
        CheckSupersession(Attached(job.ObservationIds, id => _store.LoadObservation(job.TenantId, id)), observation);
        _store.CreateObservation(observation);

        job.ObservationIds.Add(observation.Id);
        job.UpdatedAt = observation.RecordedAt;
        _store.ReplaceJob(job);
        return observation;
    }

    /// <summary>
    /// Writes an INT-050 disposition and attaches it to its job, as <see cref="Record"/> does. Repeating the current,
    /// still-valid decision about unchanged objects writes nothing and returns the existing record. A disposition never
    /// creates a managed-object mapping, a deviation or any permission to change or delete a tenant object.
    /// </summary>
    public TenantDisposition Decide(string tenantId, string jobId, TenantProfile profile, StandardCatalogue standard, DispositionRequest request, string actor)
    {
        var job = BoundJob(tenantId, jobId, profile, standard);
        var instance = Instance(standard, profile, request.ControlId, request.InstanceKey);
        var disposition = new TenantDisposition
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = job.TenantId,
            JobId = job.Id,
            SemanticId = request.SemanticId.Trim(),
            ControlId = request.ControlId.Trim(),
            InstanceKey = instance,
            StandardRelease = standard.Release,
            StandardDigest = standard.IntegrityDigest,
            ProfileId = job.ProfileId,
            ClientScopeDigest = ReviewedClientScope.Digest(profile),
            Decision = request.Decision,
            Owner = request.Owner.Trim(),
            Reason = request.Reason.Trim(),
            Actor = actor.Trim(),
            RecordedAt = Timestamps.Format(_clock.UtcNow),
            ReviewDueAt = Timestamps.Format(request.ReviewDueAt),
            ObservedObjectIds = request.ObservedObjectIds.Select(id => id.Trim()).ToList(),
            DeviationId = string.IsNullOrWhiteSpace(request.DeviationId) ? null : request.DeviationId.Trim(),
            CaseId = request.CaseId?.ToLowerInvariant(),
            SupersedesId = request.SupersedesId?.ToLowerInvariant()
        };
        var (_, capture, material) = PinEvidence(job.TenantId, request.SnapshotId, disposition.Evidence, disposition.ObservedObjectIds);
        disposition.MaterialDigest = material;

        WorkflowRecordRules.ValidateDisposition(disposition);
        if (disposition.DeviationId is not null && SubjectReview.MissingDeviation(_store, job.TenantId, disposition.DeviationId, disposition.ControlId, _clock.UtcNow))
            throw new ConfigurationException("An approved departure needs an existing, in-date approved deviation for the same control in this tenant. Record the deviation first; a disposition never creates one.");
        if (disposition.CaseId is not null && SubjectReview.MissingCase(_store, job.TenantId, disposition))
            throw new ConfigurationException("The cutover case must already exist in this job for the same requirement instance.");

        var attached = Attached(job.DispositionIds ?? new(), id => _store.LoadDisposition(job.TenantId, id));
        var current = attached.Where(d => SameSubject(d, disposition))
            .FirstOrDefault(d => !attached.Any(o => string.Equals(o.SupersedesId, d.Id, StringComparison.OrdinalIgnoreCase)));
        if (current is not null && (disposition.SupersedesId is null || string.Equals(disposition.SupersedesId, current.Id, StringComparison.OrdinalIgnoreCase))
            && SameDecision(current, disposition)
            && SubjectReview.Reasons(_store, job.TenantId, current, standard, profile, _clock.UtcNow, capture).Count == 0)
            return current;

        CheckSupersession(attached, disposition);
        _store.CreateDisposition(disposition);

        (job.DispositionIds ??= new()).Add(disposition.Id);
        job.UpdatedAt = disposition.RecordedAt;
        _store.ReplaceJob(job);
        return disposition;
    }

    private static bool SameDecision(TenantDisposition a, TenantDisposition b) =>
        a.Decision == b.Decision && a.Owner == b.Owner
        && string.Equals(a.DeviationId, b.DeviationId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.CaseId, b.CaseId, StringComparison.OrdinalIgnoreCase)
        && a.ObservedObjectIds.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b.ObservedObjectIds)
        && a.MaterialDigest == b.MaterialDigest;

    private TenantJob BoundJob(string tenantId, string jobId, TenantProfile profile, StandardCatalogue standard)
    {
        RequireVerifiedStandard(standard);
        var job = _store.RequireJob(tenantId, jobId);
        if (!string.Equals(profile.TenantId, job.TenantId, StringComparison.OrdinalIgnoreCase) || !string.Equals(profile.Id, job.ProfileId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("This client profile is not the one the job was opened for.");
        return job;
    }

    private static string Instance(StandardCatalogue standard, TenantProfile profile, string controlId, string instanceKey)
    {
        var instance = (string.IsNullOrWhiteSpace(instanceKey) ? controlId : instanceKey).Trim();
        if (ControlInstances.Find(standard, profile, instance) is null)
            throw new ConfigurationException($"{instance} is not a requirement of standard {standard.Release} for this client.");
        return instance;
    }

    /// <summary>Pins the capture's integrity digest and, when objects are named, the digest of exactly those objects.</summary>
    private (EvidenceReference? Reference, TenantSnapshot? Capture, string MaterialDigest) PinEvidence(string tenantId, string? snapshotId,
        List<EvidenceReference> evidence, List<string> objectIds)
    {
        if (snapshotId is null)
        {
            if (objectIds.Count > 0) throw new ConfigurationException("Observed objects need the capture they were observed in.");
            return (null, null, "");
        }
        var (reference, snapshot) = SnapshotReference(tenantId, snapshotId);
        evidence.Add(reference);
        var material = objectIds.Count == 0 ? ""
            : ObservedMaterial.Digest(snapshot, objectIds) ?? throw new ConfigurationException("An observed object is not in the referenced capture. Record only objects the capture contains.");
        return (reference, snapshot, material);
    }

    /// <summary>
    /// A stored assessment an outcome cites: of an intact capture, under this exact standard, with a finding for this instance.
    /// A pass cannot cite an assessment that found the requirement unmet or could not assess it; the citation would contradict the claim.
    /// </summary>
    private (string? SnapshotId, EvidenceReference? Reference) AssessmentReference(string tenantId, string assessmentId, string? snapshotId,
        StandardCatalogue standard, string instance, string status)
    {
        var stored = _store.LoadAssessment(tenantId, assessmentId) ?? throw new ConfigurationException("The referenced assessment is not saved for this tenant.");
        var assessment = stored.Result;
        if (assessment.Release != standard.Release || !string.Equals(assessment.StandardDigest, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The referenced assessment was made under a different standard. Assess the capture again under this one.");
        if (assessment.SnapshotIntegrity != SnapshotIntegrityState.Intact)
            throw new IntegrityException("The referenced assessment did not confirm that its capture was intact. Cite an assessment of an intact capture.");
        if (snapshotId is not null && !string.Equals(snapshotId, assessment.SnapshotId, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The referenced assessment is of a different capture from the one this outcome pins.");
        var finding = assessment.Findings.FirstOrDefault(f => string.Equals(f.ControlId, instance, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConfigurationException($"The referenced assessment has no finding for {instance}.");
        if (status == ObservationStatus.Pass && finding.Status is not (FindingStatus.Compliant or FindingStatus.RequiresManualReview))
            throw new SafetyViolationException($"The referenced assessment found {instance} {finding.Status}. A pass cannot cite it; record what you observed against a capture, or reassess.");
        return (assessment.SnapshotId, new EvidenceReference { Kind = EvidenceKind.Assessment, Id = assessment.Id.ToLowerInvariant(), Sha256 = stored.Sha256 });
    }

    private static List<T> Attached<T>(IEnumerable<string> ids, Func<string, T?> load) where T : class =>
        ids.Select(id => load(id) ?? throw new ConfigurationException("An attached record is missing. Review the job before recording more.")).ToList();

    /// <summary>A subject keeps one line of history: a revision must supersede its current record and nothing else.</summary>
    private static void CheckSupersession<T>(List<T> attached, T record) where T : ISubjectRecord
    {
        if (attached.Any(o => string.Equals(o.InstanceKey, record.InstanceKey, StringComparison.OrdinalIgnoreCase)
            && !SameSubject(o, record)))
            throw new SafetyViolationException("This requirement instance already has a different identity in this job. Review its history; do not create a second outcome or decision identity.");
        var sameSubject = attached.Where(o => SameSubject(o, record)).ToList();
        if (record.SupersedesId is null)
        {
            if (sameSubject.Count > 0) throw new SafetyViolationException("This requirement already has a record in this job. Record a revision that supersedes the current one.");
            return;
        }
        var predecessor = attached.FirstOrDefault(o => string.Equals(o.Id, record.SupersedesId, StringComparison.OrdinalIgnoreCase))
            ?? throw new SafetyViolationException("A record can only supersede an existing record attached to the same job.");
        if (!SameSubject(predecessor, record))
            throw new SafetyViolationException("A record can only supersede a record for the same requirement and instance.");
        if (attached.Any(o => string.Equals(o.SupersedesId, predecessor.Id, StringComparison.OrdinalIgnoreCase)))
            throw new SafetyViolationException("That record has already been revised. Supersede the current record instead.");
    }

    public static bool SameSubject(ISubjectRecord a, ISubjectRecord b) =>
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
    /// <summary>True when the capture holds an object with this ID in any collection.</summary>
    public static bool Contains(TenantSnapshot snapshot, string objectId) =>
        snapshot.Collections.Values.SelectMany(c => c.Items).Any(item => item["id"] is JsonValue value && value.TryGetValue<string>(out var text)
            && string.Equals(text, objectId, StringComparison.OrdinalIgnoreCase));

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
