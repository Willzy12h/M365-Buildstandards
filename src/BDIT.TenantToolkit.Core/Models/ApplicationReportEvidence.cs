using System.Text.Json.Serialization;

namespace BDIT.TenantToolkit.Core.Models;

/// <summary>INT-094 schema 2: metadata-only Graph report evidence, never deployment before-evidence.</summary>
public abstract class ApplicationReportEvidence
{
    [JsonRequired] public int SchemaVersion { get; set; } = 2;
    [JsonRequired] public string Kind { get; set; } = "reportEvidence";
    [JsonRequired] public string Id { get; set; } = "";
    [JsonRequired] public string ReportId { get; set; } = "";
    [JsonRequired] public int ReportSchemaVersion { get; set; } = 1;
    [JsonRequired] public string TenantId { get; set; } = "";
    [JsonRequired] public string? AccountObjectId { get; set; }
    [JsonRequired] public string Resource { get; set; } = "Graph";
    [JsonRequired] public string SourceMode { get; set; } = "live";
    [JsonRequired] public string StartedAt { get; set; } = "";
    [JsonRequired] public string EndedAt { get; set; } = "";
    [JsonRequired] public string ToolkitVersion { get; set; } = "";
    [JsonRequired] public string ModuleVersion { get; set; } = "";
    [JsonRequired] public List<ApplicationReportSource> Sources { get; set; } = [];
    [JsonRequired] public string Status { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public List<string> Limitations { get; set; } = [];
    [JsonRequired] public List<ReportSection> Sections { get; set; } = [];
    [JsonRequired] public string IntegrityDigest { get; set; } = "";
}

public sealed class ApplicationExpiryEvidence : ApplicationReportEvidence
{
    public ApplicationExpiryEvidence() { ReportId = "application-expiry"; ReportSchemaVersion = 2; }
    [JsonRequired] public ApplicationExpiryParameters Parameters { get; set; } = new();
}
public sealed class ApplicationConsentEvidence : ApplicationReportEvidence
{
    public ApplicationConsentEvidence() { ReportId = "application-consent"; }
    [JsonRequired] public ApplicationConsentParameters Parameters { get; set; } = new();
}
public sealed class ApplicationExpiryParameters
{
    [JsonRequired] public int HorizonDays { get; set; } = 30;
}
public sealed class ApplicationConsentParameters { }

/// <summary>Each registered source states its access/read outcome and observed interval, including child reads.</summary>
public sealed class ApplicationReportSource
{
    [JsonRequired] public string Api { get; set; } = "v1.0";
    [JsonRequired] public string RegisteredRoute { get; set; } = "";
    [JsonRequired] public string Reference { get; set; } = "";
    [JsonRequired] public string Status { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? Error { get; set; }
    [JsonRequired] public string StartedAt { get; set; } = "";
    [JsonRequired] public string EndedAt { get; set; } = "";
    [JsonRequired] public int Rows { get; set; }
    [JsonRequired] public int ObjectsAttempted { get; set; }
    // Absent in immutable revision-1 records and non-owner sources. Revision 2 requires this on owner sources.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ApplicationOwnerOutcomes? RetainedOwnerOutcomes { get; set; }
}

public sealed class ApplicationOwnerOutcomes
{
    [JsonRequired] public int Collected { get; set; }
    [JsonRequired] public int Partial { get; set; }
    [JsonRequired] public int Failed { get; set; }
    [JsonRequired] public int Cancelled { get; set; }
    [JsonRequired] public int NotAttempted { get; set; }
}

public sealed class ApplicationExpiryRow : GraphReportRow
{
    [JsonRequired] public string IdentityStatus { get; set; } = "Unknown";
    [JsonRequired] public string ObjectKind { get; set; } = "";
    [JsonRequired] public string? ApplicationId { get; set; }
    [JsonRequired] public string CredentialsReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? CredentialReadReason { get; set; }
    [JsonRequired] public List<ApplicationCredentialMetadata> Credentials { get; set; } = [];
    [JsonRequired] public string OwnersReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? OwnerReadReason { get; set; }
    [JsonRequired] public List<ApplicationOwnerMetadata> Owners { get; set; } = [];
}
public sealed class ApplicationCredentialMetadata
{
    [JsonRequired] public string Kind { get; set; } = "";
    [JsonRequired] public string? KeyId { get; set; }
    [JsonRequired] public string? DisplayName { get; set; }
    [JsonRequired] public string? Type { get; set; }
    [JsonRequired] public string? Usage { get; set; }
    [JsonRequired] public string? Start { get; set; }
    [JsonRequired] public string? End { get; set; }
    [JsonRequired] public string Classification { get; set; } = "Unknown";
    [JsonRequired] public string? Reason { get; set; }
}
public sealed class ApplicationOwnerMetadata
{
    [JsonRequired] public string? Id { get; set; }
    [JsonRequired] public string? Name { get; set; }
    [JsonRequired] public string ReadStatus { get; set; } = ReportReadState.Partial;
    [JsonRequired] public string? Reason { get; set; }
}
public sealed class ApplicationPrincipalReportRow : GraphReportRow
{
    [JsonRequired] public string IdentityStatus { get; set; } = "Unknown";
    [JsonRequired] public string? ApplicationId { get; set; }
    [JsonRequired] public string AppRolesReadStatus { get; set; } = ReportReadState.NotAttempted;
    [JsonRequired] public string? AppRolesReadReason { get; set; }
    [JsonRequired] public List<ApplicationRoleMetadata> AppRoles { get; set; } = [];
}
public sealed class ApplicationRoleMetadata
{
    [JsonRequired] public string? Id { get; set; }
    [JsonRequired] public string? DisplayName { get; set; }
    [JsonRequired] public string? Value { get; set; }
    [JsonRequired] public bool? IsEnabled { get; set; }
}
public sealed class DelegatedGrantReportRow : GraphReportRow
{
    [JsonRequired] public string? ClientId { get; set; }
    [JsonRequired] public string? ResourceId { get; set; }
    [JsonRequired] public string? PrincipalId { get; set; }
    [JsonRequired] public string? ConsentType { get; set; }
    [JsonRequired] public string? Scope { get; set; }
    [JsonRequired] public string? ClientName { get; set; }
    [JsonRequired] public string? ResourceName { get; set; }
    [JsonRequired] public string ResolutionStatus { get; set; } = "Unresolved";
    [JsonRequired] public string? ResolutionReason { get; set; }
}
public sealed class ApplicationRoleAssignmentReportRow : GraphReportRow
{
    [JsonRequired] public string? QueriedPrincipalId { get; set; }
    [JsonRequired] public string? PrincipalId { get; set; }
    [JsonRequired] public string? ResourceId { get; set; }
    [JsonRequired] public string? AppRoleId { get; set; }
    [JsonRequired] public string? PrincipalName { get; set; }
    [JsonRequired] public string? ResourceName { get; set; }
    [JsonRequired] public string? RoleName { get; set; }
    [JsonRequired] public string? RoleValue { get; set; }
    [JsonRequired] public string ResolutionStatus { get; set; } = "Unresolved";
    [JsonRequired] public string? ResolutionReason { get; set; }
}
