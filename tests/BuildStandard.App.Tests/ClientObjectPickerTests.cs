using System.Text.Json.Nodes;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Graph;
using Xunit;

namespace BuildStandard.Tests;
public sealed class ClientObjectPickerTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly ConnectViewModel _page;
    private readonly FakeGraphClient _graph;
    public ClientObjectPickerTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        _log = new(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new(_root.Paths, new ToolkitSettings(), _log, true); _workspace.Initialise();
        var profile = _workspace.SaveProfile(TestData.Profile()); _workspace.ApplyProfileToSession(profile, save: false);
        _graph = new(TestData.Standard()) { Mode = SessionMode.Assessment };
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace, new ConnectedTenant(TestData.Session(mode: SessionMode.Assessment), _graph, null));
        _shell = new(_workspace); _page = _shell.Page<ConnectViewModel>(); _page.Selected = profile;
        _page.ObjectPurpose = "Pilot group"; _page.ObjectQuery = "Pilot";
        _graph.Add("/groups", new JsonObject { ["id"] = TestData.Office, ["displayName"] = "Pilot engineers" });
    }
    private async Task Search()
    {
        var command = Assert.IsType<AsyncCommand>(_page.SearchObjectsCommand); command.Execute(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (command.IsRunning) await Task.Delay(10, timeout.Token);
    }
    [Fact]
    public async Task Lookup_requires_explicit_selection_and_only_fills_local_inputs()
    {
        var old = _workspace.Profile!.Parameters.PilotGroupId;
        await Search(); Assert.Single(_page.ObjectMatches); Assert.Null(_page.SelectedObject);
        Assert.False(_page.UseObjectCommand.CanExecute(null));
        _page.SelectedObject = _page.ObjectMatches[0]; _page.UseObjectCommand.Execute(null);
        Assert.Equal(TestData.Office, _page.EditPilotGroupId);
        Assert.Equal(old, _workspace.Profile.Parameters.PilotGroupId); Assert.Null(_workspace.Plan);
        Assert.Empty(_graph.Writes); Assert.Empty(_shell.ErrorMessage);
    }
    [Fact]
    public async Task Late_lookup_after_form_tenant_change_is_refused()
    {
        _graph.BeforeRead = (_, _) => { _page.EditTenantId = TestData.TenantB; return Task.CompletedTask; };
        await Search(); Assert.Empty(_page.ObjectMatches); Assert.Null(_page.SelectedObject);
        Assert.Contains("Could not complete", _page.ObjectStatus); Assert.False(_page.UseObjectCommand.CanExecute(null)); Assert.Empty(_graph.Writes);
    }
    [Fact]
    public async Task Disconnect_invalidates_choices_and_direct_use_cannot_fill_them()
    {
        await Search(); _page.SelectedObject = Assert.Single(_page.ObjectMatches);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace, null);
        _page.Refresh(); var before = _page.EditPilotGroupId;
        _page.UseObjectCommand.Execute(null); Assert.Equal(before, _page.EditPilotGroupId);
        Assert.Empty(_page.ObjectMatches); Assert.Empty(_graph.Writes);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }
}
