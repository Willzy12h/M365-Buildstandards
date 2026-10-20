using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;
/// <summary>One reviewed interval, including unattempted/failed reads. Local ID hashes the exact interval/filter tuple.</summary>
public sealed class ExchangeTraceIntervalRow:ExchangeReportRow
{
    [JsonRequired] public int WindowIndex {get;set;}
    [JsonRequired] public string FrozenAtUtc {get;set;}="";
    [JsonRequired] public string StartUtc {get;set;}="";
    [JsonRequired] public string EndUtc {get;set;}="";
    [JsonRequired] public string FilterDigest {get;set;}="";
    [JsonRequired] public int? RequestedResultSize {get;set;}
    [JsonRequired] public int? ReturnedEntries {get;set;}
    [JsonRequired] public int? ExcludedEntries {get;set;}
}
public sealed class ExchangeTraceRecipientRow:ExchangeReportRow
{
    [JsonRequired] public int WindowIndex {get;set;}
    [JsonRequired] public string MessageTraceId {get;set;}="";
    [JsonRequired] public string RecipientAddress {get;set;}="";
    [JsonRequired] public string ReceivedUtc {get;set;}="";
    [JsonRequired] public ExchangeObserved<string> SenderAddress {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> InternetMessageId {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> Status {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> Size {get;set;}=new();
    [JsonRequired] public ExchangeObserved<string> Subject {get;set;}=new();
}
