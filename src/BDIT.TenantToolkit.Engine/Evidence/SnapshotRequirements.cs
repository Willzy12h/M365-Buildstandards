using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Collection;

namespace BDIT.TenantToolkit.Engine.Evidence;

/// <summary>Checks the evidence itself; a caller-provided Complete flag is insufficient.</summary>
public static class SnapshotRequirements
{
    public static string? IncompleteReason(TenantSnapshot snapshot, StandardCatalogue standard)
    {
        if (!snapshot.Complete) return "The before-change snapshot is incomplete. Capture every required collection successfully before deploying.";
        if (!string.Equals(snapshot.StandardRelease, standard.Release, StringComparison.Ordinal))
            return "The snapshot was captured against a different standard release. Capture it again.";
        if (snapshot.Collections.Values.Any(c => !c.Usable))
            return "A collection in the before-change snapshot failed or has incomplete details. Capture it again.";
        foreach (var (key, def) in standard.Collections)
        {
            if (!snapshot.Collections.TryGetValue(key, out var capture) || !capture.Usable)
                return $"The before-change snapshot does not contain complete {def.Label} evidence.";
            if (capture.Api != def.Api || capture.Path != def.Path)
                return $"The captured route for {def.Label} differs from the current standard. Capture it again.";
            foreach (var item in capture.Items)
            {
                if (item.ContainsKey(TenantCollector.AssignmentsUnknownKey) || item.ContainsKey(TenantCollector.SettingsUnknownKey)
                    || item.ContainsKey(TenantCollector.RelationshipUnknownKey))
                    return $"The {def.Label} evidence contains an unread object detail.";
                if (def.Assignments && item[TenantCollector.AssignmentsKey] is not JsonArray)
                    return $"The {def.Label} evidence is missing assignments.";
                if (!string.IsNullOrEmpty(def.Children) && item[TenantCollector.SettingsKey] is not JsonArray)
                    return $"The {def.Label} evidence is missing child settings.";
                if (!string.IsNullOrEmpty(def.Relationship) && item[def.Relationship.Split('?')[0].Trim('/')] is not JsonArray)
                    return $"The {def.Label} evidence is missing related settings.";
            }
        }
        return null;
    }

    public static void AssertComplete(TenantSnapshot snapshot, StandardCatalogue standard)
    {
        var reason = IncompleteReason(snapshot, standard);
        if (reason is not null) throw new PlanValidationException(reason);
        if (snapshot.Collections.Values.Any(c => c.Count != c.Items.Count))
            throw new PlanValidationException("A captured collection count differs from its saved objects. Capture a new snapshot.");
    }
}
