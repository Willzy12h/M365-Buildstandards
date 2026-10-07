using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>One ownership record made under an earlier release whose meaning under the current release needs review.</summary>
public sealed record LineageNote(string MappedControlId, string SourceRelease, string Relation, IReadOnlyList<string> CurrentControls, string Message);

/// <summary>
/// INT-051 read-only reconciliation (CLA-20261006-07). Ownership records are keyed by control ID, and some IDs were
/// reused for different requirements between releases. This explains each record from an earlier release against the
/// current one. It never rebinds, moves or drops a record, and it changes no finding status or plan: the existing
/// ownership and drift checks still decide what may be written.
/// </summary>
public static class LineageReview
{
    public static IReadOnlyList<LineageNote> Review(ManagedObjectMappings mappings, StandardCatalogue current, ReleaseLineage? lineage)
    {
        var notes = new List<LineageNote>();
        foreach (var mapping in mappings.ByControl.Values.OrderBy(m => m.ControlId, StringComparer.OrdinalIgnoreCase))
        {
            if (mapping.Release.Length == 0 || string.Equals(mapping.Release, current.Release, StringComparison.OrdinalIgnoreCase)) continue;
            var id = mapping.ControlId;
            var source = lineage is not null && string.Equals(lineage.Target.Release, current.Release, StringComparison.OrdinalIgnoreCase)
                ? lineage.Source(mapping.Release) : null;
            if (source is null)
            {
                notes.Add(new LineageNote(id, mapping.Release, "Unknown", new[] { id },
                    $"Review needed: the ownership record for {id} was made under {mapping.Release}, and no lineage from {mapping.Release} to {current.Release} is recorded. Confirm that {id} still means the same requirement before relying on it."));
                continue;
            }
            var relation = source.Find(id);
            if (relation is null) continue; // Declared: an unlisted control keeps the same requirement under the same ID.
            var currentName = current.FindControl(id)?.Name;
            var was = $"{id} ({relation.SourceName}) in {mapping.Release}";
            var message = relation.Relation switch
            {
                "Renamed" when !relation.TargetControls.Contains(id, StringComparer.OrdinalIgnoreCase) =>
                    $"Review needed: the ownership record for {was} refers to the requirement {current.Release} calls {Targets(relation)}."
                    + (currentName is null ? "" : $" {id} now means {currentName}, a different requirement.")
                    + " The record has not been moved; treat the object as unowned for the current control until it is reviewed.",
                "Replaced" or "Changed" when relation.Cardinality == "OneToMany" =>
                    $"Review needed: the ownership record for {was} cannot be attributed to an instance of {Targets(relation)} automatically. {relation.Reason}"
                    + (currentName is null || relation.TargetControls.Contains(id, StringComparer.OrdinalIgnoreCase) ? "" : $" {id} now means {currentName}, a different requirement."),
                "Retired" => $"The ownership record for {was} refers to a requirement retired in {current.Release}. {relation.Reason}",
                _ => $"Review: {was} changed in {current.Release}. {relation.Reason}"
            };
            notes.Add(new LineageNote(id, mapping.Release, relation.Relation, relation.TargetControls, message));
        }
        return notes;
    }

    /// <summary>
    /// Adds each note to the reason of the finding for the control ID the record is keyed by, and to the limitations:
    /// one line per explained record, and one line per release with no recorded lineage, however many records it holds.
    /// </summary>
    public static void Annotate(AssessmentResult result, IReadOnlyList<LineageNote> notes)
    {
        foreach (var note in notes)
            foreach (var finding in result.Findings.Where(f => string.Equals(f.ControlId, note.MappedControlId, StringComparison.OrdinalIgnoreCase)))
                finding.Reason = "Release lineage: " + note.Message + (finding.Reason.Length == 0 ? "" : " " + finding.Reason);
        foreach (var note in notes.Where(n => n.Relation != "Unknown"))
            result.Limitations.Add("Release lineage: " + note.Message);
        foreach (var release in notes.Where(n => n.Relation == "Unknown").GroupBy(n => n.SourceRelease, StringComparer.OrdinalIgnoreCase))
            result.Limitations.Add($"Release lineage: {release.Count()} ownership record(s) were made under {release.Key}, which has no recorded lineage to {result.Release} "
                + $"({string.Join(", ", release.Select(n => n.MappedControlId))}). Confirm each control ID still means the same requirement before relying on it.");
    }

    private static string Targets(LineageRelation relation) => string.Join(", ", relation.TargetControls.Zip(relation.TargetNames, (id, name) => $"{id} ({name})"));
}
