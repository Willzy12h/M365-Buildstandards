using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OperationalReportsPageTests
{
    private sealed class Source(Func<OperationalReadKind, CancellationToken, Task<OperationalReportPage>> read) : IOperationalReportSource
    {
        public string TenantId => TestData.TenantA; public string ReportId => "service-health";
        public Task<OperationalReportPage> ReadNextAsync(OperationalReadKind kind, string? relationshipId, CancellationToken cancellation)
        { Assert.Null(relationshipId); return read(kind, cancellation); }
    }
    private static OperationalReportPage Page(OperationalReadKind kind) => new(kind == OperationalReadKind.HealthOverviews
        ? [new JsonObject { ["id"] = "Exchange", ["service"] = "Exchange Online", ["status"] = "serviceOperational" }] : [], false);
    private static OperationalReportsViewModel Model(ConnectedReportsFixture fixture, Func<OperationalReadKind, CancellationToken, Task<OperationalReportPage>> read)
    { fixture.Controller.OperationalSourceFactory = (_, _) => new Source(read); return new(new ShellViewModel(fixture.Workspace), fixture.Controller); }
    [Fact]
    public async Task Missing_access_or_acknowledgement_makes_no_source_token_or_http_request()
    {
        using var f = new ConnectedReportsFixture(); var reads = 0;
        var page = Model(f, (kind, _) => { reads++; return Task.FromResult(Page(kind)); });
        Assert.False(page.Acknowledged); page.Acknowledged = true; Assert.Contains("ServiceHealth.Read.All", page.ReadGuidance);
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync); f.Workspace.Session!.Scopes.Add("ServiceHealth.Read.All"); page.Acknowledged = false;
        Assert.False(page.ReadCommand.CanExecute(null)); await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
        Assert.Equal(0, reads); Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Capture_to_original_remains_owned_for_stop_close_and_late_client_change(bool changeClient)
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add("ServiceHealth.Read.All");
        var page = Model(f, (kind, _) => Task.FromResult(Page(kind))); page.Acknowledged = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.SerialiseOriginal = async report => { entered.SetResult(); await release.Task; return Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(report)); };
        var read = page.ReadAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = f.Workspace.StopAndWaitForEvidenceAsync();
        try
        {
            Assert.True(f.Workspace.Busy); Assert.False(page.CanEdit); Assert.False(close.IsCompleted);
            if (changeClient) { f.ReplaceConnection(); page.Refresh(); }
        }
        finally { release.TrySetResult(); if (changeClient) await Assert.ThrowsAnyAsync<ToolkitException>(() => read); else await read; await close; }
        Assert.True(page.HasPendingOriginal); Assert.False(f.Workspace.Busy);
        if (changeClient) Assert.Contains("No current", page.Outcome);
    }
    [Fact]
    public async Task Save_reopen_and_foreign_context_recovery_never_rerun_or_change_configuration_evidence()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add("ServiceHealth.Read.All"); var reads = 0;
        var page = Model(f, (kind, _) => { reads++; return Task.FromResult(Page(kind)); }); page.Acknowledged = true; var before = f.Workspace.Snapshot;
        await page.ReadAsync(); Assert.True(page.HasPendingOriginal); await page.SavePendingOriginalAsync(); Assert.False(page.HasPendingOriginal);
        var file = page.LastExport; var original = File.ReadAllBytes(file); await page.OpenSuppliedAsync(file); page.Search = "not present";
        await page.ExportAsync(ExportFormat.Json); Assert.Equal(original, File.ReadAllBytes(page.LastExport)); Assert.Equal(2, reads); Assert.Same(before, f.Workspace.Snapshot);
        page.Acknowledged = true; await page.ReadAsync(); f.ReplaceConnection(); page.Refresh(); Assert.Contains("No current", page.Outcome); Assert.True(page.HasPendingOriginal);
        await page.ExportPendingOriginalAsync(); Assert.Equal(TestData.TenantA, OperationalContextFile.ReadFile(page.LastExport, TestData.TenantA).Report.TenantId); Assert.Equal(4, reads);
    }
    [Fact]
    public async Task Failed_save_keeps_original_until_explicit_discard_without_retry_or_snapshot_insertion()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add("ServiceHealth.Read.All"); var reads = 0;
        var page = Model(f, (kind, _) => { reads++; return Task.FromResult(Page(kind)); }); page.Acknowledged = true; await page.ReadAsync();
        var blocked = Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "operational-context-evidence"); Directory.CreateDirectory(Path.GetDirectoryName(blocked)!); File.WriteAllText(blocked, "Synthetic obstruction");
        await Assert.ThrowsAnyAsync<IOException>(page.SavePendingOriginalAsync); Assert.True(page.HasPendingOriginal);
        page.ConfirmDiscard = _ => false; page.DiscardOriginalCommand.Execute(null); Assert.True(page.HasPendingOriginal);
        page.ConfirmDiscard = _ => true; page.DiscardOriginalCommand.Execute(null); Assert.False(page.HasPendingOriginal); Assert.Equal(2, reads);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task File_admission_blocks_overlapping_reads_and_refuses_a_late_changed_client(bool changeClient)
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add("ServiceHealth.Read.All"); var reads = 0;
        var source = new Source((kind, _) => Task.FromResult(Page(kind)));
        var report = await new ServiceHealthReportService(SystemClock.Instance, "synthetic").CaptureAsync(source, f.Workspace.Session!, () => { }, default);
        var admitted = OperationalContextFile.Read(Encoding.UTF8.GetBytes(OperationalContextSchema.Serialise(report)), TestData.TenantA);
        var page = Model(f, (kind, _) => { reads++; return Task.FromResult(Page(kind)); });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.ReadOriginal = async (_, _) => { entered.SetResult(); await release.Task; return admitted; };
        var opening = page.OpenSuppliedAsync("synthetic-owned-fixture.json"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(page.CanEdit); page.Acknowledged = true; Assert.Contains("file is being checked", page.ReadGuidance);
            await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
            if (changeClient) { f.ReplaceConnection(); page.Refresh(); }
        }
        finally { release.TrySetResult(); if (changeClient) await Assert.ThrowsAnyAsync<ToolkitException>(() => opening); else await opening; }
        Assert.Equal(0, reads); Assert.False(page.HasPendingOriginal);
        if (changeClient) Assert.Contains("No current", page.Outcome);
    }
    [Fact]
    public async Task Parent_refresh_immediately_clears_nested_current_client_presentation()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add("ServiceHealth.Read.All");
        var report = await new ServiceHealthReportService(SystemClock.Instance, "synthetic").CaptureAsync(new Source((kind, _) => Task.FromResult(Page(kind))), f.Workspace.Session!, () => { }, default);
        var file = Path.Combine(f.Root.Root, "operational.json"); File.WriteAllText(file, OperationalContextSchema.Serialise(report));
        var parent = new ShellViewModel(f.Workspace).Page<GraphReportsViewModel>(); await parent.Operational.OpenSuppliedAsync(file);
        var changed = new List<string?>(); parent.Operational.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        f.ReplaceConnection(); parent.Refresh();
        Assert.Contains("No current", parent.Operational.Outcome); Assert.Null(parent.Operational.Rows);
        Assert.Contains(nameof(OperationalReportsViewModel.Outcome), changed);
    }
}
