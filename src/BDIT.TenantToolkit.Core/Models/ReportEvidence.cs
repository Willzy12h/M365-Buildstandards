using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>INT-071 report evidence. Never a configuration snapshot or complete deployment before-evidence.</summary>
public sealed class ReportEvidence
{
    [JsonRequired] public int SchemaVersion { get; set; } = 1;
    [JsonRequired] public string Kind { get; set; } = "reportEvidence";
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string ReportId { get; set; } = "";
    [JsonRequired] public int ReportSchemaVersion { get; set; } = 1;
    [JsonRequired] public string TenantId { get; set; } = "";
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? AccountObjectId { get; set; }
    [JsonRequired] public string Resource { get; set; } = "Graph";
    [JsonRequired] public string SourceMode { get; set; } = "live";
    [JsonRequired] public string StartedAt { get; set; } = "";
    [JsonRequired] public string EndedAt { get; set; } = "";
    [JsonRequired] public string ToolkitVersion { get; set; } = "";
    [JsonRequired] public string ModuleVersion { get; set; } = "";
    [JsonRequired] public List<ReportSource> Sources { get; set; } = [];
    [JsonRequired] public ReportParameters Parameters { get; set; } = new();
    [JsonRequired] public string Status { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public List<string> Limitations { get; set; } = [];
    [JsonRequired] public List<ReportSection> Sections { get; set; } = [];
    [JsonRequired] public string IntegrityDigest { get; set; } = "";
}

public sealed class ReportSource
{
    [JsonRequired] public string Api { get; set; } = "";
    [JsonRequired] public string RegisteredRoute { get; set; } = "";
    [JsonRequired] public string Reference { get; set; } = "";
}

public sealed class ReportParameters
{
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Start { get; set; }
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? End { get; set; }
}

public sealed class ReportSection
{
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string Status { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] public string? Error { get; set; }
    [JsonRequired] public List<string> Limitations { get; set; } = [];
    [JsonRequired] public List<JsonObject> Rows { get; set; } = [];
}

public static class ReportReadState
{
    public const string Collected = "Collected";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
    public const string NotAttempted = "NotAttempted";
    public const string Cancelled = "Cancelled";
    public static IReadOnlyList<string> All { get; } = [Collected, Partial, Failed, NotAttempted, Cancelled];
}

/// <summary>Only whitelisted report fields are projected; raw Graph rows are not stored in these report sections.</summary>
public abstract class GraphReportRow
{
    [JsonRequired] public string? Id { get; set; }
    [JsonRequired] public string? Name { get; set; }
    [JsonRequired] public string ReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? Error { get; set; }
}

public sealed class UserLicenceReportRow : GraphReportRow
{
    [JsonRequired] public string? UserPrincipalName { get; set; }
    [JsonRequired] public bool? AccountEnabled { get; set; }
    [JsonRequired] public string? UserType { get; set; }
    [JsonRequired] public string ProductsReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public List<AssignedProductReport> Products { get; set; } = [];
}

public sealed class AssignedProductReport
{
    [JsonRequired] public string? SkuId { get; set; }
    [JsonRequired] public string? SkuPartNumber { get; set; }
    [JsonRequired] public List<AssignedPlanReport> ServicePlans { get; set; } = [];
}

public sealed class AssignedPlanReport
{
    [JsonRequired] public string? ServicePlanId { get; set; }
    [JsonRequired] public string? ServicePlanName { get; set; }
    [JsonRequired] public string? ProvisioningStatus { get; set; }
    [JsonRequired] public string? AppliesTo { get; set; }
}

public sealed class SubscriptionReportRow : GraphReportRow
{
    [JsonRequired] public string? SkuId { get; set; }
    [JsonRequired] public string? CapabilityStatus { get; set; }
    [JsonRequired] public long? ConsumedUnits { get; set; }
    [JsonRequired] public long? EnabledUnits { get; set; }
    [JsonRequired] public long? WarningUnits { get; set; }
    [JsonRequired] public long? SuspendedUnits { get; set; }
}

public sealed class DeviceReportRow : GraphReportRow
{
    [JsonRequired] public string? UserId { get; set; }
    [JsonRequired] public string? Ownership { get; set; }
    [JsonRequired] public string? EnrolmentType { get; set; }
    [JsonRequired] public string? ManagementAgent { get; set; }
    [JsonRequired] public string? OperatingSystem { get; set; }
    [JsonRequired] public string? OsVersion { get; set; }
    [JsonRequired] public string? ComplianceState { get; set; }
    [JsonRequired] public string? LastSyncAt { get; set; }
}

public sealed class RegistrationReportRow : GraphReportRow
{
    [JsonRequired] public string? UserPrincipalName { get; set; }
    [JsonRequired] public bool? IsMfaRegistered { get; set; }
    [JsonRequired] public bool? IsMfaCapable { get; set; }
    [JsonRequired] public bool? IsSsprRegistered { get; set; }
    [JsonRequired] public List<string>? MethodsRegistered { get; set; }
    [JsonRequired] public string? LastUpdatedAt { get; set; }
}

public sealed class SignInReportRow : GraphReportRow
{
    [JsonRequired] public string? UserId { get; set; }
    [JsonRequired] public string? UserPrincipalName { get; set; }
    [JsonRequired] public string? CreatedAt { get; set; }
    [JsonRequired] public string? ApplicationId { get; set; }
    [JsonRequired] public string? ApplicationName { get; set; }
    [JsonRequired] public string? ClientApplication { get; set; }
    [JsonRequired] public string? IpAddress { get; set; }
    [JsonRequired] public long? ErrorCode { get; set; }
    [JsonRequired] public string? FailureReason { get; set; }
}

public sealed class DirectoryAuditReportRow : GraphReportRow
{
    [JsonRequired] public string? ActivityAt { get; set; }
    [JsonRequired] public string? Category { get; set; }
    [JsonRequired] public string? Result { get; set; }
    [JsonRequired] public string? ResultReason { get; set; }
    [JsonRequired] public string? ActorUserId { get; set; }
    [JsonRequired] public string? ActorApplicationId { get; set; }
    [JsonRequired] public List<string>? TargetIds { get; set; }
}
