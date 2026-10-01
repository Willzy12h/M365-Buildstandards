using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class TenantDiscoveryTests
{
    private static SignInOutcome Identity(string tenant = TestData.TenantA, string account = TestData.Operator) => new()
    { TenantId = tenant, AccountObjectId = account, Account = "engineer@synthetic.example", Scopes = new[] { "User.Read", "Organization.Read.All" } };

    private static FakeGraphClient Graph(string organisation = TestData.TenantA, string account = TestData.Operator)
    {
        var graph = new FakeGraphClient(new StandardCatalogue()) { Mode = SessionMode.Assessment };
        graph.SetSingleton("/organization", new JsonObject { ["value"] = new JsonArray(new JsonObject
        {
            ["id"] = organisation, ["displayName"] = "Synthetic client",
            ["verifiedDomains"] = new JsonArray(new JsonObject { ["name"] = "synthetic.example", ["isDefault"] = true })
        }) });
        graph.SetSingleton("/me", new JsonObject { ["id"] = account, ["userPrincipalName"] = "engineer@synthetic.example" });
        return graph;
    }

    [Fact]
    public async Task Discovery_reads_only_identity_and_produces_a_fresh_unconfigured_profile()
    {
        var graph = Graph();
        var found = await TenantDiscoveryService.ReadIdentityAsync(graph, Identity(), CancellationToken.None);
        Assert.Equal("Synthetic client", found.Name);
        Assert.Equal("synthetic.example", found.Domain);
        Assert.Equal("engineer@synthetic.example", found.Account);
        Assert.False(found.TokenHasWriteScopes);
        Assert.Equal(new[] { "/organization?$select=id,displayName,verifiedDomains", "/me?$select=id,displayName,userPrincipalName" }, graph.Reads);
        Assert.Empty(graph.Writes);
        var profile = found.NewProfile();
        Assert.Equal(TestData.TenantA, profile.TenantId);
        Assert.Empty(profile.AssessmentClientId); Assert.Empty(profile.DeploymentClientId);
        Assert.Empty(profile.ExclusionAccounts); Assert.Empty(profile.Parameters.EmergencyAccountIds);
        Assert.Empty(profile.Parameters.AdditionalExclusionAccountIds);
    }

    [Theory]
    [InlineData(TestData.TenantB, TestData.Operator)]
    [InlineData(TestData.TenantA, TestData.Emergency)]
    [InlineData(TestData.TenantA, "")]
    public async Task A_different_organisation_or_operator_cannot_be_presented_for_confirmation(string organisation, string account)
    {
        var graph = Graph(organisation, account);
        await Assert.ThrowsAsync<TenantMismatchException>(() => TenantDiscoveryService.ReadIdentityAsync(graph, Identity(), CancellationToken.None));
        Assert.Empty(graph.Writes);
    }

    [Theory]
    [InlineData("", TestData.Operator)]
    [InlineData("organizations", TestData.Operator)]
    [InlineData(TestData.TenantA, "")]
    [InlineData(TestData.TenantB, TestData.Operator)]
    public async Task Invalid_or_unpinned_token_identity_never_reaches_Graph(string tenant, string account)
    {
        var graph = Graph();
        await Assert.ThrowsAsync<TenantMismatchException>(() => TenantDiscoveryService.ReadIdentityAsync(graph, Identity(tenant, account), CancellationToken.None));
        Assert.Empty(graph.Reads);
    }

    [Fact]
    public async Task Ambiguous_organisation_is_rejected_before_reading_the_operator()
    {
        var graph = Graph();
        graph.SetSingleton("/organization", new JsonObject { ["value"] = new JsonArray(new JsonObject(), new JsonObject()) });
        await Assert.ThrowsAsync<TenantMismatchException>(() => TenantDiscoveryService.ReadIdentityAsync(graph, Identity(), CancellationToken.None));
        Assert.Single(graph.Reads);
    }

    [Fact]
    public async Task Cancellation_performs_no_discovery_reads()
    {
        var graph = Graph();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TenantDiscoveryService.ReadIdentityAsync(graph, Identity(), stop.Token));
        Assert.Empty(graph.Reads);
    }

    [Fact]
    public async Task Disabling_shared_fallback_prevents_discovery_before_authentication()
    {
        using var http = new HttpClient();
        await Assert.ThrowsAsync<ConfigurationException>(() => TenantDiscoveryService.DiscoverAsync(http,
            new ToolkitSettings { AllowMicrosoftGraphPowerShellFallback = false }, IntPtr.Zero, NullLog.Instance, CancellationToken.None));
    }

    [Theory]
    [InlineData("organizations")]
    [InlineData("")]
    public async Task Ordinary_sign_in_still_refuses_an_unpinned_tenant(string tenant)
    {
        await Assert.ThrowsAsync<ConfigurationException>(() => MsalAuthenticator.SignInAsync(new SignInRequest
        { TenantId = tenant, ClientId = TestData.ClientId, Scopes = new[] { "User.Read" } }, NullLog.Instance, CancellationToken.None));
    }

    [Fact]
    public void Discovery_routes_have_no_write_permissions_or_policy_reads()
    {
        var routes = TenantDiscoveryService.Routes();
        Assert.Equal(2, routes.Routes.Count);
        Assert.All(routes.Routes, r => Assert.False(r.Writable));
        Assert.Null(routes.MatchRead(GraphApi.V1, "/users"));
        Assert.Null(routes.MatchWrite(GraphApi.V1, "/organization", out _));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("operator")]
    [InlineData("token")]
    [InlineData("unverified-tenant")]
    [InlineData("unverified-operator")]
    [InlineData("deployment")]
    public async Task Confirmation_cannot_authorise_a_changed_account_tenant_or_mode(string change)
    {
        var found = await TenantDiscoveryService.ReadIdentityAsync(Graph(), Identity(), CancellationToken.None);
        var session = new TenantSession { Mode = SessionMode.Assessment, TenantId = TestData.TenantA,
            AccountObjectId = TestData.Operator, OperatorObjectId = TestData.Operator, TenantVerified = true, OperatorVerified = true };
        found.VerifyConnection(session);
        switch (change)
        {
            case "tenant": session.TenantId = TestData.TenantB; break;
            case "operator": session.OperatorObjectId = TestData.Emergency; break;
            case "token": session.AccountObjectId = ""; break;
            case "unverified-tenant": session.TenantVerified = false; break;
            case "unverified-operator": session.OperatorVerified = false; break;
            case "deployment": session.Mode = SessionMode.Deployment; break;
        }
        Assert.Throws<TenantMismatchException>(() => found.VerifyConnection(session));
    }
}
