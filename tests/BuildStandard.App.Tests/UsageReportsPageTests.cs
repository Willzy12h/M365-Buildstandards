using System.Text;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageReportsPageTests
{
    private sealed class Source(Func<string,CancellationToken,Task<byte[]>> read):IUsageReportSource
    {public string TenantId=>TestData.TenantA;public Task<byte[]> DownloadAsync(string id,string period,CancellationToken ct)=>read(id,ct);}
    private static byte[] Header(string id)=>Encoding.UTF8.GetBytes(string.Join(',',UsageReportRegistry.Find(id).Columns)+"\r\n");
    private static UsageReportsViewModel Page(ConnectedReportsFixture f,Func<string,CancellationToken,Task<byte[]>> read)
    {f.Controller.UsageSourceFactory=(_,_)=>new Source(read);return new(new ShellViewModel(f.Workspace),f.Controller);}
    [Fact] public async Task Missing_access_or_acknowledgement_never_collects_and_navigation_never_signs_in()
    {
        using var f=new ConnectedReportsFixture();var reads=0;var page=Page(f,(id,_)=>{reads++;return Task.FromResult(Header(id));});
        Assert.Equal("D30",page.Period);Assert.All(page.Sources,s=>Assert.False(s.Selected));Assert.False(page.Acknowledged);
        page.Sources[0].Selected=true;page.Acknowledged=true;Assert.Contains("Reports.Read.All",page.ReadGuidance);
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);f.Workspace.Session!.Scopes.Add("Reports.Read.All");page.Acknowledged=false;
        Assert.False(page.ReadCommand.CanExecute(null));await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);
        Assert.Equal(0,reads);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Accepted_capture_remains_owned_until_original_is_retained_for_close_and_client_changes(bool changeClient)
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Reports.Read.All");
        var page=Page(f,(id,_)=>Task.FromResult(Header(id)));page.Sources[0].Selected=true;page.Acknowledged=true;
        var entered=new TaskCompletionSource<UsageReviewReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.SerialiseOriginal=async report=>{entered.SetResult(report);await release.Task;return Encoding.UTF8.GetBytes(UsageReviewSchema.Serialise(report));};
        var read=page.ReadAsync();await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close=f.Workspace.StopAndWaitForEvidenceAsync();
        try
        {
            Assert.True(f.Workspace.Busy,"The capture-to-original transition must remain owned so normal close waits for it.");
            Assert.False(page.CanEdit);Assert.False(page.ReadCommand.CanExecute(null));Assert.False(close.IsCompleted);
            if(changeClient){f.ReplaceConnection();page.Refresh();}
        }
        finally{release.TrySetResult();if(changeClient)await Assert.ThrowsAnyAsync<ToolkitException>(()=>read);else await read;await close;}
        Assert.True(page.HasPendingOriginal);Assert.False(f.Workspace.Busy);
        if(changeClient)Assert.Contains("No current",page.Outcome);
    }
    [Fact] public async Task Original_save_and_reopen_preserve_bytes_without_another_source_read_or_snapshot_change()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Reports.Read.All");var reads=0;
        var page=Page(f,(id,_)=>{reads++;return Task.FromResult(Header(id));});page.Sources[0].Selected=true;page.Acknowledged=true;
        await page.ReadAsync();Assert.True(page.HasPendingOriginal);Assert.Contains("Collected",page.Outcome);Assert.False(page.ReadCommand.CanExecute(null));
        await page.SavePendingOriginalAsync();Assert.False(page.HasPendingOriginal);Assert.Equal(1,reads);Assert.Null(f.Workspace.Snapshot);Assert.Null(f.Workspace.Plan);
        var original=File.ReadAllBytes(page.LastExport);await page.OpenSuppliedAsync(page.LastExport);await page.ExportAsync(ExportFormat.Json);
        Assert.Equal(original,File.ReadAllBytes(page.LastExport));Assert.Equal(1,reads);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Theory][InlineData(false)][InlineData(true)] public async Task Changed_selection_during_source_read_never_becomes_current_or_pending_evidence(bool aba)
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Reports.Read.All");UsageReportsViewModel? page=null;
        page=Page(f,(id,_)=>{page!.Period="D7";if(aba)page.Period="D30";return Task.FromResult(Header(id));});page.Sources[0].Selected=true;page.Acknowledged=true;
        await Assert.ThrowsAnyAsync<ToolkitException>(page.ReadAsync);Assert.False(page.HasPendingOriginal);Assert.Contains("No current",page.Outcome);
    }
    [Fact] public async Task Client_context_change_clears_current_rows_but_preserves_original_for_explicit_export()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Reports.Read.All");var reads=0;
        var page=Page(f,(id,_)=>{reads++;return Task.FromResult(Header(id));});page.Sources[0].Selected=true;page.Acknowledged=true;await page.ReadAsync();
        f.ReplaceConnection();page.Refresh();Assert.Contains("No current",page.Outcome);Assert.True(page.HasPendingOriginal);
        Assert.Contains(TestData.TenantA,page.PendingSummary);await page.ExportPendingOriginalAsync();Assert.False(page.HasPendingOriginal);
        Assert.Equal(TestData.TenantA,UsageReviewImport.ReadFile(page.LastExport,TestData.TenantA).Report.TenantId);Assert.Equal(1,reads);
    }
    [Fact] public async Task Failed_save_retains_original_and_discard_requires_explicit_confirmation_without_rerun()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Reports.Read.All");var reads=0;
        var page=Page(f,(id,_)=>{reads++;return Task.FromResult(Header(id));});page.Sources[0].Selected=true;page.Acknowledged=true;await page.ReadAsync();
        var blocked=Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA),"usage-review-evidence");Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);File.WriteAllText(blocked,"synthetic file blocks directory");
        await Assert.ThrowsAnyAsync<IOException>(page.SavePendingOriginalAsync);Assert.True(page.HasPendingOriginal);
        page.ConfirmDiscard=_=>false;page.DiscardOriginalCommand.Execute(null);Assert.True(page.HasPendingOriginal);
        page.ConfirmDiscard=_=>true;page.DiscardOriginalCommand.Execute(null);Assert.False(page.HasPendingOriginal);Assert.Equal(1,reads);
    }
    [Fact]
    public async Task Parent_refresh_clears_nested_usage_from_the_former_client_without_waiting_for_a_command()
    {
        using var f = new ConnectedReportsFixture(); f.Workspace.Session!.Scopes.Add("Reports.Read.All");
        var shell = new ShellViewModel(f.Workspace); var parent = shell.Page<GraphReportsViewModel>();
        var report = await new UsageReportService(SystemClock.Instance, "synthetic").CaptureAsync(
            new Source((id, _) => Task.FromResult(Header(id))), f.Workspace.Session!, ["email-activity"], "D30", () => { }, default);
        var file = Path.Combine(f.Root.Root, "synthetic usage.json"); File.WriteAllText(file, UsageReviewSchema.Serialise(report));
        await parent.Usage.OpenSuppliedAsync(file); Assert.DoesNotContain("No current", parent.Usage.Outcome);
        var changed = new List<string?>(); parent.Usage.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        f.ReplaceConnection(); parent.Refresh();
        // Read these before CanExecute: a command predicate must not be needed to clear another client's view.
        Assert.Contains("No current", parent.Usage.Outcome); Assert.Null(parent.Usage.Rows);
        Assert.Contains(nameof(UsageReportsViewModel.Outcome), changed);
        Assert.False(parent.Usage.JsonCommand.CanExecute(null)); Assert.Equal(0, f.Tokens.Calls); Assert.Equal(0, f.Handler.Calls);
    }
    [Fact] public void Changing_optional_access_cannot_reuse_an_ordinary_session_or_keep_a_setup_preview()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.ClientId=BuildStandard.Core.Configuration.ToolkitSettings.MicrosoftGraphPowerShellClientId;Assert.True(f.Workspace.CanReuseConnection(f.Workspace.Profile!,SessionMode.Assessment));
        Assert.False(f.Workspace.CanReuseConnection(f.Workspace.Profile!,SessionMode.Assessment,OptionalReportAccess.UsageReports));
        f.Workspace.Session!.Scopes.Add("Reports.Read.All");
        Assert.False(f.Workspace.CanReuseConnection(f.Workspace.Profile!,SessionMode.Assessment,OptionalReportAccess.UsageReports));
    }
}
