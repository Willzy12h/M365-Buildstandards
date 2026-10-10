using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// CLA-20261006-06: stored Graph and Exchange/Purview evidence must produce the same findings whenever it is
/// re-assessed. Freshness is judged against the evidence, not the day the report is run.
/// </summary>
public sealed class StoredEvidenceAssessmentTests
{
    private static AssessmentResult Assess(TenantSnapshot graph, ExchangeCapture exchange, DateTimeOffset clock, DateTimeOffset? evidenceTime) =>
        new AssessmentEngine(new FixedClock { UtcNow = clock }, "test").Assess(graph, ExchangeTestData.Standard(), TestData.Profile(),
            new ManagedObjectMappings { TenantId = TestData.TenantA }, Array.Empty<Deviation>(), "test", exchange, evidenceTime);

    [Fact]
    public void Stored_evidence_reassessed_days_later_produces_identical_exchange_findings()
    {
        var exchange = ExchangeTestData.Capture(); ExchangeTestData.AddDns(exchange);
        var captured = ExchangeTestData.Now;
        var graph = TestData.Snapshot(ExchangeTestData.Standard(), capturedAt: captured);

        var atCapture = Assess(graph, exchange, captured.AddMinutes(1), captured);
        var threeDaysLater = Assess(graph, exchange, captured.AddDays(3), captured);
        Assert.Equal(CanonicalJson.Sha256Value(atCapture.Findings.Select(f => new { f.ControlId, f.Status, f.Reason })),
            CanonicalJson.Sha256Value(threeDaysLater.Findings.Select(f => new { f.ControlId, f.Status, f.Reason })));
        Assert.DoesNotContain(threeDaysLater.Findings, f => f.Reason.Contains("over 24 hours old", StringComparison.Ordinal));
        Assert.Contains(threeDaysLater.Limitations, l => l.StartsWith("Stored evidence:", StringComparison.Ordinal));

        // Without an evidence time the live rule still applies: three-day-old Exchange evidence is not current.
        var live = Assess(graph, exchange, captured.AddDays(3), null);
        Assert.Contains(live.Findings, f => f.ControlId == "EX-004" && f.Reason.Contains("over 24 hours old", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dns_refresh_after_the_capture_counts_for_stored_evidence_as_it_does_live()
    {
        var exchange = ExchangeTestData.Capture(); ExchangeTestData.AddDns(exchange); // DNS queried at Now
        var captured = ExchangeTestData.Now.AddHours(-3);
        exchange.CapturedAt = Timestamps.Format(captured);
        var graph = TestData.Snapshot(ExchangeTestData.Standard(), capturedAt: captured);
        string Dns(AssessmentResult r) => CanonicalJson.Sha256Value(r.Findings.Where(f => f.ControlId is "EX-007" or "EX-008").Select(f => new { f.ControlId, f.Status, f.Reason }));

        var live = Assess(graph, exchange, ExchangeTestData.Now.AddMinutes(1), null);
        var storedLater = Assess(graph, exchange, ExchangeTestData.Now.AddDays(3), captured);
        Assert.Equal(Dns(live), Dns(storedLater));
    }

    [Fact]
    public void Exchange_evidence_much_older_than_the_graph_capture_is_still_stale()
    {
        var exchange = ExchangeTestData.Capture();
        var graphCaptured = ExchangeTestData.Now.AddDays(3);
        var graph = TestData.Snapshot(ExchangeTestData.Standard(), capturedAt: graphCaptured);
        var result = Assess(graph, exchange, graphCaptured.AddMinutes(1), graphCaptured);
        Assert.Contains(result.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.UnableToAssess && f.Reason.Contains("over 24 hours old", StringComparison.Ordinal));
    }
}
