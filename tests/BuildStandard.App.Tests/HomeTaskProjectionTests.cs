using BuildStandard.App.ViewModels;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using Xunit;

namespace BuildStandard.Tests;

public sealed class HomeTaskProjectionTests
{
    private static HomeTaskContext Current()
    {
        var standard = TestData.Standard(); var session = TestData.Session(mode: SessionMode.Assessment);
        session.Scopes = new() { "Policy.Read.All" };
        return new() { Profile = TestData.Profile(), Standard = standard, Session = session,
            Snapshot = TestData.Snapshot(standard), SnapshotIsLive = true };
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void Mixed_capture_sources_share_header_labels_and_preserve_each_original_time(bool graphLive, bool exchangeTool, bool incomplete)
    {
        var context = Current();
        var exchange = TestData.Snapshot(context.Standard!);
        exchange.Id = "synthetic-separate-exchange"; exchange.CapturedAt = "2026-09-02T09:00:00Z";
        exchange.Complete = !incomplete;
        context.Snapshot!.CapturedAt = "2026-09-01T08:00:00Z";
        var expected = EvidenceSourcePresentation.Create(context.Snapshot, graphLive, exchange, exchangeTool,
            context.Profile!.TenantId, context.Session!.TenantId);
        var result = HomeTaskProjection.Create(context with { SnapshotIsLive = graphLive, ExchangeSnapshot = exchange, ExchangeCapturedByTool = exchangeTool });
        Assert.StartsWith(expected.Summary, result.EvidenceText);
        Assert.Contains(expected.Details, result.EvidenceText);
        Assert.Contains(exchange.Id, result.EvidenceText); Assert.Contains(exchange.CapturedAt, result.EvidenceText);
        Assert.Contains(context.Snapshot.CapturedAt, result.EvidenceText);
        if (incomplete) Assert.Contains("Exchange completeness: INCOMPLETE", result.EvidenceText);
    }

    [Fact]
    public void Different_displayed_setup_tenant_refuses_evidence_without_copying_the_previous_capture()
    {
        var context = Current();
        var result = HomeTaskProjection.Create(context with { ApplicationSetupActive = true, DisplayedTenantId = TestData.TenantB });
        Assert.Contains("another tenant", result.EvidenceText);
        Assert.DoesNotContain(context.Snapshot!.Id, result.EvidenceText);
        Assert.DoesNotContain(context.Snapshot.CapturedAt, result.EvidenceText);
        Assert.DoesNotContain("collections reported complete", result.EvidenceText);
        Assert.Equal("setup", result.NextPageKey);
    }

    [Fact]
    public void Foreign_supplemental_evidence_is_disclosed_without_showing_its_id_or_capture_time()
    {
        var context = Current(); var exchange = TestData.Snapshot(context.Standard!);
        exchange.TenantId = TestData.TenantB; exchange.Id = "foreign-exchange-id"; exchange.CapturedAt = "2026-09-02T09:00:00Z";
        var result = HomeTaskProjection.Create(context with { ExchangeSnapshot = exchange });
        Assert.Contains("Exchange: different tenant", result.EvidenceText);
        Assert.Contains("another tenant", result.EvidenceText);
        Assert.DoesNotContain(exchange.Id, result.EvidenceText); Assert.DoesNotContain(exchange.CapturedAt, result.EvidenceText);
    }

    [Fact]
    public void Cold_start_and_missing_catalogue_have_explicit_first_steps()
    {
        var empty = HomeTaskProjection.Create(new());
        Assert.Equal("settings", empty.NextPageKey); Assert.Contains("No valid Build Standard", empty.StandardText);
        var loaded = HomeTaskProjection.Create(new() { Standard = TestData.Standard() });
        Assert.Equal("connect", loaded.NextPageKey); Assert.Contains("No client selected", loaded.ClientText);
        Assert.Contains("No full capture", loaded.EvidenceText); Assert.Contains("Offline", loaded.ConnectionText);
    }

    [Fact]
    public void Saved_client_without_a_connection_keeps_local_review_available()
    {
        var profile = TestData.Profile(); profile.Company = "Synthetic handed-over client"; profile.Domain = "example.invalid";
        var result = HomeTaskProjection.Create(new() { Standard = TestData.Standard(), Profile = profile });
        Assert.Equal("configuration", result.NextPageKey); Assert.Contains(profile.Company, result.Headline);
        Assert.Contains("saved reference", result.ClientText); Assert.Contains("Open a saved capture", result.Guidance);
        Assert.DoesNotContain(profile.TenantId, result.ClientText);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Historical_evidence_and_known_gaps_remain_visible_even_with_an_active_connection(bool complete)
    {
        var context = Current(); context.Snapshot!.Complete = complete;
        var result = HomeTaskProjection.Create(context with { SnapshotIsLive = false });
        Assert.Equal("assessment", result.NextPageKey); Assert.Contains("Historical evidence", result.EvidenceText);
        Assert.Contains("cannot authorise deployment", result.Guidance);
        if (!complete)
        {
            Assert.Contains("incomplete or partial", result.EvidenceText); Assert.Contains("incomplete or partial", result.Guidance);
            Assert.Contains("Unread settings remain unknown", result.Guidance);
        }
    }

    [Theory]
    [InlineData("flag")] [InlineData("failed")] [InlineData("missing")] [InlineData("detail")] [InlineData("count")]
    public void Partial_unknown_and_missing_collections_never_become_a_complete_collection_claim(string gap)
    {
        var context = Current(); var snapshot = context.Snapshot!; var key = snapshot.Collections.Keys.First();
        switch (gap)
        {
            case "flag": snapshot.Complete = false; break;
            case "failed": snapshot.Collections[key].Status = CaptureStatus.Error; break;
            case "missing": snapshot.Collections.Remove(key); break;
            case "detail": snapshot.Collections[key].DetailIncomplete = true; break;
            case "count": snapshot.Collections[key].Count++; break;
        }
        var result = HomeTaskProjection.Create(context);
        Assert.Equal("configuration", result.NextPageKey); Assert.Contains("incomplete or partial", result.EvidenceText);
        Assert.Contains("unknown", result.Guidance); Assert.DoesNotContain("collections reported complete", result.EvidenceText);
    }

    [Fact]
    public void Disconnected_live_flag_does_not_claim_a_current_connection_or_authority()
    {
        var result = HomeTaskProjection.Create(Current() with { Session = null });
        Assert.Contains("Offline", result.ConnectionText); Assert.Contains("no verified current connection", result.EvidenceText);
        Assert.DoesNotContain("Current-session capture", result.EvidenceText);
        Assert.Contains("disconnected evidence cannot authorise deployment", result.Guidance);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Capture_or_connection_for_a_different_client_is_explicitly_rejected(bool foreignCapture)
    {
        var context = Current();
        if (foreignCapture) context.Snapshot!.TenantId = TestData.TenantB;
        else context.Session!.TenantId = TestData.TenantB;
        var result = HomeTaskProjection.Create(context);
        Assert.Equal(foreignCapture ? "configuration" : "connect", result.NextPageKey);
        Assert.Contains(foreignCapture ? "another tenant" : "another client", foreignCapture ? result.EvidenceText : result.ConnectionText);
        Assert.DoesNotContain("Original capture", result.EvidenceText);
    }

    [Fact]
    public void Current_complete_evidence_suggests_review_without_approval_and_projection_is_pure()
    {
        var context = Current(); var before = ToolkitJson.Serialize(context);
        var first = HomeTaskProjection.Create(context); var second = HomeTaskProjection.Create(context);
        Assert.Equal(first, second); Assert.Equal(before, ToolkitJson.Serialize(context));
        Assert.Equal("assessment", first.NextPageKey); Assert.Contains("review findings", first.EvidenceText);
        Assert.Contains("separate review and approval", first.Guidance); Assert.Contains("Assessment mode — toolkit changes blocked", first.ConnectionText);
        Assert.Contains("Token: read-only capabilities verified", first.ConnectionText);
    }

    [Fact]
    public void Assessment_mode_with_write_scopes_does_not_hide_write_capability()
    {
        var context = Current(); context.Session!.Scopes.Add("Policy.ReadWrite.ConditionalAccess");
        var result = HomeTaskProjection.Create(context);
        Assert.Contains("broader permissions present", result.ConnectionText); Assert.Equal("assessment", result.NextPageKey);
        Assert.Contains("session-specific opt-in", HomeTaskProjection.BoundaryText);
        Assert.Contains("Scoped checks cover their selected requirements only", HomeTaskProjection.BoundaryText);
        Assert.Contains("Job completion needs reviewed evidence", HomeTaskProjection.BoundaryText);
    }

    [Fact]
    public void Unverified_identity_and_different_catalogue_get_review_routes()
    {
        var context = Current(); context.Session!.OperatorVerified = false;
        var identity = HomeTaskProjection.Create(context); Assert.Equal("connect", identity.NextPageKey);
        Assert.Contains("needs verification", identity.ConnectionText);
        context.Session.OperatorVerified = true; context.Snapshot!.StandardRelease = "synthetic-old";
        var changed = HomeTaskProjection.Create(context); Assert.Equal("history", changed.NextPageKey);
        Assert.Contains("synthetic-old", changed.EvidenceText); Assert.Contains("recapture", changed.Guidance);
    }

    [Fact]
    public void Busy_setup_and_interrupted_outcomes_never_offer_execution()
    {
        var context = Current();
        var busy = HomeTaskProjection.Create(context with { Busy = true, BusyMessage = "Synthetic read in progress" });
        Assert.Equal("overview", busy.NextPageKey); Assert.Contains("Synthetic read in progress", busy.Guidance);
        var setup = HomeTaskProjection.Create(context with { ApplicationSetupActive = true });
        Assert.Equal("setup", setup.NextPageKey); Assert.Contains("write-capable", setup.ConnectionText);
        var stopped = HomeTaskProjection.Create(context with { InterruptedNotice = "Synthetic cancelled operation; write acceptance unknown." });
        Assert.Equal("recovery", stopped.NextPageKey); Assert.Contains("before repeating", stopped.Guidance);
    }

    [Fact]
    public void Exchange_only_evidence_is_supplemental_and_never_full_evidence()
    {
        var context = Current();
        var exchange = HomeTaskProjection.Create(context with { Snapshot = null, ExchangeSnapshot = context.Snapshot, ExchangeCapturedByTool = false });
        Assert.Equal("configuration", exchange.NextPageKey); Assert.Contains("Historical Exchange", exchange.EvidenceText);
        Assert.Contains("full Graph evidence is not open", exchange.EvidenceText);
    }

    [Fact]
    public void New_context_projection_cannot_retain_the_previous_client_or_capture_claim()
    {
        var original = Current(); var a = HomeTaskProjection.Create(original);
        var profile = TestData.Profile(TestData.TenantB); profile.Company = "Synthetic next client";
        var b = HomeTaskProjection.Create(original with { Profile = profile, Session = null, Snapshot = null, SnapshotIsLive = false });
        Assert.Contains(profile.Company, b.Headline); Assert.DoesNotContain(original.Profile!.Company, b.Headline);
        Assert.Contains("No full capture", b.EvidenceText); Assert.NotEqual(a, b);
    }
}
