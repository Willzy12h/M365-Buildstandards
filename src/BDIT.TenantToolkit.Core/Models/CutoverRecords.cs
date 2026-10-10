namespace BDIT.TenantToolkit.Core.Models;

/// <summary>The functional stages of a cutover case (INT-050). Recording a stage never performs or approves anything.</summary>
public static class CutoverStage
{
    public const string Review = "Review";
    public const string CandidateCreated = "CandidateCreated";
    public const string PilotReviewed = "PilotReviewed";
    public const string EffectivenessVerified = "EffectivenessVerified";
    public const string RetirementReviewed = "RetirementReviewed";
    public const string Closed = "Closed";
    public static readonly string[] Order = { Review, CandidateCreated, PilotReviewed, EffectivenessVerified, RetirementReviewed, Closed };

    public static int Rank(string stage) => Array.IndexOf(Order, stage);
}

/// <summary>What happens to the old protection once the replacement is verified.</summary>
public static class RetirementDecision
{
    /// <summary>The old objects are retired outside the toolkit; closure needs a fresh capture showing them gone.</summary>
    public const string Retire = "Retire";
    /// <summary>The old and new protection deliberately stay side by side.</summary>
    public const string RetainCoexistence = "RetainCoexistence";
    public static readonly string[] All = { Retire, RetainCoexistence };
}

public static class CriterionResult
{
    public const string NotRun = "NotRun";
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public static readonly string[] All = { NotRun, Passed, Failed };
}

/// <summary>A prerequisite the pilot depends on, and whether the engineer found it met.</summary>
public sealed class CutoverPrerequisite
{
    public string Description { get; set; } = "";
    public bool Met { get; set; }
}

/// <summary>A functional criterion (service, user, device or sign-in behaviour) and its actual recorded result.</summary>
public sealed class CutoverCriterion
{
    public string Description { get; set; } = "";
    /// <summary>One of <see cref="CriterionResult.All"/>.</summary>
    public string Result { get; set; } = CriterionResult.NotRun;
    /// <summary>What was actually observed, and where. Required once the criterion has run.</summary>
    public string Detail { get; set; } = "";
}

/// <summary>A separately given approval, recorded for review. It authorises nothing in the toolkit.</summary>
public sealed class CutoverApproval
{
    public string ApprovedBy { get; set; } = "";
    /// <summary>Where the approval is recorded, for example a change reference.</summary>
    public string Reference { get; set; } = "";
}

/// <summary>A scenario the toolkit cannot support. The case stops at its stage until a revision clears it.</summary>
public sealed class CutoverEscalation
{
    public string Reason { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>
/// INT-050 cutover case revision: the full reviewed state of one move from old protection to its replacement at one
/// stage. Immutable once written; each later stage or correction is a new revision that supersedes the current one.
/// It holds no session, token, executable request or approval to activate, assign or retire anything.
/// </summary>
public sealed class CutoverRevision : IJobRecord
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    /// <summary>This revision's ID.</summary>
    public string Id { get; set; } = "";
    /// <summary>The case every revision of the same cutover shares.</summary>
    public string CaseId { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string JobId { get; set; } = "";
    public string SemanticId { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string InstanceKey { get; set; } = "";
    public string StandardRelease { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ClientScopeDigest { get; set; } = "";
    /// <summary>One of <see cref="CutoverStage.Order"/>.</summary>
    public string Stage { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Actor { get; set; } = "";
    /// <summary>Why this revision was recorded.</summary>
    public string Reason { get; set; } = "";
    public string RecordedAt { get; set; } = "";
    public string ReviewDueAt { get; set; } = "";
    /// <summary>Captures and runs this revision relies on, each pinned by its integrity digest.</summary>
    public List<EvidenceReference> Evidence { get; set; } = new();
    /// <summary>The existing protection being replaced, by exact object ID.</summary>
    public List<string> OldObjectIds { get; set; } = new();
    /// <summary>SHA-256 of the old objects as captured when the case was reviewed.</summary>
    public string OldMaterialDigest { get; set; } = "";
    /// <summary>The replacement objects, by exact object ID, once a supported run created them.</summary>
    public List<string> NewObjectIds { get; set; } = new();
    public string Overlaps { get; set; } = "";
    /// <summary>The real pilot population: group object IDs present in a capture. Never the whole tenant.</summary>
    public List<string> PilotGroupIds { get; set; } = new();
    public CutoverApproval? PilotApproval { get; set; }
    public List<CutoverPrerequisite> Prerequisites { get; set; } = new();
    public List<CutoverCriterion> Criteria { get; set; } = new();
    public string RecoveryLimits { get; set; } = "";
    /// <summary>One of <see cref="RetirementDecision.All"/>, once retirement has been reviewed.</summary>
    public string? Retirement { get; set; }
    public CutoverApproval? RetirementApproval { get; set; }
    /// <summary>Same-tenant approved deviations that remain after closure.</summary>
    public List<string> ResidualDeviationIds { get; set; } = new();
    public CutoverEscalation? Unsupported { get; set; }
    public string? SupersedesId { get; set; }
    public string IntegrityDigest { get; set; } = "";
}
