using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ServiceHealthReportServiceTests
{
    private static TenantSession Session()
    {
        var session = UsageReportTransportTests.Session();
        session.Scopes.Add("ServiceHealth.Read.All");
        return session;
    }
    private static ServiceHealthReportService Service() => new(new FixedClock { UtcNow = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero) }, "synthetic");
    private static JsonObject Overview(string status = "serviceOperational") => new() { ["id"] = "Exchange", ["service"] = "Exchange Online", ["status"] = status };
    [Fact]
    public async Task Returned_empty_is_a_successful_observation_with_no_claim_of_tenant_availability()
    {
        var source = new Source((_, _) => Task.FromResult(new OperationalReportPage([], false)));
        var report = await Service().CaptureAsync(source, Session(), () => { }, default);
        var reopened = OperationalContextSchema.Read(OperationalContextSchema.Serialise(report), TestData.TenantA);
        Assert.Equal("Collected", reopened.Status);
        Assert.Equal(2, source.Calls);
        Assert.All(reopened.Sections, section => Assert.Empty(section.Rows));
        Assert.Equal(TestData.Operator, reopened.AccountObjectId);
        Assert.Equal("live", reopened.SourceMode);
        Assert.Contains(reopened.Sections[0].Limitations, text => text.Contains("availability", StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData("second-failure", "Failed")]
    [InlineData("second-cancel", "Cancelled")]
    [InlineData("second-auth", "Failed")]
    public async Task Later_failure_or_stop_retains_prior_observations(string fault, string expected)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source((kind, _) =>
        {
            if (kind == OperationalReadKind.HealthOverviews) return Task.FromResult(new OperationalReportPage([Overview()], false));
            if (fault == "second-cancel") { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
            if (fault == "second-auth") throw new AuthenticationRequiredException("Synthetic reconnect required.");
            throw new ToolkitException("Synthetic denied read; do not retain raw failure detail.");
        });
        var report = await Service().CaptureAsync(source, Session(), () => { }, cancellation.Token);
        // Existing shared report semantics give explicit cancellation precedence while retaining prior rows.
        Assert.Equal(fault == "second-cancel" ? "Cancelled" : "Partial", report.Status);
        Assert.Single(report.Sections[0].Rows);
        Assert.Empty(report.Sections[1].Rows);
        Assert.Equal(expected, report.Sections[1].Status);
        Assert.Equal(2, source.Calls);
        _ = OperationalContextSchema.Read(OperationalContextSchema.Serialise(report), TestData.TenantA);
    }
    [Theory]
    [InlineData("missing-status")]
    [InlineData("future-status")]
    [InlineData("missing-id")]
    [InlineData("duplicate-id")]
    public async Task Incomplete_or_future_observations_remain_partial_and_duplicate_identity_is_not_deduplicated(string fault)
    {
        var row = Overview();
        if (fault == "missing-status") row.Remove("status");
        if (fault == "future-status") row["status"] = "futureMicrosoftStatus";
        if (fault == "missing-id") row.Remove("id");
        var items = new List<JsonObject> { row };
        if (fault == "duplicate-id") items.Add((JsonObject)row.DeepClone());
        var source = new Source((kind, _) => Task.FromResult(new OperationalReportPage(kind == OperationalReadKind.HealthOverviews ? items : [], false)));
        var report = await Service().CaptureAsync(source, Session(), () => { }, default);
        Assert.Equal("Partial", report.Status);
        Assert.Equal(items.Count, report.Sections[0].Rows.Count);
        Assert.All(report.Sections[0].Rows, item => Assert.Equal("Partial", item["readStatus"]!.GetValue<string>()));
        if (fault == "future-status") Assert.Equal("futureMicrosoftStatus", report.Sections[0].Rows[0]["status"]!.GetValue<string>());
        _ = OperationalContextSchema.Read(OperationalContextSchema.Serialise(report), TestData.TenantA);
    }
    [Theory]
    [InlineData("date-only")]
    [InlineData("future-classification")]
    [InlineData("blank-title")]
    public async Task Malformed_issue_clock_and_unknown_classification_or_title_cannot_be_complete(string fault)
    {
        var issue = new JsonObject { ["id"] = "MC-synthetic", ["title"] = "Synthetic advisory", ["service"] = "Exchange Online",
            ["status"] = "investigating", ["classification"] = "advisory", ["startDateTime"] = "2026-10-20T10:00:00Z" };
        if (fault == "date-only") issue["startDateTime"] = "2026-10-20";
        if (fault == "future-classification") issue["classification"] = "futureClassification";
        if (fault == "blank-title") issue["title"] = "   ";
        var source = new Source((kind, _) => Task.FromResult(new OperationalReportPage(kind == OperationalReadKind.HealthIssues ? [issue] : [], false)));
        var report = await Service().CaptureAsync(source, Session(), () => { }, default);
        Assert.Equal("Partial", report.Status);
        Assert.Equal("Partial", report.Sections[1].Rows.Single()["readStatus"]!.GetValue<string>());
        if (fault == "date-only") Assert.Null(report.Sections[1].Rows.Single()["startedAt"]);
        if (fault == "future-classification") Assert.Equal("futureClassification", report.Sections[1].Rows.Single()["classification"]!.GetValue<string>());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Duplicate_identity_annotations_are_budgeted_before_admission_and_keep_earlier_observations(bool paginated)
    {
        var rows = Enumerable.Range(0, 5000).Select(_ => { var row = Overview(); row["service"] = new string('x', 3200); return row; }).ToArray();
        var pages = 0;
        var source = new Source((kind, _) =>
        {
            Assert.Equal(OperationalReadKind.HealthOverviews, kind);
            pages++;
            return Task.FromResult(new OperationalReportPage(paginated ? rows.Skip((pages - 1) * 2500).Take(2500).ToArray() : rows, paginated && pages == 1));
        });
        var report = await Service().CaptureAsync(source, Session(), () => { }, default);
        var json = OperationalContextSchema.Serialise(report);
        Assert.Equal("Partial", report.Status);
        Assert.InRange(report.Sections[0].Rows.Count, 1, 4999);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= OperationalReportRegistry.MaximumBytes);
        Assert.All(report.Sections[0].Rows, row => Assert.Equal("Partial", row["readStatus"]!.GetValue<string>()));
        Assert.Equal("NotAttempted", report.Sections[1].Status);
    }
    [Fact]
    public async Task Fixed_request_budget_stops_pagination_and_does_not_turn_unattempted_source_into_empty_success()
    {
        var source = new Source((_, _) => Task.FromResult(new OperationalReportPage([], true)));
        var report = await Service().CaptureAsync(source, Session(), () => { }, default);
        Assert.Equal(OperationalReportRegistry.MaximumCalls, source.Calls);
        Assert.Equal("Partial", report.Sections[0].Status);
        Assert.Equal("NotAttempted", report.Sections[1].Status);
        Assert.Equal("Partial", report.Status);
    }
    [Theory]
    [InlineData("tenant")]
    [InlineData("actor")]
    [InlineData("scope")]
    [InlineData("module")]
    [InlineData("late-context")]
    public async Task Wrong_binding_access_or_changed_context_is_not_admitted(string fault)
    {
        var session = Session(); var current = true;
        if (fault == "tenant") session.TenantId = TestData.TenantB;
        if (fault == "actor") session.OperatorObjectId = TestData.TenantB;
        if (fault == "scope") session.Scopes.Remove("ServiceHealth.Read.All");
        var source = new Source((_, _) => { if (fault == "late-context") current = false; return Task.FromResult(new OperationalReportPage([], false)); }, fault == "module" ? "partner-relationships" : "service-health");
        await Assert.ThrowsAnyAsync<ToolkitException>(() => Service().CaptureAsync(source, session, () => { if (!current) throw new TenantMismatchException("Synthetic context changed."); }, default));
        Assert.Equal(fault == "late-context" ? 1 : 0, source.Calls);
    }
    private sealed class Source(Func<OperationalReadKind, CancellationToken, Task<OperationalReportPage>> read, string reportId = "service-health") : IOperationalReportSource
    {
        public int Calls { get; private set; }
        public string TenantId => TestData.TenantA;
        public string ReportId => reportId;
        public Task<OperationalReportPage> ReadNextAsync(OperationalReadKind kind, string? relationshipId, CancellationToken cancellation)
        { Assert.Null(relationshipId); Calls++; return read(kind, cancellation); }
    }
}
