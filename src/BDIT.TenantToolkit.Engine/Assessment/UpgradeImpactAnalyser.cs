using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>
/// Builds the INT-051 upgrade impact report. Both assessments read the same capture, inputs, ownership records and
/// accepted deviations; only the catalogue differs, so every difference shown is a change in the standard. Collections
/// the capture did not read stay unknown under both releases. Read-only: nothing is saved, rebound or planned.
/// </summary>
public static class UpgradeImpactAnalyser
{
    public static UpgradeImpactReport Analyse(AssessmentEngine engine, TenantSnapshot snapshot, StandardCatalogue source, StandardCatalogue target,
        ReleaseLineage? lineage, TenantProfile profile, ManagedObjectMappings mappings, IReadOnlyList<Deviation> deviations, DateTimeOffset now)
    {
        if (string.Equals(source.Release, target.Release, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("Choose two different releases to compare.");
        var before = engine.Assess(snapshot, source, profile, mappings, deviations, "upgrade impact", evidenceTime: CapturedAt(snapshot));
        var after = engine.Assess(snapshot, target, profile, mappings, deviations, "upgrade impact", evidenceTime: CapturedAt(snapshot));
        var relations = lineage is not null && string.Equals(lineage.Target.Release, target.Release, StringComparison.OrdinalIgnoreCase)
            && string.Equals(lineage.Target.Sha256, target.IntegrityDigest, StringComparison.OrdinalIgnoreCase)
            ? lineage.Source(source.Release) : null;
        if (relations is not null && !string.Equals(relations.Sha256, source.IntegrityDigest, StringComparison.OrdinalIgnoreCase)) relations = null;

        var report = new UpgradeImpactReport
        {
            TenantId = snapshot.TenantId, TenantName = snapshot.TenantName, SnapshotId = snapshot.Id, CapturedAt = snapshot.CapturedAt,
            SnapshotRelease = snapshot.StandardRelease, SourceRelease = source.Release, SourceDigest = source.IntegrityDigest,
            TargetRelease = target.Release, TargetDigest = target.IntegrityDigest, LineageRecorded = relations is not null,
            GeneratedAt = Timestamps.Format(now)
        };
        report.Notes.Add("One capture assessed under both releases with the same client inputs, ownership records and deviations. Differences describe the standard, not the tenant. Use the drift report to compare two captures.");
        if (relations is null)
            report.Notes.Add($"No verified lineage connects {source.Release} to {target.Release}. Each requirement is shown by control ID only and marked Unknown: confirm that each ID still means the same requirement.");
        if (!snapshot.Complete)
            report.Notes.Add("The capture is incomplete. Requirements whose collections were not read are unknown under both releases.");

        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var control in source.Controls)
        {
            var relation = relations?.Find(control.Id);
            // Listed controls follow their relation; anything else is matched by ID (declared by the lineage, or the
            // only guess available without one, which is then marked Unknown).
            var targets = relation?.TargetControls ?? (target.FindControl(control.Id) is null ? new List<string>() : new List<string> { control.Id });
            foreach (var t in targets) reached.Add(t);
            var change = relations is null ? UpgradeImpactChange.Unknown
                : relation is null ? (target.FindControl(control.Id) is null ? UpgradeImpactChange.Unknown : UpgradeImpactChange.Same)
                : relation.Relation switch
                {
                    "Renamed" => UpgradeImpactChange.Renamed, "Changed" => UpgradeImpactChange.Changed, "Replaced" => UpgradeImpactChange.Replaced,
                    "Retired" => UpgradeImpactChange.Retired, "Unchanged" => UpgradeImpactChange.Same, _ => UpgradeImpactChange.Unknown
                };
            var sourceStatus = Status(before, source, control.Id);
            var targetStatus = targets.Count == 0 ? "" : string.Join("; ", targets.Select(t => Status(after, target, t)));
            report.Rows.Add(new UpgradeImpactRow
            {
                Change = change, SourceControl = control.Id, SourceName = control.Name, SourceStatus = sourceStatus,
                TargetControls = string.Join(", ", targets), TargetNames = string.Join(", ", targets.Select(t => target.FindControl(t)?.Name ?? "")),
                TargetStatus = targetStatus, StatusChanged = targets.Count > 0 && sourceStatus != targetStatus,
                Reason = relation?.Reason ?? (change == UpgradeImpactChange.Unknown
                    ? (targets.Count == 0 ? $"{control.Id} is not in {target.Release} and no lineage explains why." : "Matched by control ID only; confirm it is the same requirement.")
                    : "Same requirement under the same control ID.")
            });
        }
        foreach (var control in target.Controls.Where(c => !reached.Contains(c.Id)))
            report.Rows.Add(new UpgradeImpactRow
            {
                Change = UpgradeImpactChange.Added, TargetControls = control.Id, TargetNames = control.Name, TargetStatus = Status(after, target, control.Id),
                Reason = relations is null ? "Not matched to a source control by ID." : $"New in {target.Release}, or the new meaning of a reused control ID."
            });
        report.Rows = report.Rows.OrderBy(r => Array.IndexOf(UpgradeImpactChange.Order, r.Change))
            .ThenBy(r => r.SourceControl.Length > 0 ? r.SourceControl : r.TargetControls, StringComparer.OrdinalIgnoreCase).ToList();
        return report;
    }

    /// <summary>The control's status, or each office instance's status for a repeated control, in plain words.</summary>
    private static string Status(AssessmentResult result, StandardCatalogue standard, string controlId)
    {
        var repeated = standard.FindControl(controlId)?.RepeatFor is not null;
        var findings = result.Findings.Where(f => string.Equals(f.ControlId, controlId, StringComparison.OrdinalIgnoreCase)
            || repeated && f.ControlId.StartsWith(controlId + "-", StringComparison.OrdinalIgnoreCase)).ToList();
        if (findings.Count == 0) return "Not assessed";
        if (findings.Count == 1) return StatusLabels.For(findings[0].Status);
        return string.Join(", ", findings.GroupBy(f => f.Status).OrderBy(g => g.Key).Select(g => $"{StatusLabels.For(g.Key)} ×{g.Count()}"));
    }

    private static DateTimeOffset? CapturedAt(TenantSnapshot snapshot) => Timestamps.TryParse(snapshot.CapturedAt, out var at) ? at : null;
}
