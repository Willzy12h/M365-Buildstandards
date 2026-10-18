using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using Xunit;
namespace BuildStandard.Tests;
public sealed class NativeRouteAndPinTests
{
    [Fact]
    public void Every_existing_route_is_unique_and_reachable_in_the_five_groups()
    {
        string[] expected = ["home", "clients", "overview", "recovery", "connect", "setup", "automation", "configuration", "assessment", "deviations", "plan", "deploy", "history", "jobs", "checks", "scripts", "run-reports", "reports", "domain-readiness", "mailbox-capacity", "naming", "standard", "settings"];
        Assert.Equal(expected.Order(), RouteCatalog.Items.Select(r => r.Key).Order());
        Assert.Equal(23, RouteCatalog.Items.Select(r => r.Key).Distinct().Count());
        Assert.Equal(new[] { "Workspace", "Build standard", "Reports", "Scripts", "Settings" }, RouteCatalog.Groups);
    }
    [Theory]
    [InlineData("connect", "deployment access", "Client and connection: deployment access")]
    [InlineData("configuration", "live capture", "1 · Configuration: live capture")]
    [InlineData("plan", "reviewed plan", "4 · Plan changes: reviewed plan")]
    [InlineData("deploy", "preserve evidence", "5 · Deploy and outcomes: preserve evidence")]
    public void Readiness_labels_use_the_actual_navigation_sequence(string key, string purpose, string expected)
    { Assert.Equal(expected, RouteCatalog.PrerequisiteLabel(key, purpose)); }

    [Fact]
    public void Pin_is_immutable_original_provenance_and_context_change_collapses_sensitive_details()
    {
        var snapshot = TestData.Snapshot(TestData.Standard()); snapshot.TenantName = "Original synthetic client";
        var original = PresentationContextKey.Capture(TestData.Profile(), null, null, null, snapshot, false, null);
        var record = PinnedReferenceRecord.FromSnapshot(snapshot, original);
        var pin = new PinnedReferenceViewModel(); pin.Pin(record); pin.DetailsExpanded = true;
        snapshot.TenantName = "Replaced source name";
        var current = PresentationContextKey.Capture(TestData.Profile(TestData.TenantB), null, null, null, null, false, null);
        pin.UpdateContext(current, snapshot.StandardRelease);
        Assert.True(pin.HasMismatch); Assert.False(pin.DetailsExpanded);
        Assert.Contains("Original synthetic client", pin.OriginalDetails); Assert.DoesNotContain("Replaced source name", pin.OriginalDetails);
        Assert.Contains("cannot approve or run", pin.MismatchText);
        pin.DetailsExpanded = true; pin.UpdateContext(current, snapshot.StandardRelease);
        Assert.True(pin.DetailsExpanded); // unchanged progress must not steal focus/collapse explicit review
        pin.Close(); Assert.False(pin.HasReference); Assert.Equal("", pin.OriginalDetails);
    }

    [Fact]
    public void Pinned_context_carries_opaque_identity_not_a_live_connection_or_execution_object()
    {
        var connection = new object(); var session = TestData.Session();
        var key = PresentationContextKey.Capture(TestData.Profile(), session, connection, null, null, false, null);
        Assert.IsType<Guid>(key.ConnectionInstance); Assert.IsType<Guid>(key.SessionInstance);
        Assert.DoesNotContain(typeof(PresentationContextKey).GetProperties(), p => p.PropertyType == typeof(object));
    }
}
