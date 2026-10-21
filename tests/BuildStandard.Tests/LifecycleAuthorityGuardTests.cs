using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleAuthorityGuardTests
{
    private const string User = "11111111-1111-4111-8111-111111111111", Group = "22222222-2222-4222-8222-222222222222";
    private static JsonObject Fixture() => new()
    {
        ["tenantId"] = TestData.TenantA, ["accountObjectId"] = TestData.Operator, ["clientId"] = TestData.ClientId, ["complete"] = true,
        ["user"] = new JsonObject { ["id"] = User, ["displayName"] = "Synthetic target", ["userPrincipalName"] = "target@synthetic.invalid", ["userType"] = "Member", ["accountEnabled"] = true, ["onPremisesSyncEnabled"] = false },
        ["currentRoles"] = new JsonArray(), ["eligibleRoles"] = new JsonArray(), ["roleAssignableGroups"] = new JsonArray(),
        ["group"] = new JsonObject { ["id"] = Group, ["displayName"] = "Synthetic static group", ["groupTypes"] = new JsonArray(), ["securityEnabled"] = true, ["mailEnabled"] = false, ["isAssignableToRole"] = false, ["onPremisesSyncEnabled"] = null, ["assignedLicenses"] = new JsonArray() }
    };
    private static void Require(JsonObject observation, string action = "disable-account", TenantSession? session = null, TenantProfile? profile = null)
    {
        var type = typeof(BuildStandard.Engine.Execution.ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecycleAuthorityGuard"); Assert.NotNull(type);
        var method = type.GetMethod("Require",BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); Assert.NotNull(method);
        var operation = LifecycleActionRegistry.Create(action,User,action.EndsWith("member",StringComparison.Ordinal) ? Group : null);
        try { method.Invoke(null,new object[] { session ?? TestData.Session(), profile ?? TestData.Profile(), operation, observation }); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Explicit_returned_cloud_user_values_are_distinct_from_a_missing_authority_field(bool explicitNull)
    { var observed = Fixture(); if (explicitNull) observed["user"]!["onPremisesSyncEnabled"] = null; Require(observed); Require(observed,"add-member"); }
    [Theory][InlineData("tenant")][InlineData("account")][InlineData("client")][InlineData("partial")][InlineData("unverified")][InlineData("self")][InlineData("emergency")][InlineData("guest")][InlineData("synced")][InlineData("missing-authority")][InlineData("missing-roles")][InlineData("role")][InlineData("eligible")][InlineData("role-group")][InlineData("missing-enabled")]
    public void Unknown_or_protected_authority_is_refused_instead_of_becoming_safe_empty(string fault)
    {
        var observed = Fixture(); var session = TestData.Session(); var profile = TestData.Profile();
        switch (fault)
        {
            case "tenant": observed["tenantId"] = TestData.TenantB; break;
            case "account": observed["accountObjectId"] = User; break;
            case "client": observed["clientId"] = User; break;
            case "partial": observed["complete"] = false; break;
            case "unverified": session.OperatorVerified = false; break;
            case "self": session.AccountObjectId = User; session.OperatorObjectId = User; observed["accountObjectId"] = User; break;
            case "emergency": profile.Parameters.EmergencyAccountIds.Add(User); break;
            case "guest": observed["user"]!["userType"] = "Guest"; break;
            case "synced": observed["user"]!["onPremisesSyncEnabled"] = true; break;
            case "missing-authority": observed["user"]!.AsObject().Remove("onPremisesSyncEnabled"); break;
            case "missing-enabled": observed["user"]!.AsObject().Remove("accountEnabled"); break;
            case "missing-roles": observed.Remove("eligibleRoles"); break;
            case "role": observed["currentRoles"]!.AsArray().Add(new JsonObject { ["id"] = Group }); break;
            case "eligible": observed["eligibleRoles"]!.AsArray().Add(new JsonObject { ["id"] = Group }); break;
            case "role-group": observed["roleAssignableGroups"]!.AsArray().Add(new JsonObject { ["id"] = Group }); break;
        }
        Assert.ThrowsAny<ToolkitException>(() => Require(observed,session:session,profile:profile));
    }
    [Theory][InlineData("previously-synced")][InlineData("synced")][InlineData("missing-sync")][InlineData("dynamic")][InlineData("role-assignable")][InlineData("distribution")][InlineData("missing-licenses")]
    public void Unsupported_or_licence_unknown_groups_remain_blocked(string fault)
    {
        var observed = Fixture(); var group = observed["group"]!.AsObject();
        switch (fault)
        {
            case "previously-synced": group["onPremisesSyncEnabled"] = false; break;
            case "synced": group["onPremisesSyncEnabled"] = true; break;
            case "missing-sync": group.Remove("onPremisesSyncEnabled"); break;
            case "dynamic": group["groupTypes"] = new JsonArray("DynamicMembership"); break;
            case "role-assignable": group["isAssignableToRole"] = true; break;
            case "distribution": group["securityEnabled"] = false; group["mailEnabled"] = true; break;
            case "missing-licenses": group.Remove("assignedLicenses"); break;
        }
        Assert.ThrowsAny<ToolkitException>(() => Require(observed,"remove-member"));
    }
}
