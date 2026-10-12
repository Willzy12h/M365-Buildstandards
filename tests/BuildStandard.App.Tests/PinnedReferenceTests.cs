using System.Reflection;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using Xunit;
namespace BuildStandard.Tests;
public sealed class PinnedReferenceTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    public PinnedReferenceTests()
    {
        _root.WriteStandard("2026.09.12.json", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"))));
        _root.WriteManifest(); _log = new(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_root.Paths, new ToolkitSettings(), _log, true); _workspace.Initialise();
        _workspace.ApplyProfileToSession(TestData.Profile(), false); _shell = new(_workspace);
    }
    [Fact]
    public void Saved_pin_preserves_original_provenance_without_selecting_or_approving_new_client_evidence()
    {
        var saved = TestData.Snapshot(_workspace.RequireStandard()); saved.TenantName = "Synthetic original client";
        var file = _workspace.Evidence.SaveSnapshot(saved); var bytes = File.ReadAllBytes(file);
        typeof(Workspace).GetProperty(nameof(Workspace.Snapshot))!.SetValue(_workspace, saved);
        Assert.True(_shell.PinCurrentReferenceCommand.CanExecute(null)); _shell.PinCurrentReferenceCommand.Execute(null);
        Assert.True(_shell.PinnedReference.HasReference); _shell.PinnedReference.DetailsExpanded = true;
        _workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB), false);
        Assert.True(_shell.PinnedReference.HasMismatch); Assert.False(_shell.PinnedReference.DetailsExpanded);
        Assert.Contains("Synthetic original client", _shell.PinnedReference.OriginalDetails);
        Assert.Null(_workspace.AcknowledgedSnapshotId); Assert.Null(_workspace.Plan);
        Assert.Equal(bytes, File.ReadAllBytes(file));
        _shell.SetViewportWidth(1180); Assert.True(_shell.UseDrawerReference); Assert.False(_shell.UseSideReference);
        _shell.SetViewportWidth(1480); Assert.True(_shell.UseSideReference); Assert.False(_shell.UseDrawerReference);
        _shell.ClosePinnedReferenceCommand.Execute(null); Assert.False(_shell.HasPinnedReference);
        Assert.Equal(TestData.TenantB, _workspace.Profile!.TenantId);
    }
    [Fact]
    public void Every_route_remains_on_the_same_page_instance_with_current_group_and_no_sign_in()
    {
        foreach (var route in RouteCatalog.Items)
        {
            _shell.Navigate(route.Key); var page = _shell.CurrentPage;
            Assert.Equal(route.Group, _shell.ActiveNavItem!.Group);
            Assert.Contains(_shell.VisibleNavItems, n => n.Key == route.Key);
            _shell.Navigate("home"); _shell.Navigate(route.Key); Assert.Same(page, _shell.CurrentPage);
            Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
        }
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }
}
