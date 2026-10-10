using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Checks;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ScopedCheckPageTests
{
    [Fact]
    public async Task Stored_partial_check_preserves_full_workspace_evidence_and_uses_separate_store()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        using var logger = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(root.Paths, new ToolkitSettings(), logger, diagnostics: true); workspace.Initialise();
        var profile = TestData.Profile(); workspace.ApplyProfileToSession(profile, save: false);
        var source = TestData.Snapshot(workspace.RequireStandard()); workspace.Evidence.SaveSnapshot(source);
        workspace.LoadStoredSnapshot(source.Id);
        var before = ToolkitJson.Serialize(workspace.Snapshot);
        var assessment = workspace.Assessment;
        var snapshots = workspace.Evidence.ListSnapshots(profile.TenantId).Count;
        var result = await workspace.RunScopedCheckAsync(CheckSelection.ForControl(workspace.RequireStandard(), profile, "CA-001"), historical: true);
        Assert.Equal(before, ToolkitJson.Serialize(workspace.Snapshot)); Assert.Same(assessment, workspace.Assessment);
        Assert.Equal(snapshots, workspace.Evidence.ListSnapshots(profile.TenantId).Count);
        Assert.Contains(Path.DirectorySeparatorChar + "scoped-checks" + Path.DirectorySeparatorChar, result.File);
        Assert.False(result.Evidence.Capture.Complete); Assert.False(workspace.SnapshotIsLive); Assert.Null(workspace.Connection);
        Assert.Equal(result.Evidence.Id, new ScopedCheckStore(root.Paths).Read(result.File, workspace.RequireStandard(), profile).Id);
    }

    [Fact]
    public void Disabled_checks_explain_connection_and_missing_stored_evidence()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        using var logger = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(root.Paths, new ToolkitSettings(), logger, diagnostics: true); workspace.Initialise();
        var page = new ShellViewModel(workspace).Page<AssessmentViewModel>();
        Assert.False(page.CheckAreaCommand.CanExecute(null)); Assert.Contains("Select a saved client", page.CheckGuidance);
        workspace.ApplyProfileToSession(TestData.Profile(), save: false);
        Assert.False(page.CheckControlCommand.CanExecute(null)); Assert.Contains("Connect", page.CheckGuidance);
        page.CheckStored = true;
        Assert.False(page.CheckControlCommand.CanExecute(null)); Assert.Contains("Open a stored configuration", page.CheckGuidance);
        page.CheckArea = "Exchange";
        Assert.False(page.CheckAreaCommand.CanExecute(null)); Assert.Contains("no requirements in that area", page.CheckGuidance);
        Assert.NotEmpty(page.CheckControls);
    }
}
