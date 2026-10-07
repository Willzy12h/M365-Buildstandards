using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Workflow;

/// <summary>
/// Structural rules every INT-049 and INT-050 record must meet before it is written. Cross-record rules (same-tenant
/// evidence and deviations, supersession) are checked by <see cref="JobWorkflow"/>, which has the store and the job.
/// </summary>
public static class WorkflowRecordRules
{
    public const int MaxTextLength = 4000;

    public static void ValidateJob(TenantJob job)
    {
        if (job.SchemaVersion != TenantJob.CurrentSchemaVersion) throw new ConfigurationException($"Jobs use schema version {TenantJob.CurrentSchemaVersion}.");
        Guid(job.Id, "Job ID"); Guid(job.TenantId, "Tenant ID"); Guid(job.ProfileId, "Client profile ID");
        if (!JobIntention.All.Contains(job.Intention)) throw new ConfigurationException("Choose why the job exists: new build, legacy backfill or repeat review.");
        Required(job.Actor, "The person opening the job");
        Required(job.Owner, "The job owner");
        Required(job.StandardRelease, "The standard release"); Digest(job.StandardDigest, "The standard digest");
        Digest(job.ClientScopeDigest, "The reviewed client scope digest");
        Time(job.CreatedAt, "The creation time"); Time(job.UpdatedAt, "The update time");
        Text(job.Notes, "Notes");
        if (job.ObservationIds.Any(id => !ProfileValidator.IsGuid(id)) || job.ObservationIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != job.ObservationIds.Count)
            throw new ConfigurationException("A job lists each observation once, by ID.");
        if (job.DispositionIds is { } dispositions
            && (dispositions.Any(id => !ProfileValidator.IsGuid(id)) || dispositions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != dispositions.Count))
            throw new ConfigurationException("A job lists each disposition once, by ID.");
        if (job.CutoverIds is { } cutovers
            && (cutovers.Any(id => !ProfileValidator.IsGuid(id)) || cutovers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != cutovers.Count))
            throw new ConfigurationException("A job lists each cutover revision once, by ID.");
        References(job.Evidence, EvidenceKind.Snapshot);
        if (job.ReviewPlanId is not null) Guid(job.ReviewPlanId, "The review-only plan reference");
    }

    public static void ValidateObservation(TenantObservation o)
    {
        if (o.SchemaVersion != TenantObservation.CurrentSchemaVersion) throw new ConfigurationException($"Observations use schema version {TenantObservation.CurrentSchemaVersion}.");
        Subject(o, "Observation");
        if (!ObservationStatus.All.Contains(o.Status)) throw new ConfigurationException("Record the outcome as Pass, Fail, Unknown or Pending.");
        Required(o.Actor, "The person recording the observation");
        if (ObservationStatus.IsAsserted(o.Status)) Required(o.Reason, "A reason for a Pass or Fail");
        Text(o.Reason, "The reason");
    }

    public static void ValidateDisposition(TenantDisposition d)
    {
        if (d.SchemaVersion != TenantDisposition.CurrentSchemaVersion) throw new ConfigurationException($"Dispositions use schema version {TenantDisposition.CurrentSchemaVersion}.");
        Subject(d, "Disposition");
        if (!DispositionDecision.All.Contains(d.Decision))
            throw new ConfigurationException("Choose a decision: retain external coverage, approved departure, add missing candidate, investigate, manual work or propose replacement.");
        Required(d.Actor, "The person recording the decision");
        Required(d.Owner, "The decision owner");
        Required(d.Reason, "A reason for the decision");
        if (d.Decision == DispositionDecision.RetainExternalCoverage && d.ObservedObjectIds.Count == 0)
            throw new ConfigurationException("Retaining external coverage names the exact external objects, as observed in a capture.");
        if (d.Decision == DispositionDecision.ApprovedDeparture) Required(d.DeviationId, "The approved deviation this departure relies on");
        else if (d.DeviationId is not null) throw new ConfigurationException("Only an approved departure refers to a deviation.");
        Text(d.DeviationId, "The deviation reference");
        if (d.CaseId is not null) Guid(d.CaseId, "The cutover case reference");
    }

    /// <summary>
    /// Structural rules for a cutover revision. Each stage needs the state it claims; the evidence behind it (captures,
    /// runs, deviations) is checked by <see cref="JobWorkflow"/>. Recording a stage approves and performs nothing.
    /// </summary>
    public static void ValidateCutover(CutoverRevision c)
    {
        if (c.SchemaVersion != CutoverRevision.CurrentSchemaVersion) throw new ConfigurationException($"Cutover revisions use schema version {CutoverRevision.CurrentSchemaVersion}.");
        Guid(c.CaseId, "Cutover case ID");
        Binding(c.Id, c.TenantId, c.JobId, c.ProfileId, c.SemanticId, c.ControlId, c.InstanceKey, c.StandardRelease, c.StandardDigest, c.ClientScopeDigest,
            c.RecordedAt, c.ReviewDueAt, c.SupersedesId, "Cutover revision");
        References(c.Evidence, EvidenceKind.Snapshot, EvidenceKind.Run);
        var rank = CutoverStage.Rank(c.Stage);
        if (rank < 0) throw new ConfigurationException("Choose a cutover stage: review, candidate created, pilot reviewed, effectiveness verified, retirement reviewed or closed.");
        Required(c.Actor, "The person recording the revision");
        Required(c.Owner, "The case owner");
        Required(c.Reason, "A reason for the revision");
        Text(c.Overlaps, "Overlaps"); Text(c.RecoveryLimits, "Recovery limits");
        if (c.OldObjectIds.Count == 0) throw new ConfigurationException("A cutover case names the exact old objects it replaces.");
        Distinct(c.OldObjectIds, "old object"); Digest(c.OldMaterialDigest, "The digest of the old objects");
        Distinct(c.NewObjectIds, "new object");
        if (c.OldObjectIds.Intersect(c.NewObjectIds, StringComparer.OrdinalIgnoreCase).Any()) throw new ConfigurationException("An object cannot be both the old and the new protection.");
        Distinct(c.PilotGroupIds, "pilot group");
        foreach (var group in c.PilotGroupIds) Guid(group, "A pilot group (whole-tenant targeting is never a pilot)");
        Approval(c.PilotApproval, "The pilot approval");
        foreach (var p in c.Prerequisites) Required(p.Description, "A prerequisite's description");
        foreach (var criterion in c.Criteria)
        {
            Required(criterion.Description, "A functional criterion's description");
            if (!CriterionResult.All.Contains(criterion.Result)) throw new ConfigurationException("Record each criterion as not run, passed or failed.");
            if (criterion.Result != CriterionResult.NotRun) Required(criterion.Detail, "What was actually observed for a criterion that ran");
            else Text(criterion.Detail, "The criterion detail");
        }
        if (c.Retirement is not null && !RetirementDecision.All.Contains(c.Retirement)) throw new ConfigurationException("Choose to retire the old protection or retain coexistence.");
        Approval(c.RetirementApproval, "The retirement approval");
        Distinct(c.ResidualDeviationIds, "residual deviation");
        if (c.Unsupported is { } escalation)
        {
            Required(escalation.Reason, "Why the scenario is unsupported");
            Required(escalation.Owner, "The escalation owner");
            Required(escalation.Path, "The escalation path");
        }

        // A stage is never claimed without the state it depends on. None of this is approval to act.
        if (rank >= CutoverStage.Rank(CutoverStage.CandidateCreated) && c.NewObjectIds.Count == 0)
            throw new ConfigurationException("A created candidate names the exact new objects a supported run created.");
        if (rank >= CutoverStage.Rank(CutoverStage.PilotReviewed)
            && (c.PilotGroupIds.Count == 0 || c.PilotApproval is null || c.Prerequisites.Count == 0 || c.Prerequisites.Any(p => !p.Met)))
            throw new ConfigurationException("A reviewed pilot needs a separately approved real pilot group and every prerequisite met.");
        if (rank >= CutoverStage.Rank(CutoverStage.EffectivenessVerified) && (c.Criteria.Count == 0 || c.Criteria.Any(x => x.Result != CriterionResult.Passed)))
            throw new ConfigurationException("Effectiveness is verified only when every functional criterion actually ran and passed.");
        if (rank >= CutoverStage.Rank(CutoverStage.RetirementReviewed) && (c.Retirement is null || c.RetirementApproval is null))
            throw new ConfigurationException("Retirement review records the approved decision to retire or to retain coexistence.");
        if (c.Unsupported is not null && rank == CutoverStage.Rank(CutoverStage.Closed))
            throw new ConfigurationException("A case with an unsupported scenario stays open until a revision clears it.");
    }

    private static void Subject(ISubjectRecord r, string what)
    {
        Binding(r.Id, r.TenantId, r.JobId, r.ProfileId, r.SemanticId, r.ControlId, r.InstanceKey, r.StandardRelease, r.StandardDigest, r.ClientScopeDigest,
            r.RecordedAt, r.ReviewDueAt, r.SupersedesId, what);
        References(r.Evidence, EvidenceKind.Snapshot);
        Distinct(r.ObservedObjectIds, "observed object");
        if (r.ObservedObjectIds.Count > 0) Digest(r.MaterialDigest, "The digest of the observed objects");
    }

    private static void Binding(string id, string tenantId, string jobId, string profileId, string semanticId, string controlId, string instanceKey,
        string release, string standardDigest, string scopeDigest, string recordedAt, string reviewDueAt, string? supersedesId, string what)
    {
        Guid(id, what + " ID"); Guid(tenantId, "Tenant ID"); Guid(jobId, "Job ID"); Guid(profileId, "Client profile ID");
        Required(semanticId, "The requirement's semantic identity");
        Required(controlId, "The control ID");
        if (!string.Equals(instanceKey, controlId, StringComparison.OrdinalIgnoreCase)
            && !instanceKey.StartsWith(controlId + "-", StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The instance key must be the control ID or one of its instances.");
        Required(release, "The standard release"); Digest(standardDigest, "The standard digest");
        Digest(scopeDigest, "The reviewed client scope digest");
        Time(recordedAt, "The recorded time");
        Time(reviewDueAt, "The review-due time");
        Timestamps.TryParse(recordedAt, out var recorded); Timestamps.TryParse(reviewDueAt, out var due);
        if (due <= recorded) throw new ConfigurationException("The review-due time must be after the recorded time.");
        if (supersedesId is not null)
        {
            Guid(supersedesId, "The superseded record");
            if (string.Equals(supersedesId, id, StringComparison.OrdinalIgnoreCase)) throw new ConfigurationException("A record cannot supersede itself.");
        }
    }

    private static void Distinct(List<string> values, string what)
    {
        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new ConfigurationException($"List each {what} once.");
        foreach (var value in values) Text(value, "An ID");
    }

    private static void Approval(CutoverApproval? approval, string what)
    {
        if (approval is null) return;
        Required(approval.ApprovedBy, what + "'s approver");
        Required(approval.Reference, what + "'s reference");
    }

    private static void References(IReadOnlyList<EvidenceReference> references, params string[] kinds)
    {
        foreach (var r in references)
        {
            if (!kinds.Contains(r.Kind)) throw new ConfigurationException($"Evidence kind '{r.Kind}' is not supported here.");
            Guid(r.Id, "Evidence ID"); Digest(r.Sha256, "The evidence digest");
        }
        if (references.Select(r => r.Kind + "/" + r.Id.ToLowerInvariant()).Distinct().Count() != references.Count)
            throw new ConfigurationException("Each piece of evidence is referenced once.");
    }

    private static void Guid(string? value, string what)
    {
        if (!ProfileValidator.IsGuid(value)) throw new ConfigurationException($"{what} must be a GUID.");
    }

    private static void Required(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ConfigurationException($"{what} is required.");
        Text(value, what);
    }

    private static void Text(string? value, string what)
    {
        if (value is { Length: > MaxTextLength }) throw new ConfigurationException($"{what} is longer than {MaxTextLength} characters.");
    }

    private static void Digest(string? value, string what)
    {
        if (value is not { Length: 64 } || !value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ConfigurationException($"{what} must be a lower-case SHA-256.");
    }

    private static void Time(string? value, string what)
    {
        if (!Timestamps.TryParse(value, out _)) throw new ConfigurationException($"{what} is not a valid UTC time.");
    }
}
