using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ConfigurationInventoryPageTests
{
    [Fact]
    public async Task Desktop_html_inventory_preserves_offline_partial_capture_and_exports_its_actual_properties()
    {
        using var root = new TempRoot(); root.WriteStandard("test.json", TestData.StandardJson); root.WriteManifest();
        using var logger = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(root.Paths, new ToolkitSettings(), logger, diagnostics: true); workspace.Initialise();
        var page = new ShellViewModel(workspace).Page<ConfigurationViewModel>();
        Assert.False(page.ExportHtmlCommand.CanExecute(null));
        workspace.ApplyProfileToSession(TestData.Profile(), save: false);
        var capture = TestData.Snapshot(workspace.RequireStandard());
        capture.Complete = false;
        capture.Collections["conditionalAccess"].Status = CaptureStatus.Error;
        capture.Collections["conditionalAccess"].Error = "Synthetic permission refusal";
        capture.Collections["groups"].Items.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = "aaaaaaaa-0000-4000-8000-000000000003", ["displayName"] = "<Synthetic & object>", ["customObservedValue"] = "actual-value" });
        capture.Collections["groups"].Count = capture.Collections["groups"].Items.Count;
        workspace.Evidence.SaveSnapshot(capture); workspace.LoadStoredSnapshot(capture.Id); page.Refresh();
        var before = ToolkitJson.Serialize(workspace.Snapshot);
        Assert.True(page.ExportHtmlCommand.CanExecute(null));
        Assert.Contains("offline", page.SnapshotSummary);
        Assert.Contains("INCOMPLETE", page.SnapshotSummary);
        Assert.Contains(capture.Id, page.SnapshotText);
        Assert.DoesNotContain(capture.Id, page.SnapshotSummary);
        var command = Assert.IsType<AsyncCommand>(page.ExportHtmlCommand);
        command.Execute(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (command.IsRunning) await Task.Delay(10, timeout.Token);
        var file = Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory, "configuration-*.html"));
        var html = await File.ReadAllTextAsync(file);
        Assert.Contains("Tenant configuration inventory", html);
        Assert.Contains("actual-value", html);
        Assert.Contains("&lt;Synthetic &amp; object&gt;", html);
        Assert.Contains("Synthetic permission refusal", html);
        Assert.Contains("INCOMPLETE", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(file, page.LastExport);
        Assert.Equal(before, ToolkitJson.Serialize(workspace.Snapshot));
        Assert.False(workspace.SnapshotIsLive); Assert.Null(workspace.Connection); Assert.Null(workspace.Plan);
    }
}
