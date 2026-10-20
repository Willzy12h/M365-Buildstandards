using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;
public sealed class CollaborationGroupRow : GraphReportRow
{
    [JsonRequired] public List<string>? GroupTypes { get; set; }
    [JsonRequired] public bool? MailEnabled { get; set; }
    [JsonRequired] public bool? SecurityEnabled { get; set; }
    [JsonRequired] public bool? OnPremisesSyncEnabled { get; set; }
    [JsonRequired] public string SyncMeaning { get; set; } = "Unknown";
    [JsonRequired] public List<string>? ProvisioningOptions { get; set; }
    [JsonRequired] public string OwnerReadDisposition { get; set; } = "Unknown";
    [JsonRequired] public string OwnerCoverage { get; set; } = "Unknown";
    [JsonRequired] public bool? TeamProvisioned { get; set; }
}
public sealed class CollaborationTeamMemberRow : GraphReportRow
{
    [JsonRequired] public string TeamId { get; set; } = "";
    [JsonRequired] public string? MemberId { get; set; }
    [JsonRequired] public string? ResourceType { get; set; }
    [JsonRequired] public string? UserId { get; set; }
    [JsonRequired] public string? UserTenantId { get; set; }
    [JsonRequired] public List<string>? Roles { get; set; }
    [JsonRequired] public string RoleMeaning { get; set; } = "Unknown";
}
public sealed class CollaborationSiteRow : GraphReportRow
{
    [JsonRequired] public string? ReportedName { get; set; }
    [JsonRequired] public string? SiteCollectionHost { get; set; }
    [JsonRequired] public string? WebUrl { get; set; }
    [JsonRequired] public string? CreatedAt { get; set; }
    [JsonRequired] public string? ModifiedAt { get; set; }
}
public sealed class CollaborationLibraryRow : GraphReportRow
{
    [JsonRequired] public string SiteId { get; set; } = "";
    [JsonRequired] public string? DriveId { get; set; }
    [JsonRequired] public string? DriveType { get; set; }
    [JsonRequired] public string? OwnerUserId { get; set; }
    [JsonRequired] public string? OwnerUserName { get; set; }
    [JsonRequired] public string? OwnerGroupId { get; set; }
    [JsonRequired] public string? OwnerGroupName { get; set; }
    [JsonRequired] public string? WebUrl { get; set; }
}
public sealed class CollaborationOwnerRow : GraphReportRow
{
    [JsonRequired] public string GroupId { get; set; } = "";
    [JsonRequired] public string? ObjectId { get; set; }
    [JsonRequired] public string? ResourceType { get; set; }
    [JsonRequired] public string Coverage { get; set; } = "Unknown";
}
