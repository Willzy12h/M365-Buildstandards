using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleActionRegistryTests
{
    private const string User = "11111111-1111-4111-8111-111111111111", Group = "22222222-2222-4222-8222-222222222222", Sku = "33333333-3333-4333-8333-333333333333";
    private static object Create(string id, string user = User, string? group = null, string? sku = null, string[]? disabled = null)
    {
        var type = typeof(ToolkitException).Assembly.GetType("BuildStandard.Core.Safety.LifecycleActionRegistry"); Assert.NotNull(type);
        try { return type.GetMethod("Create")!.Invoke(null, new object?[] { id, user, group, sku, disabled })!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static T Field<T>(object operation, string field) => (T)operation.GetType().GetProperty(field)!.GetValue(operation)!;
    [Theory]
    [InlineData("add-member", "POST", "/groups/" + Group + "/members/$ref", "GroupMember.ReadWrite.All")]
    [InlineData("remove-member", "DELETE", "/groups/" + Group + "/members/" + User + "/$ref", "GroupMember.ReadWrite.All")]
    [InlineData("assign-license", "POST", "/users/" + User + "/assignLicense", "LicenseAssignment.ReadWrite.All")]
    [InlineData("remove-license", "POST", "/users/" + User + "/assignLicense", "LicenseAssignment.ReadWrite.All")]
    [InlineData("enable-account", "PATCH", "/users/" + User, "User.EnableDisableAccount.All")]
    [InlineData("disable-account", "PATCH", "/users/" + User, "User.EnableDisableAccount.All")]
    [InlineData("revoke-sessions", "POST", "/users/" + User + "/revokeSignInSessions", "User.RevokeSessions.All")]
    public void Only_registered_exact_routes_and_write_purposes_can_be_constructed(string id, string method, string path, string scope)
    {
        var operation = Create(id, group: id.EndsWith("member", StringComparison.Ordinal) ? Group : null, sku: id.EndsWith("license", StringComparison.Ordinal) ? Sku : null);
        Assert.Equal(operation,BuildStandard.Core.Safety.LifecycleActionRegistry.RequireRegistered((BuildStandard.Core.Safety.LifecycleOperation)operation));
        Assert.Equal(method, Field<string>(operation,"Method")); Assert.Equal(path, Field<string>(operation,"Path")); Assert.Equal(scope,Field<string>(operation,"WriteScope"));
        var body = Field<string?>(operation,"PayloadJson");
        if (id == "add-member") Assert.Equal("https://graph.microsoft.com/v1.0/directoryObjects/" + User, JsonNode.Parse(body!)!["@odata.id"]!.GetValue<string>());
        if (id == "remove-member" || id == "revoke-sessions") Assert.Null(body);
        if (id.EndsWith("account",StringComparison.Ordinal)) { var value = JsonNode.Parse(body!)!.AsObject(); Assert.Single(value); Assert.Equal(id == "enable-account",value["accountEnabled"]!.GetValue<bool>()); }
        if (id.EndsWith("license",StringComparison.Ordinal)) { var value = JsonNode.Parse(body!)!.AsObject(); Assert.Equal(2,value.Count); Assert.Equal(id == "assign-license" ? 1 : 0,value["addLicenses"]!.AsArray().Count); Assert.Equal(id == "remove-license" ? Sku : null,value["removeLicenses"]!.AsArray().SingleOrDefault()?.GetValue<string>()); }
    }
    [Theory][InlineData("unregistered")][InlineData("invalid-user")][InlineData("injected-group")][InlineData("extra-target")][InlineData("duplicate-plans")][InlineData("unused-plans")]
    public void Unknown_actions_injected_identifiers_and_unused_or_conflicting_inputs_are_refused(string fault)
    {
        Assert.ThrowsAny<ToolkitException>(() => Create(fault == "unregistered" ? "arbitrary-write" : fault == "duplicate-plans" ? "assign-license" : "disable-account",
            user: fault == "invalid-user" ? User + "/owners" : User,
            group: fault == "injected-group" ? Group + "/members" : fault == "extra-target" ? Group : null,
            sku: fault == "duplicate-plans" ? Sku : null,
            disabled: fault == "duplicate-plans" ? [Group,Group] : fault == "unused-plans" ? [Group] : null));
    }
    [Theory][InlineData("path")][InlineData("method")][InlineData("scope")][InlineData("user")][InlineData("body")]
    public void Reconstituted_or_modified_request_descriptions_cannot_escape_registry(string fault)
    {
        var original = BuildStandard.Core.Safety.LifecycleActionRegistry.Create("disable-account",User);
        var changed = fault switch
        {
            "path" => original with { Path = "/users/" + Group }, "method" => original with { Method = "DELETE" },
            "scope" => original with { WriteScope = "Directory.ReadWrite.All" }, "user" => original with { UserId = Group },
            _ => original with { PayloadJson = "{\"accountEnabled\":false,\"passwordProfile\":{}}" }
        };
        var method = typeof(BuildStandard.Core.Safety.LifecycleActionRegistry).GetMethod("RequireRegistered"); Assert.NotNull(method);
        var failure = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,new object[] {changed}));
        Assert.IsAssignableFrom<ToolkitException>(failure.InnerException);
        Assert.Equal(original,method.Invoke(null,new object[] {original}));
    }
}
