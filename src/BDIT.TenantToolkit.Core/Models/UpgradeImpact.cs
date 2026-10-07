namespace BDIT.TenantToolkit.Core.Models;

/// <summary>
/// INT-051 upgrade impact: one unchanged capture assessed under a source and a target release, with each source
/// requirement traced to the target through release lineage. It describes how the standard changed for this tenant,
/// not how the tenant changed; that is the drift report's job. Creates no ownership, exception or write authority.
/// </summary>
public sealed class UpgradeImpactReport
{
    public string TenantId { get; set; } = "";
    public string TenantName { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    public string SnapshotRelease { get; set; } = "";
    public string SourceRelease { get; set; } = "";
    public string SourceDigest { get; set; } = "";
    public string TargetRelease { get; set; } = "";
    public string TargetDigest { get; set; } = "";
    /// <summary>False when no verified lineage connects the two releases; every row is then "Unknown" and needs review.</summary>
    public bool LineageRecorded { get; set; }
    public string GeneratedAt { get; set; } = "";
    public List<UpgradeImpactRow> Rows { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    public int Count(string change) => Rows.Count(r => r.Change == change);
    public int StatusChanges => Rows.Count(r => r.StatusChanged);
}

public sealed class UpgradeImpactRow
{
    /// <summary>Same requirement, Renamed, Changed, Replaced, Retired, Added or Unknown.</summary>
    public string Change { get; set; } = "";
    public string SourceControl { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceStatus { get; set; } = "";
    public string TargetControls { get; set; } = "";
    public string TargetNames { get; set; } = "";
    public string TargetStatus { get; set; } = "";
    public bool StatusChanged { get; set; }
    public string Reason { get; set; } = "";
}

public static class UpgradeImpactChange
{
    public const string Same = "Same requirement";
    public const string Renamed = "Renamed";
    public const string Changed = "Changed";
    public const string Replaced = "Replaced";
    public const string Retired = "Retired";
    public const string Added = "Added";
    public const string Unknown = "Unknown";
    public static readonly string[] Order = { Unknown, Replaced, Retired, Renamed, Changed, Added, Same };
}
