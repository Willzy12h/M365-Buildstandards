using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Evidence;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ScopedCollectionTests
{
    [Fact]
    public async Task Selected_control_reads_only_primary_and_licence_dependencies_and_cannot_be_before_evidence()
    {
        var standard = TestData.Standard();
        var profile = TestData.Profile();
        var selection = CheckSelection.ForControl(standard, profile, "CA-001");
        var graph = new FakeGraphClient(standard);
        var capture = await Collector().CollectScopedAsync(graph, TestData.Session(), profile, standard, selection, null, CancellationToken.None);
        Assert.Equal(new[] { "conditionalAccess", "licences" }, selection.CollectionKeys);
        Assert.Equal(selection.CollectionKeys, capture.Collections.Keys.Order(StringComparer.Ordinal));
        Assert.All(graph.Reads, path => Assert.Contains(selection.CollectionKeys, k => path.StartsWith(standard.Collections[k].BasePath, StringComparison.Ordinal)));
        Assert.NotEmpty(graph.Reads);
        Assert.All(capture.Collections.Values, c => Assert.Equal(CaptureStatus.Collected, c.Status));
        Assert.False(capture.Complete);
        Assert.NotNull(SnapshotRequirements.IncompleteReason(capture, standard));
        Assert.Empty(graph.Writes);
    }

    [Fact]
    public void Equivalence_dependencies_are_included_but_unknown_routes_are_refused()
    {
        var standard = TestData.Standard();
        standard.FindControl("CA-001")!.Equivalence!.Collection = "groups";
        var selection = CheckSelection.ForControl(standard, TestData.Profile(), "CA-001");
        Assert.Contains("groups", selection.CollectionKeys);
        standard.FindControl("CA-001")!.Equivalence!.Collection = "unregistered";
        Assert.Throws<ConfigurationException>(() => CheckSelection.ForControl(standard, TestData.Profile(), "CA-001"));
    }

    [Fact]
    public async Task Changed_dependency_is_refused_before_any_request()
    {
        var standard = TestData.Standard();
        var profile = TestData.Profile();
        var selection = CheckSelection.ForControl(standard, profile, "CA-001");
        var graph = new FakeGraphClient(standard);
        standard.FindControl("CA-001")!.Collection = "unregistered";
        await Assert.ThrowsAsync<ConfigurationException>(() => Collector().CollectScopedAsync(graph, TestData.Session(), profile, standard, selection, null, CancellationToken.None));
        Assert.Empty(graph.Reads);
    }

    [Fact]
    public async Task Cancelled_scoped_reads_preserve_existing_cancellation_states_only_for_selected_dependencies()
    {
        var standard = TestData.Standard();
        var profile = TestData.Profile();
        var selection = CheckSelection.ForControl(standard, profile, "CA-001");
        var graph = new FakeGraphClient(standard);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var capture = await Collector().CollectScopedAsync(graph, TestData.Session(), profile, standard, selection, null, cancellation.Token, true);
        Assert.Equal(selection.CollectionKeys, capture.Collections.Keys.Order(StringComparer.Ordinal));
        // Preserve the existing full collector contract: the interrupted first read records its cancellation,
        // while subsequent selected dependencies are explicitly not attempted.
        var first = capture.Collections.First();
        Assert.Equal(CaptureStatus.Error, first.Value.Status);
        Assert.Contains("cancelled", first.Value.Error, StringComparison.OrdinalIgnoreCase);
        Assert.All(capture.Collections.Skip(1), c => Assert.Equal(CaptureStatus.NotAttempted, c.Value.Status));
        Assert.All(capture.Collections.Values, c => Assert.False(c.Usable));
        Assert.False(capture.Complete);
        Assert.Empty(graph.Reads);
    }

    [Fact]
    public async Task Foreign_graph_tenant_is_refused_without_reading()
    {
        var standard = TestData.Standard(); var profile = TestData.Profile();
        var selection = CheckSelection.ForControl(standard, profile, "CA-001");
        var graph = new FakeGraphClient(standard) { TenantId = TestData.TenantB };
        await Assert.ThrowsAsync<TenantMismatchException>(() => Collector().CollectScopedAsync(graph, TestData.Session(), profile, standard, selection, null, CancellationToken.None));
        Assert.Empty(graph.Reads);
    }

    [Theory]
    [InlineData("", "control")]
    [InlineData("not-a-control", "control")]
    [InlineData("not-an-area", "area")]
    public void Unknown_or_empty_selectors_do_not_fall_back_to_full_collection(string value, string kind)
    {
        Assert.Throws<ConfigurationException>(() => kind == "area"
            ? CheckSelection.ForArea(TestData.Standard(), TestData.Profile(), value)
            : CheckSelection.ForControl(TestData.Standard(), TestData.Profile(), value));
    }

    private static TenantCollector Collector() => new(NullLog.Instance, new FixedClock(), "test");
}
