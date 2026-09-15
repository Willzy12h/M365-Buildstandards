using System.Text.Json.Nodes;

namespace BDIT.TenantToolkit.Core.Models;

public sealed class LicenceInventory
{
    public string TenantId { get; set; } = "";
    public string CapturedAt { get; set; } = "";
    public bool SubscriptionsComplete { get; set; }
    public bool UsersComplete { get; set; }
    public string? SubscriptionError { get; set; }
    public string? UserError { get; set; }
    public List<JsonObject> Subscriptions { get; set; } = new();
    public List<JsonObject> Users { get; set; } = new();
}

public sealed record LicenceSummary(string SkuId, string Product, string Status, string AppliesTo, int? Enabled,
    int? Warning, int? Suspended, int? Consumed, int? Available, int? AssignedUsers, string Detail);
public sealed record LicensedUser(string ObjectId, string Name, string UserPrincipalName, string AccountStatus, string DisabledPlans);
public sealed record LicenceScopeResult(string ControlId, string Name, string Status, int? TargetUsers, int? EligibleUsers, string Detail);
