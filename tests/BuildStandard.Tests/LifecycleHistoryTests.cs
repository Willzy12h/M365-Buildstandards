using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Evidence;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleHistoryTests
{
    private static object Call(EvidenceStore evidence,string method,params object[] args)
    {
        var member=evidence.GetType().GetMethod(method,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);Assert.NotNull(member);
        try{return member.Invoke(evidence,args)!;}catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    [Fact]public async Task Historical_record_preserves_exact_original_bytes_but_cannot_recreate_runtime_approval()
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var id=(string)preview.GetType().GetProperty("Id")!.GetValue(preview)!;
        var file=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle","plan-"+id+".json");var bytes=Encoding.UTF8.GetBytes(" \n"+File.ReadAllText(file)+"\n");File.WriteAllBytes(file,bytes);
        var historical=Call(f.Evidence,"ReadLifecycleRecord",TestData.TenantA,"plan",id);
        var copy=(byte[])historical.GetType().GetMethod("GetOriginalBytes")!.Invoke(historical,null)!;Assert.Equal(bytes,copy);copy[0]=0;
        Assert.Equal(bytes,(byte[])historical.GetType().GetMethod("GetOriginalBytes")!.Invoke(historical,null)!);
        Assert.Empty(historical.GetType().GetConstructors());Assert.NotEqual(preview.GetType(),historical.GetType());
        Assert.ThrowsAny<ToolkitException>(()=>Call(f.Evidence,"ReadLifecycleRecord",TestData.TenantB,"plan",id));
        Assert.ThrowsAny<ToolkitException>(()=>Call(f.Evidence,"ReadLifecycleRecord",TestData.TenantA,"../cache",id));
    }
    [Fact]public async Task Intent_only_history_is_explicit_unknown_not_no_actions_or_known_not_sent()
    {
        using var f=new LifecyclePreparationTests.Fixture();var preview=await f.Prepare();var id=(string)preview.GetType().GetProperty("Id")!.GetValue(preview)!;var digest=(string)preview.GetType().GetProperty("IntegrityDigest")!.GetValue(preview)!;
        Call(f.Evidence,"SaveLifecycleIntent",new JsonObject { ["kind"]="lifecycle-intent",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["planDigest"]=digest,["startedAt"]=Timestamps.Format(f.Clock.UtcNow),["integrityDigest"]="" });
        var rows=((System.Collections.IEnumerable)Call(f.Evidence,"ListLifecycleAttempts",TestData.TenantA,5000,CancellationToken.None)).Cast<object>().ToArray();var row=Assert.Single(rows);
        Assert.Equal("Unknown",row.GetType().GetProperty("WriteAcceptance")!.GetValue(row));
        Assert.Equal("No final outcome",row.GetType().GetProperty("Status")!.GetValue(row));
        Assert.Equal("disable-account",row.GetType().GetProperty("ActionId")!.GetValue(row));
    }
    [Fact]public void An_outcome_without_its_intent_is_unreadable_history_not_a_successful_empty_list()
    {
        using var f=new LifecyclePreparationTests.Fixture();var id=Guid.NewGuid().ToString("D");
        var record=new JsonObject { ["kind"]="lifecycle-run",["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["writeAcceptance"]="Unknown",["integrityDigest"]="" };record["integrityDigest"]=CanonicalJson.Sha256(record);
        var file=Path.Combine(f.Evidence.TenantDirectory(TestData.TenantA),"lifecycle","run-"+id+".json");f.Evidence.WriteJsonAtomic(file,record);
        Assert.ThrowsAny<ToolkitException>(()=>Call(f.Evidence,"ListLifecycleAttempts",TestData.TenantA,5000,CancellationToken.None));
    }

}
