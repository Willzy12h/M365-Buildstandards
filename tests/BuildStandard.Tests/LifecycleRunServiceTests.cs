using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Execution;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleRunServiceTests
{
    private sealed class Graph : IGraphClient
    {
        public string TenantId=>TestData.TenantA;public SessionMode Mode=>SessionMode.Deployment;public int Writes;public Action? BeforeSend;public Action? Sent;public Exception? Failure;
        public Task<JsonObject> GetAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<JsonObject> WriteAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject payload,CancellationToken ct)=>throw new InvalidOperationException("Generic lifecycle write forbidden");
        public Task<JsonObject> WriteReviewedAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject payload,ReviewedWriteAdmission admission,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<JsonObject> ApplyLifecycleAsync(TenantSession session,LifecycleOperation operation,ReviewedWriteAdmission admission,CancellationToken ct)
        {
            BeforeSend?.Invoke();try{admission.AssertAllowed();}catch(Exception error){throw new WriteNotSentException("Synthetic final refusal",error);}
            Writes++;Sent?.Invoke();if(Failure is not null)throw Failure;return Task.FromResult(new JsonObject());
        }
    }
    private static object Service(LifecyclePreparationTests.Fixture f)
    {var type=typeof(ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecycleRunService");Assert.NotNull(type);return Activator.CreateInstance(type,f.Evidence,f.Clock)!;}
    private static object Invoke(object service,string method,params object[] args)
    {var member=service.GetType().GetMethod(method);Assert.NotNull(member);try{return member.Invoke(service,args)!;}catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
    private static object Approve(object service,LifecyclePreparationTests.Fixture f,object preview,string? digest=null,bool protectedReviewed=true,bool downstreamReviewed=true)
    {var document=(JsonObject)preview.GetType().GetMethod("GetDocument")!.Invoke(preview,null)!;return Invoke(service,"Approve",preview,f.Session,f.Profile,f.Standard,f.Snapshot,digest??document["integrityDigest"]!.GetValue<string>(),protectedReviewed,downstreamReviewed,false,(Action)(()=>{}));}
    private static Task<JsonObject> Execute(object service,LifecyclePreparationTests.Fixture f,Graph graph,object approval,Action? current=null,CancellationToken ct=default)
    {
        Func<LifecycleOperation,ILifecycleReadSource> factory=_=>new LifecyclePreparationTests.Source {ReadEnabled=f.Source.ReadEnabled,Before=f.Source.Before};
        return (Task<JsonObject>)Invoke(service,"ExecuteAsync",graph,f.Session,f.Profile,f.Standard,f.Snapshot,approval,factory,current??(()=>{}),ct);
    }
    [Theory][InlineData("digest")][InlineData("protected")][InlineData("downstream")]
    public async Task Exact_approval_requires_explicit_target_reviews_and_original_digest(string fault)
    {using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var service=Service(f);Assert.ThrowsAny<ToolkitException>(()=>Approve(service,f,preview,fault=="digest"?"wrong":null,fault!="protected",fault!="downstream"));}
    [Fact]public async Task One_send_has_durable_intent_exact_readback_and_no_replay()
    {
        using var f=new LifecyclePreparationTests.Fixture();var enabled=true;f.Source.ReadEnabled=()=>enabled;
        var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);
        var graph=new Graph {Sent=()=>enabled=false,BeforeSend=()=>Assert.Single(Directory.GetFiles(Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle"),"intent-*.json"))};
        var run=await Execute(service,f,graph,approval);Assert.Equal(1,graph.Writes);Assert.Equal(WriteAcceptance.Accepted,run["writeAcceptance"]?.GetValue<string>());Assert.Equal(ConfigurationVerification.Pass,run["verification"]?.GetValue<string>());
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>Execute(service,f,graph,approval));Assert.Equal(1,graph.Writes);
    }
    [Theory][InlineData("changed-before")][InlineData("changed-profile")][InlineData("expired")][InlineData("stop")]
    public async Task Final_refusal_or_changed_preflight_is_known_not_sent_and_consumes_attempt(string fault)
    {
        using var f=new LifecyclePreparationTests.Fixture();var enabled=true;f.Source.ReadEnabled=()=>enabled;var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);using var stop=new CancellationTokenSource();
        if(fault=="changed-before")enabled=false;
        var graph=new Graph {BeforeSend=()=>{if(fault=="changed-profile")f.Profile.Parameters.EmergencyAccountIds.Add(TestData.Mam);if(fault=="expired")f.Clock.UtcNow=f.Clock.UtcNow.AddMinutes(11);if(fault=="stop")stop.Cancel();}};
        var run=await Execute(service,f,graph,approval,ct:stop.Token);Assert.Equal(0,graph.Writes);Assert.Equal(WriteAcceptance.NotAttempted,run["writeAcceptance"]?.GetValue<string>());
        if(fault=="stop")Assert.Equal(RunStatus.Stopped,run["status"]?.GetValue<string>());
    }
    [Fact]public async Task Transport_uncertainty_is_durable_blocks_further_writes_and_is_never_retried()
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);var graph=new Graph {Failure=new AmbiguousWriteException("Synthetic uncertain request",null)};
        var run=await Execute(service,f,graph,approval);Assert.Equal(1,graph.Writes);Assert.Equal(WriteAcceptance.Unknown,run["writeAcceptance"]?.GetValue<string>());
        var guard=f.Evidence.GetType().GetMethod("AssertNoUnresolvedLifecycle");Assert.NotNull(guard);var refusal=Assert.Throws<TargetInvocationException>(()=>guard.Invoke(f.Evidence,new object[]{TestData.TenantA}));Assert.IsAssignableFrom<ToolkitException>(refusal.InnerException);
    }
    [Fact]public async Task A_crash_after_intent_cannot_become_known_not_sent_or_replayable()
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);var graph=new Graph {Failure=new OutOfMemoryException("Synthetic fatal interruption; no allocation")};
        await Assert.ThrowsAsync<OutOfMemoryException>(()=>Execute(service,f,graph,approval));var directory=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle");Assert.Single(Directory.GetFiles(directory,"intent-*.json"));Assert.Empty(Directory.GetFiles(directory,"run-*.json"));Assert.Equal(1,graph.Writes);
    }
    [Theory][InlineData("deployment")][InlineData("reviewed")][InlineData("recovery")][InlineData("laps")][InlineData("package")]
    public async Task Earlier_unresolved_write_families_block_lifecycle_before_new_intent_or_request(string family)
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);var id=Guid.NewGuid().ToString("D");
        switch(family)
        {
            case "deployment":f.Evidence.SaveRun(new DeploymentRun {Id=id,TenantId=TestData.TenantA,StartedAt=Timestamps.Format(f.Clock.UtcNow),Status=RunStatus.ReviewRequired,Results=[new RunResult {ControlId="historical-control",Status=ResultStatus.Error,PlannedAction="Create",WriteAcceptance=WriteAcceptance.Unknown}]});break;
            case "reviewed":f.Evidence.SaveReviewedChangeRun(new ReviewedChangeRun {Id=id,TenantId=TestData.TenantA,ControlId="historical-control",WriteAcceptance=WriteAcceptance.Unknown});break;
            case "recovery":f.Evidence.SaveRecoveryRun(new RecoveryRun {Id=id,TenantId=TestData.TenantA,ControlId="historical-control",WriteAcceptance=WriteAcceptance.Unknown});break;
            case "laps":f.Evidence.SaveEntraLapsRun(new EntraLapsRun {Id=id,TenantId=TestData.TenantA,WriteAcceptance=WriteAcceptance.Unknown});break;
            case "package":f.Evidence.SavePackageRun(new PackagePublishRun {Id=id,TenantId=TestData.TenantA,Steps=[new PackagePublishStep {Acceptance=WriteAcceptance.Unknown}]});break;
        }
        var graph=new Graph();await Assert.ThrowsAnyAsync<ToolkitException>(()=>Execute(service,f,graph,approval));Assert.Equal(0,graph.Writes);
        Assert.Empty(Directory.GetFiles(Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle"),"intent-*.json"));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task Read_only_reconciliation_preserves_original_acceptance_and_never_resends(bool uncertain)
    {
        using var f=new LifecyclePreparationTests.Fixture();var enabled=true;f.Source.ReadEnabled=()=>enabled;
        var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);
        var graph=new Graph {Failure=uncertain?new AmbiguousWriteException("Synthetic uncertainty",null):null};
        var run=await Execute(service,f,graph,approval);var id=run["id"]!.GetValue<string>();
        var file=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle","run-"+id+".json");var original=File.ReadAllBytes(file);
        enabled=false;
        Func<LifecycleOperation,ILifecycleReadSource> reads=_=>new LifecyclePreparationTests.Source {ReadEnabled=()=>enabled};
        var verification=await (Task<JsonObject>)Invoke(service,"ReconcileAsync",f.Session,f.Profile,f.Standard,id,reads,(Action)(()=>{}),CancellationToken.None);
        Assert.True(verification["verified"]!.GetValue<bool>());Assert.Equal(original,File.ReadAllBytes(file));Assert.Equal(1,graph.Writes);
        var guard=f.Evidence.GetType().GetMethod("AssertNoUnresolvedLifecycle")!;
        if(uncertain)Assert.Throws<TargetInvocationException>(()=>guard.Invoke(f.Evidence,new object[]{TestData.TenantA}));
        else guard.Invoke(f.Evidence,new object[]{TestData.TenantA});
    }

    [Fact]public async Task Reconciliation_does_not_relabel_known_not_sent_as_unknown_acceptance()
    {
        using var f=new LifecyclePreparationTests.Fixture();var enabled=true;f.Source.ReadEnabled=()=>enabled;
        var preview=await f.Prepare();var service=Service(f);var approval=Approve(service,f,preview);enabled=false;
        var graph=new Graph();var run=await Execute(service,f,graph,approval);Assert.Equal(WriteAcceptance.NotAttempted,run["writeAcceptance"]?.GetValue<string>());
        Func<LifecycleOperation,ILifecycleReadSource> reads=_=>new LifecyclePreparationTests.Source {ReadEnabled=()=>enabled};
        var verification=await (Task<JsonObject>)Invoke(service,"ReconcileAsync",f.Session,f.Profile,f.Standard,run["id"]!.GetValue<string>(),reads,(Action)(()=>{}),CancellationToken.None);
        Assert.False(verification["acceptanceRemainsUnresolved"]!.GetValue<bool>());Assert.Equal(0,graph.Writes);
        Assert.Equal(WriteAcceptance.NotAttempted,verification["originalWriteAcceptance"]?.GetValue<string>());
    }

}
