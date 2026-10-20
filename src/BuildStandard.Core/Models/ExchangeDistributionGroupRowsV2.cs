using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;

/// <summary>One selected Exchange group, original configuration and direct-member read. No effective delivery claim.</summary>
public sealed class ExchangeGroupTargetRow : ExchangeReportRow
{
    [JsonRequired] public string RequestedGroupGuid {get;set;}="";
    [JsonRequired] public string? ObservedGroupGuid {get;set;}
    [JsonRequired] public string? ExternalDirectoryObjectId {get;set;}
    [JsonRequired] public string? PrimarySmtpAddress {get;set;}
    [JsonRequired] public string LookupReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public string MembershipReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public int? ReturnedEntries {get;set;}
    [JsonRequired] public int? ExcludedEntries {get;set;}
    [JsonRequired] public ExchangeObserved<string> RecipientTypeDetails {get;set;}=new();
    [JsonRequired] public ExchangeObserved<List<ExchangeReferenceValue>> ManagedBy {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> MemberJoinRestriction {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> MemberDepartRestriction {get;set;}=new();
    [JsonRequired] public ExchangeObserved<bool?> RequireSenderAuthenticationEnabled {get;set;}=new();
    [JsonRequired] public ExchangeObserved<bool?> HiddenFromAddressListsEnabled {get;set;}=new();
}

/// <summary>A direct returned member, identified in its original source namespace; no recursive expansion or name join.</summary>
public sealed class ExchangeGroupMemberRow : ExchangeReportRow
{
    [JsonRequired] public string GroupGuid {get;set;}="";
    [JsonRequired] public ExchangeReferenceValue SourceIdentity {get;set;}=new();
    [JsonRequired] public string? PrimarySmtpAddress {get;set;}
    [JsonRequired] public ExchangeObserved<string> RecipientTypeDetails {get;set;}=new();
}
