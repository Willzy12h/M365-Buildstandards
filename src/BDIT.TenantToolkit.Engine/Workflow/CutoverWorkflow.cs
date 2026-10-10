using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;

namespace BDIT.TenantToolkit.Engine.Workflow;

/// <summary>The full state an engineer records for one cutover revision (INT-050).</summary>
public sealed class CutoverRequest
{
    /// <summary>Null opens a new case at <see cref="CutoverStage.Review"/>; otherwise the case being revised.</summary>
    public string? CaseId { get; set; }
    /// <summary>The case's current revision. Required when revising a case.</summary>
    public string? SupersedesId { get; set; }
    public string SemanticId { get; set; } = "";
    public string ControlId { get; set; } = "";
    /// <summary>Defaults to the control ID when the control is not repeated.</summary>
    public string InstanceKey { get; set; } = "";
    public string Stage { get; set; } = CutoverStage.Review;
    public string Owner { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset ReviewDueAt { get; set; }
    /// <summary>The saved capture this revision relies on. Required to review, to change the pilot and to close.</summary>
    public string? SnapshotId { get; set; }
    /// <summary>The saved deployment run that created and read back the new objects. Required to record the candidate.</summary>
    public string? RunId { get; set; }
    public List<string> OldObjectIds { get; set; } = new();
    public List<string> NewObjectIds { get; set; } = new();
    public string Overlaps { get; set; } = "";
    public List<string> PilotGroupIds { get; set; } = new();
    public CutoverApproval? PilotApproval { get; set; }
    public List<CutoverPrerequisite> Prerequisites { get; set; } = new();
    public List<CutoverCriterion> Criteria { get; set; } = new();
    public string RecoveryLimits { get; set; } = "";
    public string? Retirement { get; set; }
    public CutoverApproval? RetirementApproval { get; set; }
    public List<string> ResidualDeviationIds { get; set; } = new();
    public CutoverEscalation? Unsupported { get; set; }
}

public sealed partial class JobWorkflow
{
    /// <summary>
    /// Records one cutover revision and attaches it to its job. A stage moves forward one step at a time and only with
    /// the evidence the contract names; nothing is activated, assigned or retired, and a completed API operation never
    /// advances a stage by itself. A case may step back to an earlier stage; a closed case is never reopened.
    /// </summary>
    public CutoverRevision Cutover(string tenantId, string jobId, TenantProfile profile, StandardCatalogue standard, CutoverRequest request, string actor)
    {
        var job = BoundJob(tenantId, jobId, profile, standard);
        var attached = Attached(job.CutoverIds ?? new(), id => _store.LoadCutover(job.TenantId, id));
        CutoverRevision? head = null;
        if (request.CaseId is null)
        {
            if (request.SupersedesId is not null || request.Stage != CutoverStage.Review)
                throw new SafetyViolationException("A new cutover case starts at review.");
        }
        else
        {
            var history = attached.Where(r => string.Equals(r.CaseId, request.CaseId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (history.Count == 0) throw new ConfigurationException("The cutover case is not attached to this job.");
            head = history.SingleOrDefault(r => !history.Any(o => string.Equals(o.SupersedesId, r.Id, StringComparison.OrdinalIgnoreCase)))
                ?? throw new SafetyViolationException("The cutover case has no single current revision. Review its history first.");
            if (!string.Equals(request.SupersedesId, head.Id, StringComparison.OrdinalIgnoreCase))
                throw new SafetyViolationException("A cutover revision must supersede the case's current revision.");
            if (head.Stage == CutoverStage.Closed) throw new SafetyViolationException("A closed cutover case is not reopened. Open a new case.");
        }

        var instance = head?.InstanceKey ?? Instance(standard, profile, request.ControlId, request.InstanceKey);
        if (head is not null && (!string.Equals(request.SemanticId.Trim(), head.SemanticId, StringComparison.Ordinal)
            || !string.Equals(request.ControlId.Trim(), head.ControlId, StringComparison.OrdinalIgnoreCase)
            || !(string.IsNullOrWhiteSpace(request.InstanceKey) ? request.ControlId : request.InstanceKey).Trim().Equals(head.InstanceKey, StringComparison.OrdinalIgnoreCase)))
            throw new SafetyViolationException("A cutover case stays with the requirement instance it was opened for.");
        if (head is not null && ControlInstances.Find(standard, profile, instance) is null)
            throw new ConfigurationException($"{instance} is not a requirement of standard {standard.Release} for this client.");

        var rank = CutoverStage.Rank(request.Stage);
        var headRank = head is null ? -1 : CutoverStage.Rank(head.Stage);
        if (rank > headRank + 1) throw new SafetyViolationException("A cutover case moves forward one stage at a time.");
        if (head is not null && rank > headRank && (head.Unsupported is not null || request.Unsupported is not null))
            throw new SafetyViolationException("The case is stopped on an unsupported scenario. Record a revision that clears it at the current stage first.");

        var revision = new CutoverRevision
        {
            Id = Guid.NewGuid().ToString(),
            CaseId = head?.CaseId ?? Guid.NewGuid().ToString(),
            TenantId = job.TenantId,
            JobId = job.Id,
            SemanticId = head?.SemanticId ?? request.SemanticId.Trim(),
            ControlId = head?.ControlId ?? request.ControlId.Trim(),
            InstanceKey = instance,
            StandardRelease = standard.Release,
            StandardDigest = standard.IntegrityDigest,
            ProfileId = job.ProfileId,
            ClientScopeDigest = ReviewedClientScope.Digest(profile),
            Stage = request.Stage,
            Owner = request.Owner.Trim(),
            Actor = actor.Trim(),
            Reason = request.Reason.Trim(),
            RecordedAt = Timestamps.Format(_clock.UtcNow),
            ReviewDueAt = Timestamps.Format(request.ReviewDueAt),
            OldObjectIds = Ids(request.OldObjectIds),
            NewObjectIds = Ids(request.NewObjectIds),
            Overlaps = request.Overlaps.Trim(),
            PilotGroupIds = Ids(request.PilotGroupIds),
            PilotApproval = request.PilotApproval,
            Prerequisites = request.Prerequisites,
            Criteria = request.Criteria,
            RecoveryLimits = request.RecoveryLimits.Trim(),
            Retirement = request.Retirement,
            RetirementApproval = request.RetirementApproval,
            ResidualDeviationIds = request.ResidualDeviationIds.Select(id => id.Trim()).ToList(),
            Unsupported = request.Unsupported,
            SupersedesId = head?.Id
        };

        TenantSnapshot? capture = null;
        if (request.SnapshotId is not null)
        {
            var (reference, snapshot) = SnapshotReference(job.TenantId, request.SnapshotId);
            revision.Evidence.Add(reference);
            capture = snapshot;
        }

        // Review: the old protection exactly as captured. Later stages keep the reviewed old objects; changing them is a new review.
        if (revision.Stage == CutoverStage.Review)
        {
            if (capture is null) throw new ConfigurationException("Reviewing a cutover needs the capture the old protection was observed in.");
            revision.OldMaterialDigest = ObservedMaterial.Digest(capture, revision.OldObjectIds)
                ?? throw new ConfigurationException("An old object is not in the referenced capture. Record only objects the capture contains.");
        }
        else
        {
            if (!SameSet(revision.OldObjectIds, head!.OldObjectIds))
                throw new SafetyViolationException("The old objects are fixed at review. Step the case back to review to change them.");
            revision.OldMaterialDigest = head.OldMaterialDigest;
        }

        // Candidate: only a saved, intact run that wrote and read back each new object stands for its creation.
        if (rank >= CutoverStage.Rank(CutoverStage.CandidateCreated))
        {
            var newObjectsChanged = headRank < CutoverStage.Rank(CutoverStage.CandidateCreated) || !SameSet(revision.NewObjectIds, head!.NewObjectIds);
            if (request.RunId is not null) revision.Evidence.Add(CandidateRun(job.TenantId, request.RunId, revision.NewObjectIds));
            else if (newObjectsChanged) throw new ConfigurationException("Recording the candidate needs the saved run that created and read back each new object.");
        }
        else if (request.RunId is not null) throw new ConfigurationException("A run is evidence for the candidate stage only.");

        // Pilot: a real population, as groups present in a capture, separately approved.
        if (rank >= CutoverStage.Rank(CutoverStage.PilotReviewed)
            && (headRank < CutoverStage.Rank(CutoverStage.PilotReviewed) || !SameSet(revision.PilotGroupIds, head!.PilotGroupIds)))
        {
            if (capture is null || !capture.Collections.TryGetValue("groups", out var groups) || groups.Status != CaptureStatus.Collected)
                throw new ConfigurationException("Reviewing the pilot needs a capture with collected groups.");
            var only = new TenantSnapshot { Collections = { ["groups"] = groups } };
            if (revision.PilotGroupIds.Any(id => !ObservedMaterial.Contains(only, id)))
                throw new ConfigurationException("Each pilot group must be a group present in the referenced capture.");
        }

        if (revision.Stage == CutoverStage.Closed) CheckClosure(revision, head!, capture);
        foreach (var id in revision.ResidualDeviationIds)
            if (SubjectReview.MissingDeviation(_store, job.TenantId, id, revision.ControlId, _clock.UtcNow))
                throw new ConfigurationException("Each residual exception must be an existing, in-date approved deviation for this control in this tenant.");

        WorkflowRecordRules.ValidateCutover(revision);
        _store.CreateCutover(revision);

        (job.CutoverIds ??= new()).Add(revision.Id);
        job.UpdatedAt = revision.RecordedAt;
        _store.ReplaceJob(job);
        return revision;
    }

    /// <summary>
    /// Closure needs a fresh, complete capture taken after retirement was reviewed. Retirement must be performed and
    /// seen (every old object gone); retained coexistence must still be in place. The new objects must be present.
    /// </summary>
    private static void CheckClosure(CutoverRevision revision, CutoverRevision head, TenantSnapshot? capture)
    {
        if (capture is null || !capture.Complete)
            throw new ConfigurationException("Closing a cutover needs a fresh, complete capture.");
        if (!Timestamps.TryParse(capture.CapturedAt, out var captured) || !Timestamps.TryParse(head.RecordedAt, out var reviewed) || captured <= reviewed)
            throw new ConfigurationException("Closing a cutover needs a capture taken after retirement was reviewed.");
        if (revision.NewObjectIds.Any(id => !ObservedMaterial.Contains(capture, id)))
            throw new ConfigurationException("The new protection is not in the fresh capture. The case stays open.");
        if (revision.Retirement == RetirementDecision.Retire && revision.OldObjectIds.Any(id => ObservedMaterial.Contains(capture, id)))
            throw new ConfigurationException("The old protection is still present. Approval to retire is not retirement; the case stays open until it is gone.");
        if (revision.Retirement == RetirementDecision.RetainCoexistence && revision.OldObjectIds.Any(id => !ObservedMaterial.Contains(capture, id)))
            throw new ConfigurationException("Coexistence was approved but the old protection is gone. Review retirement again.");
    }

    private EvidenceReference CandidateRun(string tenantId, string runId, List<string> newObjectIds)
    {
        var run = _store.LoadRun(tenantId, runId) ?? throw new ConfigurationException("The referenced run is not saved for this tenant.");
        if (!EvidenceIntegrity.Verify(run, run.IntegrityDigest)) throw new IntegrityException("The referenced run failed its integrity check. Use intact evidence.");
        foreach (var id in newObjectIds)
        {
            var result = run.Results.FirstOrDefault(r => string.Equals(r.ObjectId, id, StringComparison.OrdinalIgnoreCase));
            if (result is null || !_store.HasAcceptedWrite(run, result) || result.Configuration != ConfigurationVerification.Pass)
                throw new ConfigurationException("Each new object needs an accepted write in the run and a passing readback.");
        }
        return new EvidenceReference { Kind = EvidenceKind.Run, Id = run.Id.ToLowerInvariant(), Sha256 = run.IntegrityDigest };
    }

    private static List<string> Ids(IEnumerable<string> ids) => ids.Select(id => id.Trim().ToLowerInvariant()).ToList();

    private static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) => a.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(b);
}
