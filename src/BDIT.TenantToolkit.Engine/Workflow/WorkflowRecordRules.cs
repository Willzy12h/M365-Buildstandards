using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Workflow;

/// <summary>
/// Structural rules every INT-049 record must meet before it is written. Cross-record rules (same-tenant evidence,
/// supersession) are checked by <see cref="JobWorkflow"/>, which has the store and the job.
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
        References(job.Evidence);
        if (job.ReviewPlanId is not null) Guid(job.ReviewPlanId, "The review-only plan reference");
    }

    public static void ValidateObservation(TenantObservation o)
    {
        if (o.SchemaVersion != TenantObservation.CurrentSchemaVersion) throw new ConfigurationException($"Observations use schema version {TenantObservation.CurrentSchemaVersion}.");
        Guid(o.Id, "Observation ID"); Guid(o.TenantId, "Tenant ID"); Guid(o.JobId, "Job ID"); Guid(o.ProfileId, "Client profile ID");
        Required(o.SemanticId, "The requirement's semantic identity");
        Required(o.ControlId, "The control ID");
        if (!string.Equals(o.InstanceKey, o.ControlId, StringComparison.OrdinalIgnoreCase)
            && !o.InstanceKey.StartsWith(o.ControlId + "-", StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("The instance key must be the control ID or one of its instances.");
        Required(o.StandardRelease, "The standard release"); Digest(o.StandardDigest, "The standard digest");
        Digest(o.ClientScopeDigest, "The reviewed client scope digest");
        if (!ObservationStatus.All.Contains(o.Status)) throw new ConfigurationException("Record the outcome as Pass, Fail, Unknown or Pending.");
        Required(o.Actor, "The person recording the observation");
        if (ObservationStatus.IsAsserted(o.Status)) Required(o.Reason, "A reason for a Pass or Fail");
        Text(o.Reason, "The reason");
        Time(o.RecordedAt, "The recorded time");
        Time(o.ReviewDueAt, "The review-due time");
        Timestamps.TryParse(o.RecordedAt, out var recorded); Timestamps.TryParse(o.ReviewDueAt, out var due);
        if (due <= recorded) throw new ConfigurationException("The review-due time must be after the recorded time.");
        References(o.Evidence);
        if (o.ObservedObjectIds.Any(string.IsNullOrWhiteSpace) || o.ObservedObjectIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != o.ObservedObjectIds.Count)
            throw new ConfigurationException("List each observed object once.");
        if (o.ObservedObjectIds.Count > 0) Digest(o.MaterialDigest, "The digest of the observed objects");
        if (o.SupersedesId is not null)
        {
            Guid(o.SupersedesId, "The superseded observation");
            if (string.Equals(o.SupersedesId, o.Id, StringComparison.OrdinalIgnoreCase)) throw new ConfigurationException("An observation cannot supersede itself.");
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
