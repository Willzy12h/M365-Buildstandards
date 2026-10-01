using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExpandedGraphCollectionTests
{
    private const string Parent = "/policies/authenticationMethodsPolicy";
    private const string Methods = Parent + "/authenticationMethodConfigurations";
    private const string Profiles = Methods + "/FIDO2/passkeyProfiles";
    private sealed class Tokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => Task.FromResult("synthetic-token");
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class Script : HttpMessageHandler
    {
        public Queue<(string Path, string Json, HttpStatusCode Status)> Responses { get; } = new();
        public List<string> Reads { get; } = new();
        public void Add(string path, string json, HttpStatusCode status = HttpStatusCode.OK) => Responses.Enqueue((path, json, status));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Reads.Add(request.RequestUri!.PathAndQuery);
            var response = Responses.Dequeue();
            Assert.Equal(response.Path, request.RequestUri.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(response.Status) { Content = new StringContent(response.Json, Encoding.UTF8, "application/json") });
        }
    }
    private static GraphClient Client(Script script, params GraphRoute[] only) => new(new HttpClient(script), new Tokens(),
        TestData.TenantA, SessionMode.Assessment, GraphRouteAllowList.Only(only.Length > 0 ? only : new[]
        {
            new GraphRoute(GraphApi.V1, Parent, "Policy.Read.AuthenticationMethod", null, "authentication"),
            new GraphRoute(GraphApi.Beta, "/deviceManagement/deviceCompliancePolicies", "DeviceManagementConfiguration.Read.All", null, "compliance")
        }), new GraphClientOptions { Sleep = false }, NullLog.Instance);

    [Fact]
    public async Task Authentication_methods_are_read_from_the_documented_parent_response()
    {
        var script = new Script();
        script.Add("/v1.0" + Parent, """{"id":"authenticationMethodsPolicy","authenticationMethodConfigurations":[{"id":"Sms","state":"disabled"},{"id":"Fido2","state":"enabled"}]}""");
        var objects = await Client(script).GetAllAsync(GraphApi.V1, Methods, CancellationToken.None);
        Assert.Equal(2, objects.Count);
        Assert.Equal("Fido2", objects[1]["id"]!.GetValue<string>());
        Assert.Single(script.Reads);
    }

    [Fact]
    public async Task Passkey_profiles_are_read_from_the_Fido2_configuration_not_an_unsupported_list_endpoint()
    {
        var script = new Script();
        script.Add("/v1.0" + Methods + "/fido2", """{"id":"Fido2","defaultPasskeyProfile":"synthetic-profile","passkeyProfiles":[{"id":"synthetic-profile","name":"Synthetic profile"}]}""");
        var objects = await Client(script).GetAllAsync(GraphApi.V1, Profiles, CancellationToken.None);
        Assert.Single(objects);
        Assert.Equal("synthetic-profile", objects[0]["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"Fido2\"}")]
    [InlineData("{\"id\":\"Fido2\",\"passkeyProfiles\":null}")]
    [InlineData("{\"id\":\"Fido2\",\"passkeyProfiles\":[],\"passkeyProfiles@odata.nextLink\":\"https://graph.microsoft.com/next\"}")]
    [InlineData("{\"id\":\"Fido2\",\"passkeyProfiles\":[null]}")]
    [InlineData("{\"id\":\"Fido2\",\"passkeyProfiles\":[{\"id\":\"x\"},{\"id\":\"x\"}]}")]
    public async Task Missing_or_partial_passkey_data_never_becomes_an_empty_success(string response)
    {
        var script = new Script(); script.Add("/v1.0" + Methods + "/fido2", response);
        await Assert.ThrowsAsync<GraphRequestException>(() => Client(script).GetAllAsync(GraphApi.V1, Profiles, CancellationToken.None));
    }

    [Fact]
    public async Task A_reported_empty_array_is_a_complete_read_and_the_access_probe_uses_the_same_route()
    {
        var script = new Script(); script.Add("/v1.0" + Methods + "/fido2", """{"id":"Fido2","passkeyProfiles":[]}""");
        var page = await Client(script).GetAsync(GraphApi.V1, Profiles + "?$top=1", CancellationToken.None);
        Assert.Empty(page["value"]!.AsArray());
    }

    [Fact]
    public async Task The_parent_route_must_also_be_explicitly_allowed()
    {
        var script = new Script();
        await Assert.ThrowsAsync<WriteDeniedException>(() => Client(script,
            new GraphRoute(GraphApi.V1, Methods, "Policy.Read.AuthenticationMethod", null, "methods"))
            .GetAllAsync(GraphApi.V1, Methods, CancellationToken.None));
        Assert.Empty(script.Reads);
    }

    [Fact]
    public async Task Scheduled_actions_are_read_explicitly_with_each_rule_configuration()
    {
        var path = "/deviceManagement/deviceCompliancePolicies/" + TestData.ClientId + "/scheduledActionsForRule";
        var script = new Script();
        script.Add("/beta" + path, new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = TestData.Operator, ["ruleName"] = "PasswordRequired" }) }.ToJsonString());
        script.Add("/beta" + path + "/" + TestData.Operator + "/scheduledActionConfigurations", """{"value":[{"id":"synthetic-action","actionType":"block","gracePeriodHours":120}]}""");
        var rules = await Client(script).GetAllAsync(GraphApi.Beta, path + "?$expand=scheduledActionConfigurations", CancellationToken.None);
        Assert.Equal(120, rules[0]["scheduledActionConfigurations"]![0]!["gracePeriodHours"]!.GetValue<int>());
        Assert.Equal(2, script.Reads.Count);
        Assert.DoesNotContain(script.Reads, p => p.Contains("$expand", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_action_read_failure_does_not_return_a_partial_rule_as_success()
    {
        var path = "/deviceManagement/deviceCompliancePolicies/" + TestData.ClientId + "/scheduledActionsForRule";
        var script = new Script();
        script.Add("/beta" + path, new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = TestData.Operator }) }.ToJsonString());
        script.Add("/beta" + path + "/" + TestData.Operator + "/scheduledActionConfigurations", """{"error":{"code":"denied","message":"Synthetic denial"}}""", HttpStatusCode.Forbidden);
        await Assert.ThrowsAsync<PermissionException>(() => Client(script).GetAllAsync(GraphApi.Beta, path + "?$expand=scheduledActionConfigurations", CancellationToken.None));
    }
}
