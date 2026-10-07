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
        References(job.Evidence);
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
        if (d.CaseId is not null) throw new ConfigurationException("Cutover cases are not available in this build. Record the decision without a case reference.");
    }

    private static void Subject(ISubjectRecord r, string what)
    {
        Guid(r.Id, what + " ID"); Guid(r.TenantId, "Tenant ID"); Guid(r.JobId, "Job ID"); Guid(r.ProfileId, "Client profile ID");
        Required(r.SemanticId, "The requirement's semantic identity");
        Required(r.ControlId, "The control ID");
        if (!string.Equals(r.InstanceKey, r.ControlId, StringComparison.OrdinalIgnoreCase)
            && !r.InstanceKey.StartsWith(r.ControlId + "-", StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The instance key must be the control ID or one of its instances.");
        Required(r.StandardRelease, "The standard release"); Digest(r.StandardDigest, "The standard digest");
        Digest(r.ClientScopeDigest, "The reviewed client scope digest");
        Time(r.RecordedAt, "The recorded time");
        Time(r.ReviewDueAt, "The review-due time");
        Timestamps.TryParse(r.RecordedAt, out var recorded); Timestamps.TryParse(r.ReviewDueAt, out var due);
        if (due <= recorded) throw new ConfigurationException("The review-due time must be after the recorded time.");
        References(r.Evidence);
        if (r.ObservedObjectIds.Any(string.IsNullOrWhiteSpace) || r.ObservedObjectIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.ObservedObjectIds.Count)
            throw new ConfigurationException("List each observed object once.");
        if (r.ObservedObjectIds.Count > 0) Digest(r.MaterialDigest, "The digest of the observed objects");
        if (r.SupersedesId is not null)
        {
            Guid(r.SupersedesId, "The superseded record");
            if (string.Equals(r.SupersedesId, r.Id, StringComparison.OrdinalIgnoreCase)) throw new ConfigurationException("A record cannot supersede itself.");
        }
    }

    private static void References(IReadOnlyList<EvidenceReference> references)
    {
        foreach (var r in references)
        {
            if (!EvidenceKind.All.Contains(r.Kind)) throw new ConfigurationException($"Evidence kind '{r.Kind}' is not supported.");
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
