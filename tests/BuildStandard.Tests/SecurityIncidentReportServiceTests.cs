using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentReportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static TenantSession Session() { var session = UsageReportTransportTests.Session(); session.Scopes.Add("SecurityIncident.Read.All"); return session; }
    private static Task<OperationalContextReport> Capture(Source source, TenantSession? session = null, Action? current = null, CancellationToken token = default)
    {
        return new SecurityIncidentReportService(new FixedClock { UtcNow = Now }, "synthetic")
            .CaptureAsync(source, session ?? Session(), current ?? (() => { }), token);
    }
    private static JsonObject Incident() => new()
    {
        ["id"] = "123", ["displayName"] = "Synthetic incident", ["createdDateTime"] = "2026-10-20T09:00:00Z",
        ["lastUpdateDateTime"] = "2026-10-20T10:00:00Z", ["severity"] = "medium", ["status"] = "active",
        ["classification"] = "unknown", ["determination"] = "unknown", ["priorityScore"] = null,
        ["comments"] = new JsonArray("private synthetic content"), ["description"] = "private synthetic description"
    };
    [Fact] public async Task Completed_read_retains_only_metadata_and_null_priority_is_not_zero()
    {
        var source = new Source(_ => Task.FromResult(new OperationalReportPage([Incident()], false)));
        var report = await Capture(source); var json = OperationalContextSchema.Serialise(report);
        Assert.Equal("Collected", report.Status); Assert.Null(report.Sections[0].Rows[0]["priorityScore"]);
        Assert.DoesNotContain("private synthetic", json); Assert.Contains(OperationalContextSchema.IncidentNotice, report.Sections[0].Limitations);
        _ = OperationalContextSchema.Read(json, TestData.TenantA); Assert.Equal(1, source.Calls);
    }
    [Theory][InlineData("future")][InlineData("missing-id")][InlineData("bad-priority")][InlineData("duplicate")][InlineData("outside-window")]
    public async Task Incomplete_metadata_never_becomes_complete_or_silently_deduplicated(string fault)
    {
        var row = Incident();
        if (fault == "future") row["severity"] = "FutureSeverity";
        if (fault == "missing-id") row.Remove("id");
        if (fault == "bad-priority") row["priorityScore"] = 101;
        if (fault == "outside-window") row["createdDateTime"] = "2026-10-10T09:00:00Z";
        var source = new Source(_ => Task.FromResult(new OperationalReportPage(fault == "duplicate" ? [row, (JsonObject)row.DeepClone()] : [row], false)));
        var report = await Capture(source); Assert.Equal("Partial", report.Status);
        Assert.All(report.Sections[0].Rows, item => Assert.Equal("Partial", item["readStatus"]!.GetValue<string>()));
        Assert.Equal(fault == "duplicate" ? 2 : 1, report.Sections[0].Rows.Count);
        if (fault == "future") Assert.Equal("FutureSeverity", report.Sections[0].Rows[0]["severity"]!.GetValue<string>());
        _ = OperationalContextSchema.Read(OperationalContextSchema.Serialise(report), TestData.TenantA);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Later_failure_or_cancellation_retains_prior_rows_and_sanitises_diagnostics(bool cancel)
    {
        using var stop = new CancellationTokenSource(); var calls = 0;
        var source = new Source(_ =>
        {
            if (++calls == 1) return Task.FromResult(new OperationalReportPage([Incident()], true));
            if (cancel) { stop.Cancel(); throw new OperationCanceledException(stop.Token); }
            throw new ToolkitException("private synthetic failure");
        });
        var report = await Capture(source, token: stop.Token);
        Assert.Equal("Partial", report.Status); Assert.Single(report.Sections[0].Rows);
        Assert.DoesNotContain("private synthetic", OperationalContextSchema.Serialise(report)); Assert.Equal(2, source.Calls);
    }
    [Theory][InlineData("scope")][InlineData("account")][InlineData("late-context")]
    public async Task Changed_or_unauthorised_context_is_not_admitted(string fault)
    {
        var session = Session(); var current = true;
        if (fault == "scope") session.Scopes.Remove("SecurityIncident.Read.All");
        if (fault == "account") session.OperatorObjectId = TestData.TenantB;
        var source = new Source(_ => { current = false; return Task.FromResult(new OperationalReportPage([], false)); });
        await Assert.ThrowsAnyAsync<ToolkitException>(() => Capture(source, session, () => { if (!current) throw new TenantMismatchException("Synthetic stale context"); }));
        Assert.Equal(fault == "late-context" ? 1 : 0, source.Calls);
    }
    [Fact] public async Task Repeated_empty_pages_stop_at_the_request_cap_without_success()
    {
        var source = new Source(_ => Task.FromResult(new OperationalReportPage([], true)));
        var report = await Capture(source); Assert.Equal(100, source.Calls); Assert.NotEqual("Collected", report.Status); Assert.Empty(report.Sections[0].Rows);
    }
    [Fact] public async Task Oversized_page_is_refused_without_silent_truncation()
    {
        var source = new Source(_ => Task.FromResult(new OperationalReportPage(Enumerable.Range(0, 101).Select(_ => Incident()).ToArray(), false)));
        var report = await Capture(source); Assert.Equal("Failed", report.Status); Assert.Empty(report.Sections[0].Rows);
    }
    [Fact] public async Task Row_limit_retains_earlier_rows_but_never_claims_completed_coverage()
    {
        var id = 0;
        var source = new Source(_ => Task.FromResult(new OperationalReportPage(Enumerable.Range(0, 100).Select(_ =>
        { var row = Incident(); row["id"] = (++id).ToString(System.Globalization.CultureInfo.InvariantCulture); return row; }).ToArray(), true)));
        var report = await Capture(source); Assert.Equal("Partial", report.Status); Assert.Equal(5000, report.Sections[0].Rows.Count);
        Assert.Equal(50, source.Calls); _ = OperationalContextSchema.Read(OperationalContextSchema.Serialise(report), TestData.TenantA);
    }
    [Fact] public async Task Precancelled_capture_performs_no_reads()
    {
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var source = new Source(_ => Task.FromResult(new OperationalReportPage([], false)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Capture(source, token: stop.Token)); Assert.Equal(0, source.Calls);
    }
    private sealed class Source(Func<CancellationToken, Task<OperationalReportPage>> read) : ISecurityIncidentReportSource
    {
        public int Calls { get; private set; }
        public string TenantId => TestData.TenantA;
        public SecurityIncidentReadSelection IncidentSelection { get; } = new(Now.AddDays(-1), Now, Now);
        public Task<OperationalReportPage> ReadIncidentsNextAsync(CancellationToken token) { Calls++; return read(token); }
    }
}
