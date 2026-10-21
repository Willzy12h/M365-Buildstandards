using System.Text.Json.Serialization;
namespace BuildStandard.Engine.Reports;

/// <summary>Sanitised modern source descriptor. The original completed-job URL and status response are deliberately absent.</summary>
public sealed class CoverageModernExportRecord
{
    [JsonRequired] public string AdapterVersion {get;set;}="intune-export-csv/1";
    [JsonRequired] public string ReportName {get;set;}="";
    [JsonRequired] public string RequestSha256 {get;set;}="";
    [JsonRequired] public string JobId {get;set;}="";
    [JsonRequired] public string ArchiveFile {get;set;}="";
    [JsonRequired] public string ArchiveSha256 {get;set;}="";
    [JsonRequired] public int ArchiveBytes {get;set;}
    [JsonRequired] public string CsvSha256 {get;set;}="";
    [JsonRequired] public int CsvBytes {get;set;}
    [JsonRequired] public int SourceRows {get;set;}
}
