using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Graph;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Assessment;

public sealed class LicenceInventoryService(IClock clock)
{
    public async Task<LicenceInventory> CaptureAsync(IGraphClient graph, string tenantId, bool includeUsers, CancellationToken ct)
    {
        if (!ProfileValidator.IsGuid(tenantId) || !string.Equals(graph.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The licence report must match the connected tenant.");
        var result = new LicenceInventory { TenantId = tenantId, CapturedAt = Timestamps.Format(clock.UtcNow) };
        try
        {
            result.Subscriptions = (await graph.GetAllAsync(GraphApi.V1, "/subscribedSkus", ct)).Select(x => (JsonObject)x.DeepClone()).ToList();
            result.SubscriptionsComplete = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { result.SubscriptionError = SensitiveDataScrubber.Scrub(ex.Message); }
        if (includeUsers)
        {
            try
            {
                // Read ordinary selected properties with complete pagination; local search needs no advanced Graph query.
                result.Users = (await graph.GetAllAsync(GraphApi.V1,
                    "/users?$select=id,displayName,userPrincipalName,accountEnabled,userType,assignedLicenses,assignedPlans", ct)).Select(x => (JsonObject)x.DeepClone()).ToList();
                result.UsersComplete = result.Users.All(u => ProfileValidator.IsGuid(Text(u, "id")) && u["assignedLicenses"] is JsonArray && u["assignedPlans"] is JsonArray);
                if (!result.UsersComplete) result.UserError = "Some users did not return licence or service-plan properties. Assignment and scope results are incomplete.";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.UserError = SensitiveDataScrubber.Scrub(ex.Message); }
        }
        return result;
    }

    public static IReadOnlyList<LicenceSummary> Summaries(LicenceInventory inventory) => inventory.Subscriptions.Select(s =>
    {
        var enabled = Number(s["prepaidUnits"]?["enabled"]);
        var warning = Number(s["prepaidUnits"]?["warning"]);
        var suspended = Number(s["prepaidUnits"]?["suspended"]);
        var consumed = Number(s["consumedUnits"]);
        var skuId = Text(s, "skuId");
        int? userCount = inventory.UsersComplete ? inventory.Users.Count(u => HasSku(u, skuId)) : null;
        var available = enabled.HasValue && consumed.HasValue ? enabled - consumed : null;
        var detail = "Available = enabled seats minus consumed seats. Counts are per SKU; summing assignments does not count unique people.";
        if (available < 0) detail += " Assigned seats exceed enabled capacity.";
        if (userCount.HasValue && consumed.HasValue && userCount != consumed) detail += " User and subscription counts differ; replication delays or non-user licensing may apply.";
        return new LicenceSummary(skuId, Text(s, "skuPartNumber"), Text(s, "capabilityStatus"), Text(s, "appliesTo"),
            enabled, warning, suspended, consumed, available, userCount, detail);
    }).OrderBy(s => s.Product, StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<LicensedUser> AssignedUsers(LicenceInventory inventory, string skuId, string search) => inventory.Users
        .Where(u => HasSku(u, skuId))
        .Select(u => new LicensedUser(Text(u, "id"), Text(u, "displayName"), Text(u, "userPrincipalName"),
            u["accountEnabled"] is JsonValue b && b.TryGetValue<bool>(out var enabled) ? enabled ? "Enabled" : "Disabled" : "Unknown",
            string.Join(", ", (u["assignedLicenses"] as JsonArray ?? new()).OfType<JsonObject>().Where(l => Text(l, "skuId") == skuId)
                .SelectMany(l => Strings(l["disabledPlans"])))))
        .Where(u => string.IsNullOrWhiteSpace(search) || (u.Name + " " + u.UserPrincipalName + " " + u.ObjectId).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static IReadOnlyList<LicenceScopeResult> CheckScope(LicenceInventory inventory, StandardCatalogue standard, TenantProfile profile, DeploymentPlan? plan)
    {
        if (!string.Equals(inventory.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase)
            || (plan is not null && !string.Equals(plan.TenantId, inventory.TenantId, StringComparison.OrdinalIgnoreCase)))
            throw new TenantMismatchException("Licence evidence, profile and plan must belong to the same tenant.");
        var results = new List<LicenceScopeResult>();
        foreach (var control in standard.Controls.Where(c => c.Licence.ServicePlans.Count > 0))
        {
            if (!inventory.SubscriptionsComplete) { results.Add(Result(control, "Unknown", null, null, "Subscription reads failed.")); continue; }
            var required = control.Licence.ServicePlans;
            var provisioned = inventory.Subscriptions.Where(UsableSku).SelectMany(ServicePlans)
                .Where(p => Text(p, "provisioningStatus") == "Success").Select(p => Text(p, "servicePlanName")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = required.Where(p => !provisioned.Contains(p)).ToList();
            if (missing.Count > 0) { results.Add(Result(control, "Unavailable", null, null, "No confirmed active provisioned service plan: " + string.Join(", ", missing))); continue; }
            if (!inventory.UsersComplete) { results.Add(Result(control, "Scope unknown", null, null, "Tenant service plans found. Load user assignments to check individual eligibility.")); continue; }
            var payload = plan?.Rows.FirstOrDefault(r => r.ControlId == control.Id)?.Payload;
            if (payload?["conditions"]?["users"] is not JsonObject targeting)
            { results.Add(Result(control, "Scope unknown", null, null, "Tenant service plans found; no resolved user targeting is available. Intune device/group targeting needs a separate assignment review.")); continue; }
            if (new[] { "includeGroups", "excludeGroups", "includeRoles", "excludeRoles" }.Any(k => Strings(targeting[k]).Any())
                || targeting.ContainsKey("includeGuestsOrExternalUsers") || targeting.ContainsKey("excludeGuestsOrExternalUsers"))
            { results.Add(Result(control, "Scope unknown", null, null, "Group, role or external-user targeting requires membership/licensing review; it is not inferred from subscription counts.")); continue; }
            var included = Strings(targeting["includeUsers"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var excluded = Strings(targeting["excludeUsers"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (included.Count == 0 || included.Any(id => id != "All" && id != "None" && !ProfileValidator.IsGuid(id)))
            { results.Add(Result(control, "Scope unknown", null, null, "Unsupported or missing user targeting.")); continue; }
            var knownIds = inventory.Users.Select(u => Text(u, "id")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (included.Where(ProfileValidator.IsGuid).Any(id => !knownIds.Contains(id)))
            { results.Add(Result(control, "Scope unknown", null, null, "A targeted user is missing from the completed directory read.")); continue; }
            var users = inventory.Users.Where(u => (included.Contains("All") || included.Contains(Text(u, "id"))) && !excluded.Contains(Text(u, "id"))).ToList();
            if (users.Any(u => Text(u, "userType") == "Guest"))
            { results.Add(Result(control, "Scope unknown", users.Count, null, "Guest/external-user licensing requires a separate tenant-specific review.")); continue; }
            var eligible = users.Count(u => required.All(p => UserHasPlan(inventory, u, p)));
            results.Add(Result(control, users.Count == 0 ? "No target users" : eligible == users.Count ? "Eligible in captured data" : "Assignment gap", users.Count, eligible,
                $"{eligible} of {users.Count} directly resolved target users have all required enabled service plans. This checks captured assignments, not contractual entitlement or effective policy enforcement."));
        }
        return results;
    }

    private static bool UserHasPlan(LicenceInventory inventory, JsonObject user, string name) => inventory.Subscriptions.Where(UsableSku)
        .Any(s => (user["assignedLicenses"] as JsonArray ?? new()).OfType<JsonObject>().Any(l => Text(l, "skuId") == Text(s, "skuId")
            && ServicePlans(s).Any(p => Text(p, "servicePlanName").Equals(name, StringComparison.OrdinalIgnoreCase) && Text(p, "provisioningStatus") == "Success"
                && !Strings(l["disabledPlans"]).Contains(Text(p, "servicePlanId"), StringComparer.OrdinalIgnoreCase)
                && (user["assignedPlans"] as JsonArray ?? new()).OfType<JsonObject>().Any(a => Text(a, "servicePlanId") == Text(p, "servicePlanId") && Text(a, "capabilityStatus") == "Enabled"))));
    private static bool UsableSku(JsonObject s) => Text(s, "capabilityStatus") == "Enabled" && Number(s["prepaidUnits"]?["enabled"]) > 0;
    private static IEnumerable<JsonObject> ServicePlans(JsonObject sku) => (sku["servicePlans"] as JsonArray ?? new()).OfType<JsonObject>();
    private static bool HasSku(JsonObject user, string id) => (user["assignedLicenses"] as JsonArray ?? new()).OfType<JsonObject>().Any(l => Text(l, "skuId").Equals(id, StringComparison.OrdinalIgnoreCase));
    private static string Text(JsonObject value, string key) => value[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "Unknown";
    private static int? Number(JsonNode? value) => value is JsonValue v && v.TryGetValue<int>(out var n) ? n : null;
    private static IEnumerable<string> Strings(JsonNode? value) => (value as JsonArray ?? new()).OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var s) ? s : "");
    private static LicenceScopeResult Result(ControlDefinition c, string status, int? count, int? eligible, string detail) => new(c.Id, c.Name, status, count, eligible, detail);
}
