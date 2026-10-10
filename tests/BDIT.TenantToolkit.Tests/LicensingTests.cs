using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class LicensingTests
{
    [Theory]
    [InlineData("Suspended", "Success", 25)]
    [InlineData("Enabled", "PendingActivation", 25)]
    [InlineData("Enabled", "Error", 25)]
    [InlineData("Enabled", "Success", 0)]
    public void Planner_licence_evaluator_does_not_accept_unusable_or_unprovisioned_service_plans(string status, string provisioning, int seats)
    {
        var snapshot = TestData.Snapshot(TestData.Standard()); var sku = snapshot.Collections["licences"].Items.Single();
        sku["capabilityStatus"] = status; sku["prepaidUnits"]!["enabled"] = seats; sku["servicePlans"]![0]!["provisioningStatus"] = provisioning;
        Assert.False(LicenceEvaluator.FromSnapshot(snapshot).Has("AAD_PREMIUM"));
    }
    private const string Sku = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
    private const string Service = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
    private static LicenceInventory Inventory() => new()
    {
        TenantId = TestData.TenantA, SubscriptionsComplete = true, UsersComplete = true,
        Subscriptions = new() { ToolkitJson.ParseObject($$"""{"skuId":"{{Sku}}","skuPartNumber":"SYNTHETIC_PRODUCT","capabilityStatus":"Enabled","appliesTo":"User","prepaidUnits":{"enabled":10,"warning":2,"suspended":1},"consumedUnits":3,"servicePlans":[{"servicePlanId":"{{Service}}","servicePlanName":"AAD_PREMIUM","provisioningStatus":"Success"}]}""") },
        Users = new() { ToolkitJson.ParseObject($$"""{"id":"{{TestData.Operator}}","displayName":"Synthetic Engineer","userPrincipalName":"engineer@example.invalid","userType":"Member","accountEnabled":true,"assignedLicenses":[{"skuId":"{{Sku}}","disabledPlans":[]}],"assignedPlans":[{"servicePlanId":"{{Service}}","capabilityStatus":"Enabled"}]}""") }
    };
    private static DeploymentPlan Plan() => new()
    {
        TenantId = TestData.TenantA,
        Rows = new() { new PlanRow { ControlId = "CA-001", Payload = new JsonObject { ["conditions"] = new JsonObject { ["users"] = new JsonObject { ["includeUsers"] = new JsonArray("All"), ["excludeUsers"] = new JsonArray() } } } } }
    };

    [Fact]
    public void Counts_preserve_missing_values_overassignment_and_unique_users_per_SKU()
    {
        var report = Inventory();
        var row = LicenceInventoryService.Summaries(report).Single();
        Assert.Equal(10, row.Enabled); Assert.Equal(3, row.Consumed); Assert.Equal(7, row.Available); Assert.Equal(1, row.AssignedUsers);
        Assert.Contains("differ", row.Detail);
        report.Subscriptions[0]["consumedUnits"] = 11;
        Assert.Equal(-1, LicenceInventoryService.Summaries(report).Single().Available);
        report.Subscriptions[0]["prepaidUnits"] = null; report.UsersComplete = false;
        row = LicenceInventoryService.Summaries(report).Single(); Assert.Null(row.Enabled); Assert.Null(row.Available); Assert.Null(row.AssignedUsers);
    }

    [Fact]
    public void Assignment_search_finds_name_UPN_or_ID_without_network_calls()
    {
        var report = Inventory();
        Assert.Single(LicenceInventoryService.AssignedUsers(report, Sku, "synthetic"));
        Assert.Single(LicenceInventoryService.AssignedUsers(report, Sku, "engineer@example.invalid"));
        Assert.Single(LicenceInventoryService.AssignedUsers(report, Sku, TestData.Operator));
        Assert.Empty(LicenceInventoryService.AssignedUsers(report, Sku, "nobody"));
    }

    [Theory]
    [InlineData("enabled", "Eligible in captured data")]
    [InlineData("disabled", "Assignment gap")]
    [InlineData("pending", "Assignment gap")]
    [InlineData("group", "Scope unknown")]
    [InlineData("unread", "Scope unknown")]
    [InlineData("suspended", "Unavailable")]
    [InlineData("guest", "Scope unknown")]
    public void Scope_distinguishes_capacity_assignment_provisioning_and_unresolved_targeting(string input, string expected)
    {
        var report = Inventory(); var plan = Plan();
        if (input == "disabled") report.Users[0]["assignedLicenses"]![0]!["disabledPlans"] = new JsonArray(Service);
        if (input == "pending") report.Users[0]["assignedPlans"]![0]!["capabilityStatus"] = "Suspended";
        if (input == "group") plan.Rows[0].Payload!["conditions"]!["users"]!["includeGroups"] = new JsonArray(TestData.Mam);
        if (input == "unread") report.UsersComplete = false;
        if (input == "suspended") report.Subscriptions[0]["capabilityStatus"] = "Suspended";
        if (input == "guest") report.Users[0]["userType"] = "Guest";
        var row = LicenceInventoryService.CheckScope(report, TestData.Standard(), TestData.Profile(), plan).Single(x => x.ControlId == "CA-001");
        Assert.Equal(expected, row.Status);
    }

    [Fact]
    public async Task Capture_is_read_only_tenant_bound_and_records_incomplete_user_properties()
    {
        var standard = TestData.Standard(); var graph = new FakeGraphClient(standard) { Mode = SessionMode.Assessment };
        graph.Add("/subscribedSkus", Inventory().Subscriptions.Single());
        graph.Add("/users", new JsonObject { ["id"] = TestData.Operator, ["displayName"] = "Missing licence fields" });
        var service = new LicenceInventoryService(new FixedClock());
        await Assert.ThrowsAsync<TenantMismatchException>(() => service.CaptureAsync(graph, TestData.TenantB, true, CancellationToken.None));
        Assert.Empty(graph.Reads);
        var report = await service.CaptureAsync(graph, TestData.TenantA, true, CancellationToken.None);
        Assert.True(report.SubscriptionsComplete); Assert.False(report.UsersComplete); Assert.NotNull(report.UserError);
        Assert.Empty(graph.Writes); Assert.Empty(graph.RecoveryWrites);
        Assert.Throws<TenantMismatchException>(() => LicenceInventoryService.CheckScope(report, standard, TestData.Profile(TestData.TenantB), Plan()));
    }
}
