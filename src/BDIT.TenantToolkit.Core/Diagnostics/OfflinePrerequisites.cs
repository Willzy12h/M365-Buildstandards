namespace BDIT.TenantToolkit.Core.Diagnostics;

public enum OfflinePrerequisiteStatus { Ready, NeedsAttention, Cancelled }
public enum OfflineFindingDisposition { Pass, Fail, Unknown, NotApplicable }
public enum OfflineFindingApplicability { Required, Optional }

/// <summary>Local observations only: never tenant readiness, write approval or execution evidence.</summary>
public sealed record OfflinePrerequisiteFinding(string Id, OfflineFindingApplicability Applicability,
    OfflineFindingDisposition Disposition, string Explanation, string NextStep);

public sealed record OfflinePrerequisiteResult(string ToolkitVersion, string StartedAt, string EndedAt,
    OfflinePrerequisiteStatus Status, IReadOnlyList<OfflinePrerequisiteFinding> Findings)
{
    public string Kind => "offlinePrerequisites";
    public int SchemaVersion => 1;
}

/// <summary>Fixed safe wording: requested input and operating-system errors must not become CLI output.</summary>
public sealed class OfflineDiagnosticInputException : Exception
{
    public OfflineDiagnosticInputException() : base("Choose an existing, ordinary portable root folder without links or reparse points.") { }
}
