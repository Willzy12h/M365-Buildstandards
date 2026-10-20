using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;
/// <summary>Independent source-bound observations; never deployment evidence, consent or effective access.</summary>
public sealed class OperationalContextReport
{
    [JsonRequired] public int SchemaVersion {get;set;}=1;
    [JsonRequired] public string Kind {get;set;}="operationalContextReport";
    [JsonRequired] public string Id {get;set;}=Guid.NewGuid().ToString("D");
    [JsonRequired] public string ReportId {get;set;}="";
    [JsonRequired] public string AccessIntent {get;set;}="";
    [JsonRequired] public string PermissionMode {get;set;}="Assessment";
    [JsonRequired] public string AdapterVersion {get;set;}="1.0";
    [JsonRequired] public string TenantId {get;set;}="";
    [JsonRequired] public string? AccountObjectId {get;set;}
    [JsonRequired] public string? ClientId {get;set;}
    [JsonRequired] public string SourceMode {get;set;}="historical";
    [JsonRequired] public string? CustomerTenantId {get;set;}
    [JsonRequired] public string AsOfUtc {get;set;}="";
    [JsonRequired] public string StartedAt {get;set;}="";
    [JsonRequired] public string EndedAt {get;set;}="";
    [JsonRequired] public string ToolkitVersion {get;set;}="";
    [JsonRequired] public string Status {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public List<ReportSource> Sources {get;set;}=[];
    [JsonRequired] public List<ReportSection> Sections {get;set;}=[];
    [JsonRequired] public PartnerReportCoverage? PartnerCoverage {get;set;}
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CollaborationReportSelection? CollaborationSelection {get;set;}
    [JsonRequired] public string IntegrityDigest {get;set;}="";
}
public sealed class PartnerReportCoverage
{
    [JsonRequired] public bool ParentInventoryComplete {get;set;}
    [JsonRequired] public int ParentRowsRead {get;set;}
    [JsonRequired] public int SelectedCustomerRows {get;set;}
    [JsonRequired] public int ExcludedOtherCustomerRows {get;set;}
    [JsonRequired] public int UnattributableCustomerRows {get;set;}
}
public sealed class ServiceHealthOverviewRow:GraphReportRow
{
    [JsonRequired] public string? Status {get;set;}
}
public sealed class ServiceHealthIssueRow:GraphReportRow
{
    [JsonRequired] public string? Service {get;set;}
    [JsonRequired] public string? Feature {get;set;}
    [JsonRequired] public string? Classification {get;set;}
    [JsonRequired] public string? Status {get;set;}
    [JsonRequired] public string? ImpactSummary {get;set;}
    [JsonRequired] public string? StartedAt {get;set;}
    [JsonRequired] public string? EndedAt {get;set;}
    [JsonRequired] public string? ModifiedAt {get;set;}
}
public sealed class PartnerRelationshipRow:GraphReportRow
{
    [JsonRequired] public string? ResourceType {get;set;}
    [JsonRequired] public string? CustomerTenantId {get;set;}
    [JsonRequired] public string? Status {get;set;}
    [JsonRequired] public string? CreatedAt {get;set;}
    [JsonRequired] public string? ActivatedAt {get;set;}
    [JsonRequired] public string? ModifiedAt {get;set;}
    [JsonRequired] public string? EndedAt {get;set;}
    [JsonRequired] public string? Duration {get;set;}
    [JsonRequired] public string? AutoExtendDuration {get;set;}
    [JsonRequired] public string ExpiryAtAsOf {get;set;}="Unknown";
    [JsonRequired] public List<string>? RoleDefinitionIds {get;set;}
    [JsonRequired] public string AssignmentReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public string? AssignmentReadError {get;set;}="Child assignments were not attempted.";
    [JsonRequired] public int? ReturnedAssignments {get;set;}
}
public sealed class PartnerAssignmentReadRow:GraphReportRow
{
    [JsonRequired] public int? ReturnedAssignments {get;set;}
}
public sealed class PartnerAccessAssignmentRow:GraphReportRow
{
    [JsonRequired] public string? RelationshipId {get;set;}
    [JsonRequired] public string? AssignmentId {get;set;}
    [JsonRequired] public string? Status {get;set;}
    [JsonRequired] public string? ContainerType {get;set;}
    [JsonRequired] public string? ContainerId {get;set;}
    [JsonRequired] public List<string>? RoleDefinitionIds {get;set;}
}
