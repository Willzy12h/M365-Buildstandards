using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;

/// <summary>INT-128 reported value shape. ReturnedNull, missing and failed remain distinct from false/zero/empty.</summary>
public sealed class ExchangeObserved<T>
{
    [JsonRequired] public string State {get;set;}="NotAttempted";
    [JsonRequired,JsonIgnore(Condition=JsonIgnoreCondition.Never)] public T? Value {get;set;}
    [JsonRequired,JsonIgnore(Condition=JsonIgnoreCondition.Never)] public string? Reason {get;set;}
}

/// <summary>A reported raw reference, not a resolved principal or authority to act on an Entra object.</summary>
public sealed class ExchangeReferenceValue
{
    [JsonRequired] public string Namespace {get;set;}="";
    [JsonRequired] public string Value {get;set;}="";
    [JsonRequired,JsonIgnore(Condition=JsonIgnoreCondition.Never)] public string? ExternalDirectoryObjectId {get;set;}
}

/// <summary>One requested ExchangeGuid and its read outcome, even when no child entries could be returned.</summary>
public sealed class ExchangeTargetReadRow:ExchangeReportRow
{
    [JsonRequired] public string RequestedExchangeGuid {get;set;}="";
    [JsonRequired] public string? ObservedExchangeGuid {get;set;}
    [JsonRequired] public string? ExternalDirectoryObjectId {get;set;}
    [JsonRequired] public string? PrimarySmtpAddress {get;set;}
    [JsonRequired] public string LookupReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public string ChildReadStatus {get;set;}=ReportReadState.NotAttempted;
    [JsonRequired] public int? ReturnedEntries {get;set;}
    [JsonRequired] public int? ExcludedEntries {get;set;}
}

/// <summary>RuleIdentity is opaque; Id is its local sha256 observation key, never a fabricated service GUID.</summary>
public sealed class InboxRuleObservationRow:ExchangeReportRow
{
    [JsonRequired] public string MailboxExchangeGuid {get;set;}="";
    [JsonRequired] public string RuleIdentity {get;set;}="";
    [JsonRequired] public string? ActualIdentityRaw {get;set;}
    [JsonRequired] public ExchangeObserved<bool?> Enabled {get;set;}=new();
    [JsonRequired] public ExchangeObserved<int?> Priority {get;set;}=new();
    [JsonRequired] public ExchangeObserved<List<ExchangeReferenceValue>> ForwardTo {get;set;}=new();
    [JsonRequired] public ExchangeObserved<List<ExchangeReferenceValue>> ForwardAsAttachmentTo {get;set;}=new();
    [JsonRequired] public ExchangeObserved<List<ExchangeReferenceValue>> RedirectTo {get;set;}=new();
    [JsonRequired] public ExchangeObserved<bool?> DeleteMessage {get;set;}=new();
    [JsonRequired] public ExchangeObserved<bool?> MarkAsRead {get;set;}=new();
    [JsonRequired] public ExchangeObserved<bool?> StopProcessingRules {get;set;}=new();
}
