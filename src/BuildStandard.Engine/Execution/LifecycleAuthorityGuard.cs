using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
namespace BuildStandard.Engine.Execution;

/// <summary>
/// INT-137 target checks over a completed, fixed-source capture. This internal check grants no approval:
/// the lifecycle service must establish capture origin/completeness, managed snapshot, freshness and identity separately.
/// Reports, imported JSON and a caller's claimed complete flag cannot become execution authority.
/// </summary>
internal static class LifecycleAuthorityGuard
{
    internal static void Require(TenantSession session, TenantProfile profile, LifecycleOperation operation, JsonObject observed)
    {
        _ = LifecycleActionRegistry.RequireRegistered(operation);
        if (!session.TenantVerified || !session.OperatorVerified || !GuidId(session.AccountObjectId)
            || !Same(session.OperatorObjectId,session.AccountObjectId) || !GuidId(session.ClientId)
            || !Same(profile.TenantId,session.TenantId) || !Same(Text(observed,"tenantId"),session.TenantId)
            || !Same(Text(observed,"accountObjectId"),session.AccountObjectId) || !Same(Text(observed,"clientId"),session.ClientId))
            throw Refuse("Verified tenant, operator, registration and captured-source identity must match.");
        if (!Bool(observed,"complete")) throw Refuse("Required lifecycle authority reads are incomplete; an empty subset cannot establish readiness.");
        foreach (var key in new[] { "currentRoles", "eligibleRoles", "roleAssignableGroups" })
        {
            if (observed[key] is not JsonArray rows) throw Refuse("Required directory privilege/eligibility inventory is missing or unknown.");
            if (rows.Count != 0) throw Refuse(key == "roleAssignableGroups"
                ? "A role-assignable group exists. Without approved PIM-for-Groups eligibility access, privileged eligibility cannot be excluded; lifecycle actions remain unavailable."
                : "Current or eligible directory role authority cannot be treated as an ordinary user. Lifecycle actions remain unavailable.");
        }
        if (observed["user"] is not JsonObject user || !Same(Text(user,"id"),operation.UserId)
            || Text(user,"userType") != "Member") throw Refuse("Choose the exact existing member user; unidentified, guest and deleted targets are unsupported.");
        _ = Text(user,"displayName"); _ = Text(user,"userPrincipalName"); _ = Bool(user,"accountEnabled");
        if (!user.ContainsKey("onPremisesSyncEnabled") || user["onPremisesSyncEnabled"] is not null && Bool(user,"onPremisesSyncEnabled"))
            throw Refuse("User synchronisation authority is missing, unknown or currently on-premises managed.");
        if (user.TryGetPropertyValue("deletedDateTime",out var deleted) && deleted is not null) throw Refuse("Deleted users are unsupported.");
        if (Same(operation.UserId,session.AccountObjectId) || Same(operation.UserId,session.OperatorObjectId)
            || profile.Parameters.EmergencyAccountIds.Concat(profile.Parameters.AdditionalExclusionAccountIds).Any(id => Same(id,operation.UserId))
            || profile.ExclusionAccounts.Any(account => Same(account.ObjectId,operation.UserId)))
            throw Refuse("The signed-in operator and configured emergency/protected accounts cannot be lifecycle targets.");
        if (operation.GroupId is null) return;
        if (observed["group"] is not JsonObject group || !Same(Text(group,"id"),operation.GroupId)) throw Refuse("The exact selected group was not completely observed.");
        _ = Text(group,"displayName");
        // Microsoft group semantics differ from users: false means previously synced; explicit null means never synced.
        if (!group.ContainsKey("onPremisesSyncEnabled") || group["onPremisesSyncEnabled"] is not null)
            throw Refuse("Only groups explicitly reported as never synchronised are supported; current, former and unknown synchronisation stay blocked.");
        if (Bool(group,"isAssignableToRole")) throw Refuse("Role-assignable group membership is unsupported.");
        if (group["groupTypes"] is not JsonArray types) throw Refuse("Group population type is unknown.");
        var kinds = types.Select(value => value is JsonValue scalar && scalar.TryGetValue<string>(out var kind) ? kind : null).ToArray();
        if (kinds.Any(kind => kind != "Unified") || kinds.Length > 1) throw Refuse("Dynamic or unknown group populations are unsupported.");
        var unified = kinds.Length == 1; var security = Bool(group,"securityEnabled"); var mail = Bool(group,"mailEnabled");
        if (unified ? !mail : !security || mail) throw Refuse("Distribution, mail-enabled security and unidentified group types are not supported Graph membership targets.");
        if (group["assignedLicenses"] is not JsonArray licenses) throw Refuse("Group licensing effects are unknown; membership changes remain blocked.");
        var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in licenses)
        {
            if (entry is not JsonObject license || !GuidId(Text(license,"skuId")) || !skus.Add(Text(license,"skuId"))
                || license["disabledPlans"] is not JsonArray plans) throw Refuse("Group licence assignments are missing, invalid or ambiguous.");
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in plans)
                if (plan is not JsonValue scalar || !scalar.TryGetValue<string>(out var id) || !GuidId(id) || !ids.Add(id))
                    throw Refuse("Group service-plan state is unknown or ambiguous.");
        }
    }
    private static string Text(JsonObject value,string key)
    {
        if (value[key] is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > 1024)
            throw Refuse("A required lifecycle identity field is missing or invalid: " + key);
        return text;
    }
    private static bool Bool(JsonObject value,string key)
    {
        if (value[key] is not JsonValue scalar || !scalar.TryGetValue<bool>(out var flag)) throw Refuse("A required lifecycle boolean is missing or unknown: " + key);
        return flag;
    }
    private static bool GuidId(string? text) => Guid.TryParseExact(text,"D",out var value) && value != Guid.Empty;
    private static bool Same(string? left,string? right) => string.Equals(left,right,StringComparison.OrdinalIgnoreCase);
    private static SafetyViolationException Refuse(string message) => new(message);
}
