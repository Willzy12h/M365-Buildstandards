using System.Text.Json.Serialization;
namespace BuildStandard.Core.Models;
/// <summary>INT-129 original usage CSV pack. Report identity text never establishes a directory join or change approval.</summary>
public sealed class UsageReviewReport
{
    [JsonRequired] public int SchemaVersion {get;set;}=1;
    [JsonRequired] public string Kind {get;set;}="usageReviewReport";
    [JsonRequired] public string Id {get;set;}="";
    [JsonRequired] public string TenantId {get;set;}="";
    [JsonRequired] public string? AccountObjectId {get;set;}
    [JsonRequired] public string SourceMode {get;set;}="historical";
    [JsonRequired] public string StartedAt {get;set;}="";
    [JsonRequired] public string EndedAt {get;set;}="";
    [JsonRequired] public string ToolkitVersion {get;set;}="";
    [JsonRequired] public string Status {get;set;}="NotAttempted";
    [JsonRequired] public List<UsageReviewSource> Sources {get;set;}=[];
    [JsonRequired] public string IntegrityDigest {get;set;}="";
}
public sealed class UsageReviewSource
{
    [JsonRequired] public string SourceId {get;set;}="";
    [JsonRequired] public string Period {get;set;}="D30";
    [JsonRequired] public string Status {get;set;}="NotAttempted";
    [JsonRequired] public string? Reason {get;set;}
    [JsonRequired] public string PrivacyProvenance {get;set;}="Unknown";
    [JsonRequired] public string? DownloadHost {get;set;}
    [JsonRequired] public string? CsvSha256 {get;set;}
    [JsonRequired] public string? OriginalCsvBase64 {get;set;}
}
