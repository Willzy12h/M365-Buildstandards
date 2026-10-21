using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Evidence;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleRunEvidenceTests
{
    private static object? Call(EvidenceStore store,string method,params object[] args)
    {
        var member=store.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);Assert.NotNull(member);
        try{return member.Invoke(store,args);}catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    private static JsonObject Record(string family,string id)=>new(){["kind"]="lifecycle-"+family,["schemaVersion"]=1,["id"]=id,["tenantId"]=TestData.TenantA,["integrityDigest"]=""};
    [Fact]public void Intent_is_durable_immutable_and_blocks_other_writes_until_a_known_outcome()
    {
        using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);var id=Guid.NewGuid().ToString("D");
        Call(evidence,"SaveLifecycleIntent",Record("intent",id));
        var refusal=Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantA));Assert.Contains("lifecycle",refusal.Message,StringComparison.OrdinalIgnoreCase);
        Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"SaveLifecycleIntent",Record("intent",id)));
        var outcome=Record("run",id);outcome["writeAcceptance"]=BuildStandard.Core.Models.WriteAcceptance.NotAttempted;outcome["verification"]="NotRun";
        Call(evidence,"SaveLifecycleOutcome",outcome);Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantA);
        Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"SaveLifecycleOutcome",outcome));
    }
    [Theory][InlineData("Unknown")][InlineData("FutureValue")][InlineData("Accepted")]
    public void Uncertain_or_unverified_acceptance_never_becomes_a_safe_bucket(string acceptance)
    {
        using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);var id=Guid.NewGuid().ToString("D");
        Call(evidence,"SaveLifecycleIntent",Record("intent",id));var outcome=Record("run",id);outcome["writeAcceptance"]=acceptance;outcome["verification"]="NotRun";
        Call(evidence,"SaveLifecycleOutcome",outcome);Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantA));
    }
    [Fact]public void Corrupt_record_or_unknown_schema_is_not_skipped_while_searching_for_a_success()
    {
        using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);var id=Guid.NewGuid().ToString("D");Call(evidence,"SaveLifecycleIntent",Record("intent",id));
        var file=Path.Combine(evidence.TenantDirectory(TestData.TenantA),"lifecycle","intent-"+id+".json");var data=JsonNode.Parse(File.ReadAllText(file))!.AsObject();data["schemaVersion"]=2;data["integrityDigest"]="";data["integrityDigest"]=CanonicalJson.Sha256(data);File.WriteAllText(file,data.ToJsonString());
        Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantA));
    }
    [Fact]public void Verified_effect_does_not_clear_unknown_write_acceptance()
    {
        using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);var id=Guid.NewGuid().ToString("D");Call(evidence,"SaveLifecycleIntent",Record("intent",id));var outcome=Record("run",id);outcome["writeAcceptance"]="Unknown";outcome["verification"]="Pass";
        Call(evidence,"SaveLifecycleOutcome",outcome);Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantA));
    }
    [Fact]public void Another_client_cannot_inherit_this_clients_lifecycle_block_or_record()
    {using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);Call(evidence,"SaveLifecycleIntent",Record("intent",Guid.NewGuid().ToString("D")));Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantB);}
    [Theory][InlineData("reviewed")][InlineData("laps")][InlineData("completion")]
    public void Existing_write_and_completion_guards_refuse_pending_lifecycle_while_read_only_lease_remains_available(string family)
    {
        using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);
        Call(evidence,"SaveLifecycleIntent",Record("intent",Guid.NewGuid().ToString("D")));
        using(var lease=evidence.AcquireTenantWriteLease(TestData.TenantA)) { } // a read-only reconciliation is not blocked by the lease itself
        Action check=family switch
        {
            "reviewed"=>()=>evidence.AssertNoUnresolvedReviewedChanges(TestData.TenantA),
            "laps"=>()=>evidence.AssertNoUnresolvedEntraLaps(TestData.TenantA),
            _=>()=>evidence.AssertCompletionWritesResolved(TestData.TenantA,new[]{"CA-001"})
        };
        var error=Assert.ThrowsAny<ToolkitException>(check);Assert.Contains("lifecycle",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]public void A_pass_label_without_its_complete_original_readback_cannot_clear_a_write()
    {
        using var root=new TempRoot();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);var id=Guid.NewGuid().ToString("D");
        Call(evidence,"SaveLifecycleIntent",Record("intent",id));var outcome=Record("run",id);
        outcome["writeAcceptance"]="Accepted";outcome["verification"]="Pass";
        Call(evidence,"SaveLifecycleOutcome",outcome);
        Assert.ThrowsAny<ToolkitException>(()=>Call(evidence,"AssertNoUnresolvedLifecycle",TestData.TenantA));
    }

}
