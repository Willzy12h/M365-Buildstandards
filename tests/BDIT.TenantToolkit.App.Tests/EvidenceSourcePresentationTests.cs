using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class EvidenceSourcePresentationTests
{
    private const string Tenant = "11111111-1111-4111-8111-111111111111";
    private static TenantSnapshot Capture(string id, string at) => new() { Id = id, TenantId = Tenant, CapturedAt = at, Complete = true };
    private static readonly TenantSnapshot Graph = Capture("synthetic-graph", "2026-10-01T10:00:00Z");
    private static readonly TenantSnapshot Exchange = Capture("synthetic-exchange", "2026-09-25T09:00:00Z");

    [Theory]
    [InlineData(true, false, "Live full capture", "Exchange: historical")]
    [InlineData(false, true, "Historical evidence", "Exchange: tool capture")]
    [InlineData(true, true, "Live full capture", "Exchange: tool capture")]
    public void Mixed_sources_retain_both_actual_origins_and_each_original_identity(bool graphLive, bool exchangeTool, string primary, string secondary)
    {
        var source = EvidenceSourcePresentation.Create(Graph, graphLive, Exchange, exchangeTool, Tenant, Tenant);
        Assert.StartsWith(primary, source.Summary); Assert.Contains(secondary, source.Summary);
        Assert.Equal(Graph.CapturedAt, source.OriginalCaptureTime);
        Assert.Contains(Graph.Id, source.Details); Assert.Contains(Graph.CapturedAt, source.Details);
        Assert.Contains(Exchange.Id, source.Details); Assert.Contains(Exchange.CapturedAt, source.Details);
        Assert.Contains(exchangeTool ? "collected by the tool" : "historical/offline", source.Details);
    }

    [Theory]
    [InlineData(true, "Supplemental Exchange capture")]
    [InlineData(false, "Historical Exchange evidence")]
    public void Exchange_only_uses_its_actual_source_without_claiming_a_full_Graph_capture(bool tool, string expected)
    {
        var source = EvidenceSourcePresentation.Create(null, false, Exchange, tool, Tenant, Tenant);
        Assert.StartsWith(expected, source.Summary); Assert.DoesNotContain("Live full capture", source.Summary);
        Assert.Equal(Exchange.CapturedAt, source.OriginalCaptureTime); Assert.Contains(Exchange.Id, source.Details);
        if (tool) Assert.DoesNotContain("Historical", source.Summary);
    }

    [Fact]
    public void Foreign_secondary_capture_is_disclosed_without_copying_its_identity_under_this_client()
    {
        var foreign = Capture("foreign-exchange-id", "2026-09-29T01:00:00Z"); foreign.TenantId = "22222222-2222-4222-8222-222222222222";
        var source = EvidenceSourcePresentation.Create(Graph, true, foreign, false, Tenant, Tenant);
        Assert.Contains("Exchange: different tenant", source.Summary); Assert.Contains("another tenant", source.Details);
        Assert.DoesNotContain(foreign.Id, source.Details); Assert.DoesNotContain(foreign.CapturedAt, source.Details);
    }

    [Fact]
    public void Absent_or_foreign_primary_has_no_original_time_or_copyable_capture_identity()
    {
        var absent = EvidenceSourcePresentation.Create(null, false, null, false, null, "");
        Assert.Equal("No client evidence selected", absent.Summary); Assert.Empty(absent.Details);
        var foreign = EvidenceSourcePresentation.Create(Graph, true, Exchange, false, Tenant, "different tenant");
        Assert.Contains("another tenant", foreign.Summary); Assert.Empty(foreign.OriginalCaptureTime); Assert.Empty(foreign.Details);
    }
}
