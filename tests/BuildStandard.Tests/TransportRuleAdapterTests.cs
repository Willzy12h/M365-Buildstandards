using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class TransportRuleAdapterTests
{
    public static IEnumerable<object[]> Cases()=>ExchangeReportRunnerTests.Runtimes().Cast<object[]>().SelectMany(runtime=>
        new[]{"transport-values","transport-inventory","transport-empty","transport-broad","transport-missing-guid","transport-null-state","transport-duplicate","transport-failed","transport-cap","wrong-account","wrong-tenant"}.Select(scenario=>new object[]{runtime[0],scenario}));
    [Theory][MemberData(nameof(ExchangeReportRunnerTests.Runtimes),MemberType=typeof(ExchangeReportRunnerTests))]
    public async Task Stop_in_later_rule_keeps_earlier_metadata_and_unknown_stopped_counts(string runtime)
    {
        using var root=new TempRoot();using var stop=new CancellationTokenSource();var calls=0;
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,"transport-stop");
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new BuildStandard.Core.SystemClock());
        var progress=new InlineProgress(text=>{if(text.StartsWith("Reading Get-TransportRule",StringComparison.Ordinal)&&++calls==2)stop.Cancel();});
        var result=await runner.RunAsync(ScriptCatalogue.Shipped.Find("exo.transport-rule-observations"),new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),
            new Dictionary<string,string?>{["RuleGuids"]=TestData.Office+","+TestData.TenantB,["OrganisationInventory"]="false"},runtime,progress,stop.Token);
        Assert.Equal(ReadRunOutcome.Cancelled,result.Outcome);Assert.NotNull(result.Evidence);Assert.Equal("Cancelled",result.Evidence.Status);
        Assert.Equal(2,result.Evidence.Sections[0].Rows.Count);Assert.Single(result.Evidence.Sections[1].Rows);
        Assert.Equal("Collected",result.Evidence.Sections[0].Rows[0]["readStatus"]!.GetValue<string>());
        Assert.Equal("Failed",result.Evidence.Sections[0].Rows[1]["readStatus"]!.GetValue<string>());Assert.Null(result.Evidence.Sections[0].Rows[1]["returnedEntries"]);
    }
    private sealed class InlineProgress(Action<string> report):IProgress<string>{public void Report(string text)=>report(text);}
    [Theory][MemberData(nameof(Cases))]
    public async Task Pinned_metadata_adapter_preserves_scope_unknowns_and_source_caps(string runtime,string scenario)
    {
        using var root=new TempRoot();var entry=ScriptCatalogue.Shipped.Find("exo.transport-rule-observations");
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,scenario);
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new BuildStandard.Core.SystemClock());
        var inventory=scenario is "transport-inventory" or "transport-empty" or "transport-missing-guid" or "transport-cap";
        var result=await runner.RunAsync(entry,new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),
            new Dictionary<string,string?>{["RuleGuids"]=inventory?"":TestData.Office,["OrganisationInventory"]=inventory?"true":"false"},runtime,null,CancellationToken.None);
        var log=File.Exists(environment["BDIT_FAKE_LOG"])?File.ReadAllText(environment["BDIT_FAKE_LOG"]):"";
        if(scenario.StartsWith("wrong-",StringComparison.Ordinal)){Assert.Null(result.Evidence);Assert.DoesNotContain("Read ",log);return;}
        Assert.True(result.Evidence is not null,"Outcome="+result.Outcome+"; "+result.Reason);var report=result.Evidence!;
        var target=Assert.Single(report.Sections[0].Rows);var policies=report.Sections[1].Rows;
        Assert.Equal(inventory?"OrganisationInventory":"SelectedRule",target["scope"]!.GetValue<string>());
        if(scenario is "transport-failed" or "transport-broad" or "transport-duplicate")
        {Assert.Equal("Failed",target["readStatus"]!.GetValue<string>());Assert.Null(target["returnedEntries"]);Assert.Empty(policies);}
        else if(scenario=="transport-empty") {Assert.Equal("Collected",report.Status);Assert.Equal(0,target["returnedEntries"]!.GetValue<int>());Assert.Empty(policies);}
        else if(scenario=="transport-missing-guid") {Assert.Equal("Partial",report.Status);Assert.Equal(1,target["excludedEntries"]!.GetValue<int>());Assert.Empty(policies);}
        else if(scenario=="transport-cap") {Assert.Equal("Partial",report.Status);Assert.Equal(target["requestedResultSize"]!.GetValue<int>(),target["returnedEntries"]!.GetValue<int>());}
        else
        {
            var policy=Assert.Single(policies);Assert.Equal(TestData.Office,policy["id"]!.GetValue<string>());Assert.Equal(TestData.Office,policy["ruleGuid"]!.GetValue<string>());
            Assert.Equal(scenario=="transport-null-state"?"ReturnedNull":"Value",policy["state"]!["state"]!.GetValue<string>());
            if(scenario=="transport-null-state")Assert.Equal("Partial",report.Status);else Assert.Equal("Collected",report.Status);
            Assert.Equal(0,policy["priority"]!["value"]!.GetValue<int>());Assert.DoesNotContain("Actions",policy.ToJsonString());
        }
        Assert.Contains("ExcludeDetails=True",log);Assert.DoesNotContain("Set-",log);Assert.DoesNotContain("Remove-",log);
        if(inventory)Assert.DoesNotContain("Identity="+TestData.Office,log);else Assert.Contains("Identity="+TestData.Office,log);
    }
}
