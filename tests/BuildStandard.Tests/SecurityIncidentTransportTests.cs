using System.Net;
using System.Text;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Graph;
using BuildStandard.Core.Reporting;
using BuildStandard.Graph.Auth;
using BuildStandard.Graph.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentTransportTests
{
    private static SecurityIncidentReadSelection Selection() { var end = new DateTimeOffset(2026,10,20,12,0,0,TimeSpan.Zero); return new(end.AddDays(-1), end, end); }
    private static OperationalReportTransport Transport(HttpClient client, Tokens tokens, string scope = "SecurityIncident.Read.All", Action? current = null)
    {
        var session = TestData.Session(); session.Scopes = [scope];
        return new OperationalReportTransport(client, tokens, session, "security-incidents", current ?? (() => {}), incidents: Selection());
    }
    private static Task<OperationalReportPage> Read(OperationalReportTransport source) => source.ReadIncidentsNextAsync(CancellationToken.None);
    private static HttpResponseMessage Page(string? next = null)
    {
        var body = new Dictionary<string,object?> { ["value"] = new[] { new { id = "123" } } };
        if (next is not null) body["@odata.nextLink"] = next;
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Exact_incident_window_is_retained_across_pages_and_single_silent_renewal(bool renew)
    {
        var calls = 0; var tokens = new Tokens(); var first = "https://graph.microsoft.com/v1.0" + Selection().RegisteredPath; var next = first + "&$skip=100";
        using var client = new HttpClient(new Handler(request =>
        {
            calls++; Assert.Equal(HttpMethod.Get, request.Method);
            if (renew && calls == 1) return new(HttpStatusCode.Unauthorized);
            var page = calls - (renew ? 1 : 0); Assert.Equal(page == 1 ? first : next, request.RequestUri!.OriginalString);
            return Page(page == 1 ? next : null);
        }));
        using var source = Transport(client, tokens); Assert.True((await Read(source)).HasMore); Assert.False((await Read(source)).HasMore);
        await Assert.ThrowsAnyAsync<ToolkitException>(() => Read(source)); Assert.Equal(renew ? 3 : 2, calls); Assert.Equal(renew ? 1 : 0, tokens.Renewals);
    }
    [Theory][InlineData("host")][InlineData("path")][InlineData("missing-filter")][InlineData("changed-filter")][InlineData("expand")][InlineData("select")][InlineData("top")]
    public async Task Changed_or_unbounded_continuation_is_refused_without_following_it(string fault)
    {
        var calls = 0; var first = "https://graph.microsoft.com/v1.0" + Selection().RegisteredPath; var next = first + "&$skip=100";
        next = fault switch
        {
            "host" => next.Replace("graph.microsoft.com", "elsewhere.example"),
            "path" => next.Replace("security/incidents", "security/alerts_v2"),
            "missing-filter" => "https://graph.microsoft.com/v1.0/security/incidents?$skip=100",
            "changed-filter" => next.Replace("2026-10-19", "2026-10-18"),
            "expand" => next + "&$expand=alerts", "select" => next + "&$select=id", "top" => next.Replace("$top=100", "$top=1001"), _ => next
        };
        using var client = new HttpClient(new Handler(_ => { calls++; return Page(next); })); using var source = Transport(client, new());
        await Assert.ThrowsAnyAsync<ToolkitException>(() => Read(source)); await Assert.ThrowsAnyAsync<ToolkitException>(() => Read(source)); Assert.Equal(1, calls);
    }
    [Fact] public async Task A_page_exceeding_the_registered_incident_page_size_is_refused()
    {
        using var client = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { value = Enumerable.Range(1, 101).Select(id => new { id = id.ToString() }).ToArray() }), Encoding.UTF8, "application/json") }));
        using var source = Transport(client, new());
        await Assert.ThrowsAnyAsync<ToolkitException>(() => Read(source));
    }
    [Fact] public async Task Missing_returned_scope_or_changed_context_refuses_before_any_request()
    {
        var calls = 0; using var client = new HttpClient(new Handler(_ => { calls++; return Page(); }));
        Assert.ThrowsAny<ToolkitException>(() => Transport(client, new(), "ServiceHealth.Read.All"));
        var current = true; var tokens = new Tokens { After = () => current = false };
        using var source = Transport(client, tokens, current: () => { if (!current) throw new TenantMismatchException("Synthetic context changed"); });
        await Assert.ThrowsAnyAsync<ToolkitException>(() => Read(source)); Assert.Equal(0, calls);
    }
    [Fact] public void Existing_connection_factory_keeps_identity_and_does_not_acquire_tokens()
    {
        var calls = 0; var tokens = new Tokens { After = () => calls++ };
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("No request expected")));
        var graph = new GraphClient(http, tokens, TestData.TenantA, SessionMode.Assessment, GraphRouteAllowList.ForReports(), new(), NullLog.Instance);
        var session = TestData.Session(); session.Mode = SessionMode.Assessment; session.Scopes = ["SecurityIncident.Read.All"];
        using var source = graph.ForSecurityIncidentReports(session, Selection(), () => {});
        Assert.Equal(TestData.TenantA, source.TenantId); Assert.Equal("security-incidents", source.ReportId); Assert.Equal(0, calls);
        session.TenantId = TestData.TenantB;
        Assert.Throws<TenantMismatchException>(() => graph.ForSecurityIncidentReports(session, Selection(), () => {}));
        session.TenantId = TestData.TenantA; session.Mode = SessionMode.Deployment;
        Assert.Throws<TenantMismatchException>(() => graph.ForSecurityIncidentReports(session, Selection(), () => {})); Assert.Equal(0, calls);
    }
    private sealed class Tokens : IAccessTokenProvider
    {
        public int Renewals { get; private set; } public Action? After { get; init; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) => GetAccessTokenAsync(false, ct);
        public Task<string> GetAccessTokenAsync(bool refresh, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (refresh) Renewals++; After?.Invoke(); return Task.FromResult("synthetic-token"); }
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); } }
}
