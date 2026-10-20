using System.Net;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
using BuildStandard.Graph.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OperationalReportTransportTests
{
    private const string First = "https://graph.microsoft.com/v1.0/admin/serviceAnnouncement/healthOverviews?$select=id,service,status";
    private const string Next = "https://graph.microsoft.com/v1.0/admin/serviceAnnouncement/healthOverviews?$skiptoken=opaque-synthetic-cursor";
    private static TenantSession Session() { var session = TestData.Session(); session.Scopes = ["ServiceHealth.Read.All"]; return session; }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Fixed_get_cursor_and_one_silent_renewal_preserve_request_and_actor_boundaries(bool renew)
    {
        var calls = 0; var tokens = new Tokens();
        using var client = new HttpClient(new Handler(request =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method); Assert.Equal("synthetic-token", request.Headers.Authorization?.Parameter);
            if (renew && calls == 1) return new(HttpStatusCode.Unauthorized);
            var page = calls - (renew ? 1 : 0);
            Assert.Equal(page == 1 ? First : Next, request.RequestUri!.OriginalString);
            return page == 1 ? Json("{\"value\":[{\"id\":\"Exchange\"}],\"@odata.nextLink\":\"" + Next + "\"}") : Json("{\"value\":[]}");
        }));
        using var transport = new OperationalReportTransport(client, tokens, Session(), "service-health", () => { });
        var first = await transport.ReadNextAsync(OperationalReadKind.HealthOverviews, null, default);
        Assert.True(first.HasMore); Assert.Single(first.Items);
        var last = await transport.ReadNextAsync(OperationalReadKind.HealthOverviews, null, default);
        Assert.False(last.HasMore); Assert.Empty(last.Items);
        await Assert.ThrowsAnyAsync<ToolkitException>(() => transport.ReadNextAsync(OperationalReadKind.HealthOverviews, null, default));
        Assert.Equal(renew ? 3 : 2, calls);
        Assert.Equal(renew ? new[] { false, true, false } : new[] { false, false }, tokens.Refreshes);
    }
    [Theory]
    [InlineData("host")][InlineData("path")][InlineData("expand")][InlineData("select")][InlineData("duplicate-query")]
    [InlineData("fragment")][InlineData("credentials")][InlineData("port")][InlineData("no-cursor")][InlineData("top")]
    public async Task Foreign_or_unregistered_continuation_is_refused_without_a_second_request(string fault)
    {
        var next = fault switch
        {
            "host" => Next.Replace("graph.microsoft.com", "evil.example"),
            "path" => Next.Replace("healthOverviews", "issues"),
            "expand" => Next + "&$expand=permissions",
            "select" => Next + "&$select=posts",
            "duplicate-query" => Next + "&$skiptoken=second",
            "fragment" => Next + "#fragment",
            "credentials" => Next.Replace("https://", "https://user:password@"),
            "port" => Next.Replace(".com/", ".com:444/"),
            "no-cursor" => First,
            _ => Next + "&$top=1001"
        };
        var calls = 0;
        using var client = new HttpClient(new Handler(_ => { calls++; return Json("{\"value\":[],\"@odata.nextLink\":\"" + next + "\"}"); }));
        using var transport = new OperationalReportTransport(client, new Tokens(), Session(), "service-health", () => { });
        var error = await Assert.ThrowsAnyAsync<ToolkitException>(() => transport.ReadNextAsync(OperationalReadKind.HealthOverviews, null, default));
        Assert.Equal(1, calls); Assert.DoesNotContain("opaque-synthetic-cursor", error.ToString()); Assert.DoesNotContain("password", error.ToString());
    }
    [Theory]
    [InlineData("scope")][InlineData("actor")][InlineData("kind")][InlineData("parent")][InlineData("context-after-token")]
    [InlineData("redirect")][InlineData("double-401")][InlineData("missing-value")][InlineData("duplicate-property")]
    [InlineData("null-row")][InlineData("content-type")][InlineData("content-length")][InlineData("repeated-cursor")]
    public async Task Malformed_binding_output_and_replay_are_never_empty_success(string fault)
    {
        var session = Session(); var current = true; var calls = 0;
        if (fault == "scope") session.Scopes = [];
        if (fault == "actor") session.OperatorVerified = false;
        var tokens = new Tokens(); if (fault == "context-after-token") tokens.After = () => current = false;
        using var client = new HttpClient(new Handler(_ =>
        {
            calls++;
            if (fault == "redirect") return new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://evil.example/") } };
            if (fault == "double-401") return new(HttpStatusCode.Unauthorized);
            if (fault == "missing-value") return Json("{}");
            if (fault == "duplicate-property") return Json("{\"value\":[],\"value\":[]}");
            if (fault == "null-row") return Json("{\"value\":[null]}");
            if (fault == "repeated-cursor") return Json("{\"value\":[],\"@odata.nextLink\":\"" + Next + "\"}");
            var response = Json("{\"value\":[]}");
            if (fault == "content-type") response.Content = new StringContent("<html/>", Encoding.UTF8, "text/html");
            if (fault == "content-length") response.Content.Headers.ContentLength = OperationalReportRegistry.MaximumBytes + 1;
            return response;
        }));
        await Assert.ThrowsAnyAsync<ToolkitException>(async () =>
        {
            using var transport = new OperationalReportTransport(client, tokens, session, "service-health", () => { if (!current) throw new TenantMismatchException("Synthetic context changed."); });
            var kind = fault == "kind" ? OperationalReadKind.PartnerRelationships : OperationalReadKind.HealthOverviews;
            _ = await transport.ReadNextAsync(kind, fault == "parent" ? "unsafe-parent" : null, default);
            if (fault == "repeated-cursor") _ = await transport.ReadNextAsync(kind, null, default);
        });
        Assert.Equal(fault is "scope" or "actor" or "kind" or "parent" or "context-after-token" ? 0 : fault is "double-401" or "repeated-cursor" ? 2 : 1, calls);
    }
    private sealed class Tokens : IAccessTokenProvider
    {
        public List<bool> Refreshes { get; } = []; public Action? After { get; set; }
        public Task<string> GetAccessTokenAsync(CancellationToken cancellation) => GetAccessTokenAsync(false, cancellation);
        public Task<string> GetAccessTokenAsync(bool refresh, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Refreshes.Add(refresh); After?.Invoke(); return Task.FromResult("synthetic-token"); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); } }
}
