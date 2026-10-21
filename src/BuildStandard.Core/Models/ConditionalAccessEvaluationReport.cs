using System.Text.Json.Serialization;

namespace BuildStandard.Core.Models;

/// <summary>INT-127 report metadata paired with exact original response bytes; neither file authorises deployment.</summary>
public sealed class ConditionalAccessEvaluationReport
{
    [JsonRequired] public string Kind {get;set;}="conditionalAccessEvaluationReport";
    [JsonRequired] public int SchemaVersion {get;set;}=1;
    [JsonRequired] public string Id {get;set;}="";
    [JsonRequired] public string TenantId {get;set;}="";
    [JsonRequired] public string AccountObjectId {get;set;}="";
    [JsonRequired] public string ClientId {get;set;}="";
    [JsonRequired] public string Resource {get;set;}="https://graph.microsoft.com/";
    [JsonRequired] public string SourceMode {get;set;}="live";
    [JsonRequired] public SessionMode PermissionMode {get;set;}
    [JsonRequired] public List<string> Scopes {get;set;}=[];
    [JsonRequired] public string StartedAt {get;set;}="";
    [JsonRequired] public string CompletedAt {get;set;}="";
    [JsonRequired] public string ToolkitVersion {get;set;}="";
    [JsonRequired] public string AdapterVersion {get;set;}="conditional-access-evaluation/1";
    [JsonRequired] public ConditionalAccessScenario Scenario {get;set;}=null!;
    [JsonRequired] public string RequestSha256 {get;set;}="";
    [JsonRequired] public string ResponseSha256 {get;set;}="";
    [JsonRequired] public int ResponseBytes {get;set;}
    [JsonRequired] public string Status {get;set;}="";
    [JsonRequired] public string IntegrityDigest {get;set;}="";
}
