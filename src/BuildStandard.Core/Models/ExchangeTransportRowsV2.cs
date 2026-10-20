using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;
public sealed class ExchangeTransportReadRow:ExchangeReportRow
{
    [JsonRequired] public string Scope {get;set;}="";
    [JsonRequired] public string? RequestedRuleGuid {get;set;}
    [JsonRequired] public int? RequestedResultSize {get;set;}
    [JsonRequired] public int? ReturnedEntries {get;set;}
    [JsonRequired] public int? ExcludedEntries {get;set;}
}
/// <summary>Only metadata; Guid is the original Exchange rule ID, never a local observation hash.</summary>
public sealed class TransportRuleObservationRow:ExchangeReportRow
{
    [JsonRequired] public string RuleGuid {get;set;}="";
    [JsonRequired] public ExchangeObserved<int?> Priority {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> State {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> Mode {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> ActivationDate {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> ExpiryDate {get;set;}=new();
}
