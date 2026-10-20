using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;

/// <summary>Exact selected mailbox, folder discovery and ACL read. Unknown counts are never successful empty reads.</summary>
public sealed class ExchangeCalendarTargetRow : ExchangeReportRow
{
    [JsonRequired] public string RequestedExchangeGuid {get;set;}="";
    [JsonRequired] public string? ObservedExchangeGuid {get;set;}
    [JsonRequired] public string? ExternalDirectoryObjectId {get;set;}
    [JsonRequired] public string? PrimarySmtpAddress {get;set;}
    [JsonRequired] public string LookupReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public string FolderReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public int? ReturnedFolders {get;set;}
    [JsonRequired] public int? DefaultFolderMatches {get;set;}
    [JsonRequired] public string? FolderId {get;set;}
    [JsonRequired] public string? FolderPath {get;set;}
    [JsonRequired] public string? NativeFolderIdentity {get;set;}
    [JsonRequired] public string PermissionReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public int? ReturnedEntries {get;set;}
    [JsonRequired] public int? ExcludedEntries {get;set;}
}

/// <summary>One original folder/principal ACL observation, not an effective delegate/access verdict.</summary>
public sealed class CalendarPermissionObservationRow : ExchangeReportRow
{
    [JsonRequired] public string MailboxExchangeGuid {get;set;}="";
    [JsonRequired] public string NativeFolderIdentity {get;set;}="";
    [JsonRequired] public ExchangeReferenceValue Principal {get;set;}=new();
    [JsonRequired] public ExchangeObserved<List<string>> Rights {get;set;}=new();
    [JsonRequired] public ExchangeObserved<List<string>> SharingPermissionFlags {get;set;}=new();
}
