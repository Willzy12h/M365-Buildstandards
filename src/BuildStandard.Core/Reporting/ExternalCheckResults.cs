using System.Text.Json.Serialization;
namespace BuildStandard.Core.Reporting;

/// <summary>External source claims only; never a native assessment, snapshot or execution approval.</summary>
public sealed record ExternalCheckResults(string Kind, int SchemaVersion, string ToolName, string ToolVersion,
    string SourceRunId, string TenantId, string PerformedAt, string SourceState,
    IReadOnlyList<ExternalCheckResultRow> Rows, string IntegrityDigest);
public sealed record ExternalCheckResultRow(string TestId, string Outcome, string Title, string? Reason, string? Reference)
{
    [JsonIgnore] public string DisplayOutcome => Outcome is "Passed" or "Failed" or "Skipped" or "Error" or "Unknown" ? Outcome : "Unclassified";
}
