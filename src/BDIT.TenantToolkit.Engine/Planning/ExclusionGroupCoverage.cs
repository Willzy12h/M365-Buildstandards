using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Collection;

namespace BDIT.TenantToolkit.Engine.Planning;

/// <summary>
/// Finds the tenant's user exclusion group and reports who is actually in it.
///
/// Exclusions used to be made policy by policy, so the exempt population was only knowable by reading every policy.
/// Once the standard's exclusion group exists, every Conditional Access candidate excludes it, and the exempt list is
/// one group an engineer can open and audit.
///
/// The deploying engineer's own exclusion is still written directly into each candidate as well. Group membership is
/// read from a capture that is minutes old and can change before the write lands, so it cannot carry the guarantee
/// that the person pressing the button will not be locked out. The group makes the population readable; the direct
/// exclusion makes it certain. When the engineer or an emergency account is missing from the group, that is reported
/// as a warning rather than silently accepted.
/// </summary>
public static class ExclusionGroupCoverage
{
    public const string UsersRole = "users";

    public sealed record Result(string? GroupId, string GroupName, IReadOnlyList<string> Warnings);

    public static Result ForUsers(StandardCatalogue standard, TenantSnapshot snapshot, ManagedObjectMappings mappings,
        TenantSession session, IReadOnlyList<string> emergencyAccountIds, NameResolver names)
    {
        var control = standard.Controls.FirstOrDefault(c => string.Equals(c.ExclusionRole, UsersRole, StringComparison.OrdinalIgnoreCase));
        if (control is null) return new Result(null, "", Array.Empty<string>());

        var mapping = mappings.Find(control.Id);
        if (mapping is null || !ProfileValidator.IsGuid(mapping.ObjectId))
            return new Result(null, control.Name, new[]
            {
                $"The exclusion group '{control.Name}' has not been created yet, so this policy names its exclusions individually. Create {control.Id} first to keep the exempt population in one readable place."
            });

        var group = snapshot.Collections.TryGetValue(control.Collection ?? "groups", out var capture)
            ? capture.Items.FirstOrDefault(i => string.Equals(i["id"]?.GetValue<string>(), mapping.ObjectId, StringComparison.OrdinalIgnoreCase))
            : null;

        var warnings = new List<string>();
        if (group is null)
        {
            warnings.Add($"The exclusion group '{control.Name}' [{mapping.ObjectId}] is recorded but was not found in the capture. Its membership could not be checked.");
            return new Result(mapping.ObjectId, control.Name, warnings);
        }

        if (group[TenantCollector.RelationshipUnknownKey] is not null || group["members"] is not JsonArray members)
        {
            warnings.Add($"The membership of '{control.Name}' could not be read, so it is not known whether the engineer and the emergency accounts are in it. Absence is not assumed.");
            return new Result(mapping.ObjectId, control.Name, warnings);
        }

        var ids = members.OfType<JsonObject>()
            .Select(m => m["id"]?.GetValue<string>() ?? "")
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var operatorId = session.OperatorObjectId ?? "";
        if (ProfileValidator.IsGuid(operatorId) && !ids.Contains(operatorId))
            warnings.Add($"You are not a member of '{control.Name}'. This policy still excludes you directly, which is what prevents a lockout, but add {session.OperatorUpn} to the group so every exemption is visible in one place.");

        foreach (var account in emergencyAccountIds.Where(id => ProfileValidator.IsGuid(id) && !ids.Contains(id)))
            warnings.Add($"Emergency access account {names.Render(JsonValue.Create(account))} is excluded by identity but is not a member of '{control.Name}'. Add it to the group so the exempt population is readable there.");

        return new Result(mapping.ObjectId, control.Name, warnings);
    }
}
