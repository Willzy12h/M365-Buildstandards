using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

public enum ReviewedChangeKind { SecureCompliance, MdmAll, DisableSms, DisableVoice, ConfigureTap, EnableAuthenticator, EnableConditionalAccess, ReportOnlyConditionalAccess, DisableConditionalAccess, AssignGroups, RemoveAssignments, EnrolFeatureUpdates, UnenrolFeatureUpdates, EnrolQualityUpdates, UnenrolQualityUpdates }

public enum AssignmentPopulation { Groups, AllUsers, AllDevices }

public sealed class ReviewedChangePlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string StandardDigest { get; set; } = "";
    public string ProfileDigest { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string SnapshotDigest { get; set; } = "";
    public string MappingsDigest { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public ReviewedChangeKind Kind { get; set; }
    public string ControlId { get; set; } = "";
    public string ObjectId { get; set; } = "";
    public string Collection { get; set; } = "";
    public GraphApi Api { get; set; }
    public string Path { get; set; } = "";
    public string Method { get; set; } = "PATCH";
    public string RequiredScope { get; set; } = "";
    public JsonObject Before { get; set; } = new();
    public JsonObject Payload { get; set; } = new();
    /// <summary>Null preserves historical group-only plans and their integrity digests.</summary>
    public AssignmentPopulation? Population { get; set; }
    public List<string> IncludeGroups { get; set; } = new();
    public List<string> ExcludeGroups { get; set; } = new();
    public List<string> DeviceIds { get; set; } = new();
    public List<string> ResolvedTargets { get; set; } = new();
    public string Consequence { get; set; } = "";
    public string IntegrityDigest { get; set; } = "";
}

public sealed class ReviewedChangeRun
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string ControlId { get; set; } = "";
    public string PlanDigest { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string Status { get; set; } = RunStatus.Running;
    public string WriteAcceptance { get; set; } = Models.WriteAcceptance.NotAttempted;
    public string Verification { get; set; } = ConfigurationVerification.NotRun;
    public JsonObject? After { get; set; }
    public string? Error { get; set; }
    public string IntegrityDigest { get; set; } = "";
}

public sealed class ReviewedChangeVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string RunDigest { get; set; } = "";
    public string OperatorId { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public bool Verified { get; set; }
    public JsonObject? After { get; set; }
    public string? Error { get; set; }
    public string IntegrityDigest { get; set; } = "";
}
