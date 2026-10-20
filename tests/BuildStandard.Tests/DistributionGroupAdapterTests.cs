using BuildStandard.Core;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class DistributionGroupAdapterTests
{
    public static IEnumerable<object[]> Cases()=>ExchangeReportRunnerTests.Runtimes().Cast<object[]>().SelectMany(runtime=>
        new[]{"group-values","group-raw-member","group-empty","group-duplicate","group-failed","group-unsupported","group-unresolved-member","group-unresolved-owner","group-null-flag","wrong-account","wrong-tenant"}.Select(scenario=>new object[]{runtime[0],scenario}));
    [Theory][MemberData(nameof(ExchangeReportRunnerTests.Runtimes),MemberType=typeof(ExchangeReportRunnerTests))]
    public async Task Stop_in_later_group_keeps_earlier_members_and_unknown_counts_for_the_stopped_source(string runtime)
    {
        using var root=new TempRoot();using var stop=new CancellationTokenSource();var calls=0;
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,"group-stop");
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var progress=new InlineProgress(text=>{if(text.StartsWith("Reading Get-DistributionGroupMember",StringComparison.Ordinal)&&++calls==2)stop.Cancel();});
        var result=await runner.RunAsync(ScriptCatalogue.Shipped.Find("exo.distribution-group-observations"),new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),
            new Dictionary<string,string?>{["GroupGuids"]=TestData.Office+","+TestData.TenantB},runtime,progress,stop.Token);
        Assert.Equal(ReadRunOutcome.Cancelled,result.Outcome);Assert.NotNull(result.Evidence);Assert.Equal("Cancelled",result.Evidence.Status);
        Assert.Equal(2,result.Evidence.Sections[0].Rows.Count);Assert.Single(result.Evidence.Sections[1].Rows);
        Assert.Equal("Collected",result.Evidence.Sections[0].Rows[0]["membershipReadStatus"]!.GetValue<string>());
        Assert.Equal("Failed",result.Evidence.Sections[0].Rows[1]["membershipReadStatus"]!.GetValue<string>());Assert.Null(result.Evidence.Sections[0].Rows[1]["returnedEntries"]);
    }
    private sealed class InlineProgress(Action<string> report):IProgress<string>{public void Report(string value)=>report(value);}
    [Theory][MemberData(nameof(Cases))]
    public async Task Pinned_group_adapter_preserves_direct_source_identity_and_unknown_membership(string runtime,string scenario)
    {
        using var root=new TempRoot();var entry=ScriptCatalogue.Shipped.Find("exo.distribution-group-observations");
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,scenario);
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var result=await runner.RunAsync(entry,new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),new Dictionary<string,string?>{["GroupGuids"]=TestData.Office},runtime,null,CancellationToken.None);
        var log=File.Exists(environment["BDIT_FAKE_LOG"])?File.ReadAllText(environment["BDIT_FAKE_LOG"]):"";
        if(scenario.StartsWith("wrong-",StringComparison.Ordinal)){Assert.Null(result.Evidence);Assert.DoesNotContain("Read ",log);return;}
        Assert.True(result.Evidence is not null,"Outcome="+result.Outcome+"; "+result.Reason);var report=result.Evidence!;
        var target=Assert.Single(report.Sections[0].Rows);Assert.Equal(TestData.Office,target["observedGroupGuid"]!.GetValue<string>());
        if(scenario=="group-unsupported")
        {Assert.Equal("NotAttempted",target["membershipReadStatus"]!.GetValue<string>());Assert.Null(target["returnedEntries"]);Assert.Empty(report.Sections[1].Rows);Assert.DoesNotContain("Read Get-DistributionGroupMember",log);}
        else if(scenario is "group-failed" or "group-duplicate")
        {Assert.Equal("Failed",target["membershipReadStatus"]!.GetValue<string>());Assert.Null(target["returnedEntries"]);Assert.Empty(report.Sections[1].Rows);}
        else if(scenario=="group-empty")
        {Assert.Equal(0,target["returnedEntries"]!.GetValue<int>());Assert.Empty(report.Sections[1].Rows);}
        else
        {
            var member=Assert.Single(report.Sections[1].Rows);
            Assert.Equal(scenario=="group-unresolved-member"?"UnresolvedDisplayLabel":scenario=="group-raw-member"?"RawIdentity":"ExchangeRecipientGuid",member["sourceIdentity"]!["namespace"]!.GetValue<string>());
            if(scenario=="group-raw-member")
            {Assert.Equal("original-member",member["sourceIdentity"]!["value"]!.GetValue<string>());var directory=member["sourceIdentity"]!["externalDirectoryObjectId"];Assert.NotNull(directory);Assert.Equal(TestData.Operator,directory.GetValue<string>());}
            else if(scenario!="group-unresolved-member")Assert.Equal("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",member["sourceIdentity"]!["value"]!.GetValue<string>());
            if(scenario=="group-null-flag"){Assert.Equal("ReturnedNull",target["requireSenderAuthenticationEnabled"]!["state"]!.GetValue<string>());Assert.Null(target["requireSenderAuthenticationEnabled"]!["value"]);}
            else Assert.False(target["requireSenderAuthenticationEnabled"]!["value"]!.GetValue<bool>());
        }
        Assert.Equal(scenario is "group-values" or "group-raw-member" or "group-empty"?"Collected":"Partial",report.Status);
        Assert.DoesNotContain("Get-UnifiedGroup",log);Assert.DoesNotContain("Set-",log);Assert.DoesNotContain("Add-",log);Assert.DoesNotContain("Remove-",log);
    }
}
