using System.Net;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using Microsoft.Identity.Client;
using Xunit;

namespace BuildStandard.Tests;

public sealed class PermissionAcquisitionTests
{
    [Theory]
    [InlineData(true, 1, false, 1, 0)]
    [InlineData(true, 1, true, 1, 1)]
    [InlineData(true, 0, false, 0, 1)]
    [InlineData(true, 2, false, 0, 1)]
    [InlineData(false, 1, false, 0, 1)]
    public async Task Access_preview_occurs_only_at_the_explicit_interactive_boundary(bool known, int accounts,
        bool expired, int expectedSilent, int expectedInteraction)
    {
        var silent = 0; var approvals = 0; var interactive = 0;
        var result = await ExplicitConnectAcquisition.AcquireAsync(known, Enumerable.Range(0, accounts).ToArray(),
            (_, token) => { silent++; if (expired) throw new MsalUiRequiredException("interaction_required", "synthetic"); return Task.FromResult("cached"); },
            (_, token) => { Assert.Equal(1, approvals); interactive++; return Task.FromResult("selected"); }, CancellationToken.None,
            _ => { approvals++; return Task.CompletedTask; });
        Assert.Equal(expectedSilent, silent); Assert.Equal(expectedInteraction, approvals); Assert.Equal(expectedInteraction, interactive);
        Assert.Equal(expectedInteraction == 0 ? "cached" : "selected", result);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Refused_or_cancelled_access_preview_never_reaches_Microsoft_interaction(bool afterCache)
    {
        var interactive = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExplicitConnectAcquisition.AcquireAsync(afterCache,
            afterCache ? new[] { "known" } : Array.Empty<string>(),
            (_, token) => Task.FromException<string>(new MsalUiRequiredException("interaction_required", "synthetic")),
            (_, token) => { interactive++; return Task.FromResult("not allowed"); }, CancellationToken.None,
            _ => throw new OperationCanceledException("Engineer declined the displayed request")));
        Assert.Equal(0, interactive);
    }

    [Theory]
    [InlineData("User.Read", true)] [InlineData("https://graph.microsoft.com/user.read", true)]
    [InlineData("Directory.Read.All", false)] [InlineData("User.Read,Directory.Read.All", false)] [InlineData("", false)]
    public void Actual_returned_scope_changes_never_reuse_initial_authorisation(string actual, bool same)
    {
        Assert.Equal(same, MsalAuthenticator.SameAuthorisationScopes(new[] { "User.Read" },
            actual.Length == 0 ? Array.Empty<string>() : actual.Split(',')));
    }

    private sealed class ReturnedAccount : IAccount
    {
        public string Username => "engineer@synthetic.example";
        public string Environment => "login.microsoftonline.com";
        public AccountId HomeAccountId { get; } = new("synthetic-account", TestData.Operator, TestData.TenantA);
    }

    private sealed class IdentityReads : HttpMessageHandler
    {
        public int Reads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method); Reads++;
            var path = request.RequestUri!.AbsolutePath;
            var body = path switch
            {
                "/v1.0/organization" => new JsonObject { ["value"] = new JsonArray(new JsonObject { ["id"] = TestData.TenantA, ["displayName"] = "Synthetic client" }) },
                "/v1.0/me" => new JsonObject { ["id"] = TestData.Operator, ["userPrincipalName"] = "engineer@synthetic.example" },
                _ => throw new InvalidOperationException("Unregistered synthetic read: " + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString()) });
        }
    }

    [Theory]
    [InlineData("User.Read", false, true)]
    [InlineData("User.Invite.All", false, false)]
    [InlineData("Mail.Send", false, false)]
    [InlineData("Future.Read.All", false, false)]
    [InlineData("User.Read", true, true)]
    [InlineData("User.Invite.All", true, true)]
    [InlineData("Future.Read.All", true, true)]
    public async Task Verified_connection_enforces_returned_capability_policy_without_new_requests(string returnedScope, bool shared, bool accepted)
    {
        using var root = new TempRoot(); using var handler = new IdentityReads(); using var http = new HttpClient(handler);
        var settings = new ToolkitSettings { AssessmentClientId = shared ? "" : TestData.ClientId };
        var service = new TenantConnectionService(settings, root.Paths, http, NullLog.Instance, "synthetic");
        var standard = TestData.Standard(); var acquisitions = 0;
        Task<MsalAuthenticator> Acquire(SignInRequest request, IToolkitLog log, CancellationToken token)
        {
            acquisitions++; Assert.Equal(service.ScopesFor(SessionMode.Assessment, standard), request.Scopes);
            Assert.DoesNotContain("User.Invite.All", request.Scopes); Assert.DoesNotContain("Mail.Send", request.Scopes);
            Assert.DoesNotContain("Future.Read.All", request.Scopes);
            var result = new AuthenticationResult("synthetic-token", false, TestData.Operator,
                DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(1), TestData.TenantA,
                new ReturnedAccount(), "", new[] { returnedScope }, Guid.Empty);
            var app = PublicClientApplicationBuilder.Create(request.ClientId).WithAuthority("https://login.microsoftonline.com/" + TestData.TenantA).Build();
            return Task.FromResult(new MsalAuthenticator(app, request.Scopes.ToArray(), TestData.TenantA, "", log,
                result, request.ClientId, (_, _) => throw new InvalidOperationException("No renewal in this test")));
        }
        Task<ConnectedTenant> Connect() => service.ConnectAsync(TestData.Profile(), SessionMode.Assessment, standard,
            null, CancellationToken.None, null, null, Acquire);
        if (!accepted) await Assert.ThrowsAsync<ConfigurationException>(Connect);
        else
        {
            var connected = await Connect();
            Assert.True(connected.Session.TenantVerified); Assert.True(connected.Session.OperatorVerified);
            Assert.Equal(SessionMode.Assessment, connected.Session.Mode);
            if (returnedScope != "User.Read") Assert.Contains(connected.Session.Notices, n => n.Contains("toolkit changes blocked", StringComparison.Ordinal));
            await Assert.ThrowsAsync<WriteDeniedException>(() => connected.Graph.WriteAsync(GraphApi.V1, GraphWriteMethod.Post,
                "/identity/conditionalAccess/policies", new JsonObject(), CancellationToken.None));
            await connected.ReleaseAsync();
        }
        Assert.Equal(1, acquisitions); Assert.Equal(2, handler.Reads);
    }
}
