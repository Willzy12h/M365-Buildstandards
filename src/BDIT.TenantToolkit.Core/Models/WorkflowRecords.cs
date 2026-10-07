namespace BDIT.TenantToolkit.Core.Models;

/// <summary>Why a tenant job exists (INT-049).</summary>
public static class JobIntention
{
    public const string NewBuild = "NewBuild";
    public const string LegacyBackfill = "LegacyBackfill";
    public const string RepeatReview = "RepeatReview";
    public static readonly string[] All = { NewBuild, LegacyBackfill, RepeatReview };
}

/// <summary>The outcome an engineer records in an observation (INT-049).</summary>
public static class ObservationStatus
{
    public const string Pass = "Pass";
    public const string Fail = "Fail";
    public const string Unknown = "Unknown";
    public const string Pending = "Pending";
    public static readonly string[] All = { Pass, Fail, Unknown, Pending };

    /// <summary>Pass and Fail assert an outcome, so they need a named actor and a reason.</summary>
    public static bool IsAsserted(string status) => status is Pass or Fail;
}

/// <summary>Kinds of stored evidence a workflow record may rely on.</summary>
public static class EvidenceKind
{
    /// <summary>A saved capture, pinned by its integrity digest.</summary>
    public const string Snapshot = "snapshot";
    /// <summary>A saved deployment run, pinned by its integrity digest. Cutover cases only.</summary>
    public const string Run = "run";
    public static readonly string[] All = { Snapshot, Run };
}

/// <summary>A piece of stored evidence a record relies on, by kind, ID and the SHA-256 it had when it was relied on.</summary>
public sealed class EvidenceReference
{
    /// <summary>One of <see cref="EvidenceKind.All"/>.</summary>
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

/// <summary>
/// INT-049 tenant job: the engineer's intention for a tenant and the records attached to it. Its state is always
/// projected from current evidence and records. It holds no session, token, acknowledged capture, plan approval or
/// executable request; a plan reference is for review only, and resuming still needs every live check.
/// </summary>
public sealed class TenantJob
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    /// <summary><see cref="ReviewedClientScope.Digest"/> of the client inputs the job was opened against.</summary>
    public string ClientScopeDigest { get; set; } = "";
    public string StandardRelease { get; set; } = "";
    /// <summary>Verified SHA-256 of the catalogue's exact bytes, from standards/manifest.json.</summary>
    public string StandardDigest { get; set; } = "";
    public string Intention { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Actor { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    /// <summary>Observations attached to this job, in the order they were attached.</summary>
    public List<string> ObservationIds { get; set; } = new();
    /// <summary>INT-050 dispositions attached to this job, in the order they were attached. Absent until the first one.</summary>
    public List<string>? DispositionIds { get; set; }
    /// <summary>INT-050 cutover revisions attached to this job, in the order they were attached. Absent until the first one.</summary>
    public List<string>? CutoverIds { get; set; }
    /// <summary>The captures the job was last reviewed against.</summary>
    public List<EvidenceReference> Evidence { get; set; } = new();
    /// <summary>Review-only reference to a plan. Never an approval or a request to execute.</summary>
    public string? ReviewPlanId { get; set; }
    public string IntegrityDigest { get; set; } = "";
}

/// <summary>
/// INT-049 observation: one engineer outcome for one requirement instance, bound to the standard, client inputs and
/// evidence it relied on. Immutable once written; a revision is a new observation that supersedes it.
/// </summary>
public sealed class TenantObservation : ISubjectRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string JobId { get; set; } = "";
    /// <summary>Stable requirement identity across releases (from release lineage where it declares one).</summary>
    public string SemanticId { get; set; } = "";
    public string ControlId { get; set; } = "";
    /// <summary>The engine's instance identifier, for example PRE-008-london; equal to the control ID when not repeated.</summary>
    public string InstanceKey { get; set; } = "";
    public string StandardRelease { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ClientScopeDigest { get; set; } = "";
    /// <summary>One of <see cref="ObservationStatus.All"/>.</summary>
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public string RecordedAt { get; set; } = "";
    public string ReviewDueAt { get; set; } = "";
    public List<EvidenceReference> Evidence { get; set; } = new();
    /// <summary>Exact object IDs this outcome was observed on.</summary>
    public List<string> ObservedObjectIds { get; set; } = new();
    /// <summary>SHA-256 of the observed objects as captured, so a changed object or setting projects review.</summary>
    public string MaterialDigest { get; set; } = "";
    public string? SupersedesId { get; set; }
    public string IntegrityDigest { get; set; } = "";
}

/// <summary>Why a record no longer stands on its own. Shown to engineers as written.</summary>
public static class ReviewReason
{
    public const string StandardChanged = "The standard release or its catalogue bytes changed.";
    public const string ClientInputsChanged = "The client inputs it was reviewed against changed.";
    public const string EvidenceMissing = "Evidence it relied on is missing or no longer matches.";
    public const string MaterialChanged = "The observed objects or settings changed.";
    public const string ReviewOverdue = "Its review date has passed.";
    public const string Forked = "More than one record claims to be current for this requirement.";
    public const string PredecessorMissing = "The record it revises is missing or unreadable.";
    public const string DeviationMissing = "The approved deviation it relies on is missing, for another control, or past its review date.";
    public const string CaseMissing = "The cutover case it refers to is missing from this job.";
}

/// <summary>
/// The bindings INT-049 and INT-050 records share: one requirement instance of one job, the catalogue identity and
/// client inputs it was decided against, the evidence relied on and the record it revises.
/// </summary>
public interface ISubjectRecord : IJobRecord
{
    string TenantId { get; }
    string SemanticId { get; }
    string ControlId { get; }
    string InstanceKey { get; }
    string StandardRelease { get; }
    string StandardDigest { get; }
    string ProfileId { get; }
    string ClientScopeDigest { get; }
    string ReviewDueAt { get; }
    List<EvidenceReference> Evidence { get; }
    List<string> ObservedObjectIds { get; }
    string MaterialDigest { get; }
}

/// <summary>An immutable record attached to a job, linked into a supersession graph.</summary>
public interface IJobRecord
{
    string Id { get; }
    string JobId { get; }
    string RecordedAt { get; }
    string? SupersedesId { get; }
}

/// <summary>What an engineer decided about a legacy or missing requirement (INT-050).</summary>
public static class DispositionDecision
{
    /// <summary>An existing external object covers the requirement. An engineer decision, never a compliant finding or ownership.</summary>
    public const string RetainExternalCoverage = "RetainExternalCoverage";
    /// <summary>Relies on an existing approved deviation for the same control; it never creates one.</summary>
    public const string ApprovedDeparture = "ApprovedDeparture";
    public const string AddMissingCandidate = "AddMissingCandidate";
    public const string Investigate = "Investigate";
    public const string ManualWork = "ManualWork";
    public const string ProposeReplacement = "ProposeReplacement";
    public static readonly string[] All = { RetainExternalCoverage, ApprovedDeparture, AddMissingCandidate, Investigate, ManualWork, ProposeReplacement };

    /// <summary>Decisions that leave no work outstanding while they remain valid. The rest name work still to do.</summary>
    public static bool Settles(string decision) => decision is RetainExternalCoverage or ApprovedDeparture;
}

/// <summary>
/// INT-050 disposition: one engineer decision about one requirement instance, bound like an observation to the
/// standard, client inputs and evidence it relied on. Immutable once written; a revised decision supersedes it. It
/// grants no ownership, mapping or permission to change or delete any tenant object.
/// </summary>
public sealed class TenantDisposition : ISubjectRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string JobId { get; set; } = "";
    public string SemanticId { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string InstanceKey { get; set; } = "";
    public string StandardRelease { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ClientScopeDigest { get; set; } = "";
    /// <summary>One of <see cref="DispositionDecision.All"/>.</summary>
    public string Decision { get; set; } = "";
    /// <summary>Who answers for the decision; distinct from the actor who recorded it.</summary>
    public string Owner { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Actor { get; set; } = "";
    public string RecordedAt { get; set; } = "";
    public string ReviewDueAt { get; set; } = "";
    public List<EvidenceReference> Evidence { get; set; } = new();
    /// <summary>Exact object IDs the decision is about, for example the external policy retained.</summary>
    public List<string> ObservedObjectIds { get; set; } = new();
    public string MaterialDigest { get; set; } = "";
    /// <summary>The same-tenant approved deviation an <see cref="DispositionDecision.ApprovedDeparture"/> relies on.</summary>
    public string? DeviationId { get; set; }
    /// <summary>Optional cutover case for the same requirement instance in the same job.</summary>
    public string? CaseId { get; set; }
    public string? SupersedesId { get; set; }
    public string IntegrityDigest { get; set; } = "";
}
