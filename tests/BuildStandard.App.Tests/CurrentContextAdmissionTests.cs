using System.Reflection;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using Xunit;
namespace BuildStandard.Tests;
public sealed class CurrentContextAdmissionTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    public CurrentContextAdmissionTests()
    {
        _root.WriteStandard("2026.09.12.json", File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"))));
        _root.WriteManifest(); _log = new(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_root.Paths, new ToolkitSettings(), _log, true); _workspace.Initialise();
    }
    private void Set(string name, object? value) => typeof(Workspace).GetProperty(name)!.SetValue(_workspace, value);
    [Fact]
    public void A_to_B_to_A_never_readmits_an_old_callback_and_keeps_original_history()
    {
        var a = TestData.Profile(); _workspace.ApplyProfileToSession(a, false);
        var snapshot = TestData.Snapshot(_workspace.RequireStandard()); _workspace.Evidence.SaveSnapshot(snapshot);
        var originalFile = _workspace.Paths.TenantDirectory(a.TenantId);
        var files = Directory.GetFiles(originalFile, "*", SearchOption.AllDirectories);
        var bytes = files.ToDictionary(p => p, File.ReadAllBytes);
        var original = _workspace.CapturePresentationContext();
        _workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB), false);
        Assert.False(_workspace.IsCurrent(original));
        _workspace.ApplyProfileToSession(a, false);
        Assert.False(_workspace.IsCurrent(original));
        Assert.True(_workspace.IsCurrent(_workspace.CapturePresentationContext()));
        foreach (var pair in bytes) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
    }
    [Fact]
    public void Same_tenant_account_replacement_invalidates_results_and_original_export_notification()
    {
        var profile = TestData.Profile(); _workspace.ApplyProfileToSession(profile, false);
        var session = TestData.Session();
        var connection = new BuildStandard.Graph.ConnectedTenant(session, new FakeGraphClient(_workspace.RequireStandard()), null);
        Set(nameof(Workspace.Connection), connection);
        var before = _workspace.CapturePresentationContext();
        session.AccountObjectId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        Assert.False(_workspace.IsCurrent(before));
        session.AccountObjectId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        Assert.False(_workspace.IsCurrent(before));
        Set(nameof(Workspace.Connection), null);
        var disconnected = _workspace.CapturePresentationContext();
        Set(nameof(Workspace.Connection), connection);
        Assert.False(_workspace.IsCurrent(disconnected)); Assert.False(_workspace.IsCurrent(before));
    }
    [Fact]
    public void Home_and_header_share_uncertain_run_without_snapshot_and_viewing_does_not_reconcile_it()
    {
        var profile = TestData.Profile(); _workspace.ApplyProfileToSession(profile, false);
        var run = new DeploymentRun { TenantId = profile.TenantId, Status = RunStatus.Interrupted,
            Results = [new() { WriteAcceptance = WriteAcceptance.Unknown }] };
        Set(nameof(Workspace.LastRun), run);
        var shell = new ShellViewModel(_workspace);
        var home = shell.Page<HomeViewModel>(); home.Refresh();
        Assert.Equal("recovery", home.Projection.NextPageKey); Assert.Equal(home.Projection.NextPageKey, shell.NextActionKey);
        _workspace.MarkRunViewed(); home.Refresh();
        Assert.Equal("recovery", home.Projection.NextPageKey); Assert.Equal("recovery", shell.NextActionKey);
        Assert.Equal(WriteAcceptance.Unknown, run.Results[0].WriteAcceptance);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }
}
