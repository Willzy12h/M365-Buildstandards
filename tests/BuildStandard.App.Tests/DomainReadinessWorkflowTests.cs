using System.Text;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Exchange;
using Xunit;

namespace BuildStandard.Tests;
public sealed class DomainReadinessWorkflowTests
{
    private static DomainReadinessRequest Request()=>new("example.invalid",[DomainReadinessCheck.Mx]);
    private static DomainReadinessReport Report(DomainReadinessRequest request,bool cancelled=false)
    {
        var r=new DomainReadinessReport{Id=Guid.NewGuid().ToString(),RunId=Guid.NewGuid().ToString(),Domain=request.Domain,
            TenantId=request.TenantId,AccountObjectId=request.AccountObjectId,ToolkitVersion="synthetic",StartedAt="2026-10-18T12:00:00.000Z",CompletedAt="2026-10-18T12:00:01.000Z",
            RequestedChecks=request.Checks.ToList(),Status=cancelled?DomainReadinessState.Cancelled:DomainReadinessState.Complete,
            Results=[new(DomainReadinessCheck.Mx,cancelled?DomainReadinessMeaning.UnableToCheck:DomainReadinessMeaning.Observed,"Synthetic result",[],"Ask the DNS owner.")]};
        if(!cancelled)r.Observations.Add(new(r.Domain,DomainDnsType.MX,DomainDnsState.Observed,r.StartedAt,r.CompletedAt,DomainDnsResponse.Resolver,"1",[new(r.Domain,DomainDnsType.MX,"Answer",Exchange:"gateway.invalid",Preference:10)]));
        r.IntegrityDigest=EvidenceIntegrity.Compute(r);return r;
    }
    [Fact] public void Navigation_exposes_domain_readiness_without_connecting_or_querying()
    {
        using var f=new ConnectedReportsFixture(false);var shell=new ShellViewModel(f.Workspace);
        Assert.Contains(RouteCatalog.Items,r=>r.Key=="domain-readiness"&&r.Group=="Reports");shell.Navigate("domain-readiness");
        Assert.Equal("Domain readiness",shell.CurrentPageTitle);Assert.Equal(0,f.Handler.Calls);Assert.Equal(0,f.Tokens.Calls);
    }
    [Fact] public async Task An_offline_client_is_never_invented_as_a_verified_domain_binding()
    {
        using var f=new ConnectedReportsFixture(false);var calls=0;
        var controller=new DomainReadinessController(f.Workspace,(r,_)=>{calls++;return Task.FromResult(Report(r));});
        await controller.CollectAsync(Request(),true,()=>true);
        Assert.Equal(1,calls);Assert.Null(controller.Current!.Report.TenantId);Assert.Null(controller.Current.Report.AccountObjectId);
        Assert.True(controller.HasPending);Assert.Null(f.Workspace.Snapshot);Assert.Null(f.Workspace.Connection);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public async Task Only_the_actual_verified_connection_supplies_binding_without_new_authentication()
    {
        using var f=new ConnectedReportsFixture();var controller=new DomainReadinessController(f.Workspace,(r,_)=>Task.FromResult(Report(r)));
        await controller.CollectAsync(Request(),true,()=>true);
        Assert.Equal(f.Workspace.Session!.TenantId,controller.Current!.Report.TenantId);Assert.Equal(f.Workspace.Session.AccountObjectId,controller.Current.Report.AccountObjectId);
        Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public async Task No_acknowledgement_or_a_supplied_binding_is_refused_before_any_query()
    {
        using var f=new ConnectedReportsFixture(false);var calls=0;var controller=new DomainReadinessController(f.Workspace,(r,_)=>{calls++;return Task.FromResult(Report(r));});
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.CollectAsync(Request(),false,()=>true));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.CollectAsync(Request() with{TenantId=TestData.TenantB},true,()=>true));
        Assert.Equal(0,calls);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Late_context_or_parameter_change_retains_original_only_and_requires_deliberate_recovery(bool parameter)
    {
        using var f=new ConnectedReportsFixture(false);var done=new TaskCompletionSource<DomainReadinessReport>(TaskCreationOptions.RunContinuationsAsynchronously);var selected=true;
        var controller=new DomainReadinessController(f.Workspace,(_,_)=>done.Task);var task=controller.CollectAsync(Request(),true,()=>selected);
        if(parameter)selected=false;
        else {var a=f.Workspace.Profile!;f.Workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB),false);f.Workspace.ApplyProfileToSession(a,false);}
        done.SetResult(Report(Request()));await task;
        Assert.Null(controller.Current);Assert.True(controller.HasPending);var digest=controller.Pending!.OriginalSha256;
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.ExportAsync(f.Root.Paths.ReportsDirectory));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.ExportAsync(f.Root.Paths.ReportsDirectory,true,false));
        var folder=await controller.ExportAsync(f.Root.Paths.ReportsDirectory,true,true);
        Assert.Equal(digest,CanonicalJson.Sha256Hex(File.ReadAllBytes(Path.Combine(folder,"report.json"))));Assert.False(controller.HasPending);Assert.Null(controller.Current);
    }
    [Fact] public async Task Pending_result_blocks_replacement_until_saved_exported_or_explicitly_discarded()
    {
        using var f=new ConnectedReportsFixture(false);var calls=0;var controller=new DomainReadinessController(f.Workspace,(r,_)=>{calls++;return Task.FromResult(Report(r));});
        await controller.CollectAsync(Request(),true,()=>true);var digest=controller.Pending!.OriginalSha256;
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.CollectAsync(Request(),true,()=>true));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.ImportAsync("unused",()=>true));
        Assert.ThrowsAny<ToolkitException>(()=>controller.Discard(false));Assert.Equal(digest,controller.Pending.OriginalSha256);Assert.Equal(1,calls);
        controller.Discard(true);Assert.False(controller.HasPending);Assert.Null(controller.Current);
    }
    [Fact] public async Task Failed_save_keeps_original_and_retry_only_saves_without_requerying()
    {
        using var f=new ConnectedReportsFixture(false);var calls=0;var controller=new DomainReadinessController(f.Workspace,(r,_)=>{calls++;return Task.FromResult(Report(r));});
        await controller.CollectAsync(Request(),true,()=>true);var blocker=Path.Combine(f.Root.Root,"blocked");File.WriteAllText(blocker,"keep");
        await Assert.ThrowsAnyAsync<IOException>(()=>controller.ExportAsync(blocker));Assert.True(controller.HasPending);
        var folder=await controller.ExportAsync(f.Root.Paths.ReportsDirectory);Assert.True(File.Exists(Path.Combine(folder,"report.json")));Assert.False(controller.HasPending);Assert.Equal(1,calls);
    }
    [Fact] public async Task Stop_preserves_truthful_cancelled_result_without_retry_or_deployment_evidence()
    {
        using var f=new ConnectedReportsFixture(false);var entered=new TaskCompletionSource();
        var controller=new DomainReadinessController(f.Workspace,async(r,ct)=>{entered.SetResult();try{await Task.Delay(Timeout.InfiniteTimeSpan,ct);}catch(OperationCanceledException){}return Report(r,true);});
        var run=controller.CollectAsync(Request(),true,()=>true);await entered.Task;f.Workspace.CancelOperation();await run;
        Assert.Equal(DomainReadinessState.Cancelled,controller.Pending!.Report.Status);Assert.True(controller.HasPending);Assert.Null(f.Workspace.Snapshot);Assert.False(f.Workspace.Busy);
    }
    [Fact] public async Task Imported_envelope_remains_historical_with_original_binding_and_no_run()
    {
        using var f=new ConnectedReportsFixture(false);var calls=0;var controller=new DomainReadinessController(f.Workspace,(r,_)=>{calls++;return Task.FromResult(Report(r));});
        var original=Report(Request() with{TenantId=TestData.TenantB});var file=Path.Combine(f.Root.Root,"historical.json");var bytes=Encoding.UTF8.GetBytes(ToolkitJson.Serialize(original));File.WriteAllBytes(file,bytes);
        await controller.ImportAsync(file,()=>true);Assert.True(controller.Current!.Historical);Assert.Equal(TestData.TenantB,controller.Current.Report.TenantId);
        Assert.Equal(bytes,controller.Current.OriginalBytes);Assert.False(controller.HasPending);Assert.Equal(0,calls);Assert.Null(f.Workspace.Snapshot);
    }
    [Fact] public async Task Malformed_collector_output_is_not_a_successful_empty_report()
    {
        using var f=new ConnectedReportsFixture(false);var controller=new DomainReadinessController(f.Workspace,(_,_)=>Task.FromResult(new DomainReadinessReport()));
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>controller.CollectAsync(Request(),true,()=>true));Assert.Null(controller.Current);Assert.False(controller.HasPending);
    }
}
