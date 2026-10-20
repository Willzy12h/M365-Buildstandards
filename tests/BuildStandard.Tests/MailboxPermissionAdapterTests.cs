using BuildStandard.Core;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class MailboxPermissionAdapterTests
{
    public static IEnumerable<object[]> Cases()=>ExchangeReportRunnerTests.Runtimes().Cast<object[]>().SelectMany(runtime=>
        new[]{"permission-values","permission-empty","permission-failed","permission-missing-flag","permission-null-rights","permission-unresolved","permission-all","wrong-account","wrong-tenant"}.Select(scenario=>new object[]{runtime[0],scenario}));
    [Theory][MemberData(nameof(ExchangeReportRunnerTests.Runtimes),MemberType=typeof(ExchangeReportRunnerTests))]
    public async Task Stop_during_later_source_keeps_the_earlier_permission_and_independent_unknown_counts(string runtime)
    {
        using var root=new TempRoot();using var stop=new CancellationTokenSource();
        var entry=ScriptCatalogue.Shipped.Find("exo.mailbox-permission-observations");
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,"permission-stop");
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var progress=new InlineProgress(text=>{if(text.StartsWith("Reading Get-EXORecipientPermission",StringComparison.Ordinal))stop.Cancel();});
        var result=await runner.RunAsync(entry,new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),new Dictionary<string,string?>
        { ["MailboxGuids"]=TestData.Office,["FullAccess"]="true",["SendAs"]="true" },runtime,progress,stop.Token);
        Assert.Equal(ReadRunOutcome.Cancelled,result.Outcome);Assert.NotNull(result.Evidence);Assert.Equal("Cancelled",result.Evidence.Status);
        var edge=Assert.Single(result.Evidence.Sections[1].Rows);Assert.Equal("FullAccess",edge["permissionKind"]!.GetValue<string>());
        var reads=Assert.Single(result.Evidence.Sections[0].Rows)["permissionReads"]!;
        Assert.Equal("Collected",reads[0]!["status"]!.GetValue<string>());Assert.Equal(1,reads[0]!["returnedEntries"]!.GetValue<int>());
        Assert.Equal("Failed",reads[1]!["status"]!.GetValue<string>());Assert.Null(reads[1]!["returnedEntries"]);
    }
    private sealed class InlineProgress(Action<string> report):IProgress<string>{public void Report(string value)=>report(value);}
    [Theory][MemberData(nameof(Cases))]
    public async Task Pinned_permissions_adapter_preserves_each_selected_source_and_unknowns_without_effective_access_claims(string runtime,string scenario)
    {
        using var root=new TempRoot();
        var entry=ScriptCatalogue.Shipped.Find("exo.mailbox-permission-observations");
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,scenario);
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var result=await runner.RunAsync(entry,new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),new Dictionary<string,string?>
        { ["MailboxGuids"]=TestData.Office,["FullAccess"]="true",["SendAs"]=scenario=="permission-all"?"true":"false",["SendOnBehalf"]=scenario=="permission-all"?"true":"false" },runtime,null,CancellationToken.None);
        var log=File.Exists(environment["BDIT_FAKE_LOG"])?File.ReadAllText(environment["BDIT_FAKE_LOG"]):"";
        if(scenario.StartsWith("wrong-",StringComparison.Ordinal)){Assert.Null(result.Evidence);Assert.DoesNotContain("Read ",log);return;}
        Assert.NotNull(result.Evidence);var evidence=result.Evidence;Assert.Equal(2,evidence.ReportSchemaVersion);
        var target=Assert.Single(evidence.Sections[0].Rows);Assert.Equal(3,target["permissionReads"]!.AsArray().Count);
        var full=target["permissionReads"]![0]!;
        if(scenario=="permission-failed")
        {Assert.Equal("Failed",full["status"]!.GetValue<string>());Assert.Null(full["returnedEntries"]);Assert.Empty(evidence.Sections[1].Rows);}
        else if(scenario=="permission-empty")
        {Assert.Equal(0,full["returnedEntries"]!.GetValue<int>());Assert.Empty(evidence.Sections[1].Rows);Assert.Equal("Collected",evidence.Status);}
        else
        {
            var edge=Assert.Single(evidence.Sections[1].Rows,row=>row["permissionKind"]!.GetValue<string>()=="FullAccess");
            Assert.Equal(scenario=="permission-unresolved"?"UnresolvedDisplayLabel":"RawIdentity",edge["principal"]!["namespace"]!.GetValue<string>());
            Assert.Null(edge["principal"]!["externalDirectoryObjectId"]);
            Assert.Equal(scenario=="permission-missing-flag"?"Missing":"Value",edge["deny"]!["state"]!.GetValue<string>());
            if(scenario=="permission-missing-flag")Assert.Null(edge["deny"]!["value"]);else Assert.False(edge["deny"]!["value"]!.GetValue<bool>());
            Assert.Equal(scenario=="permission-null-rights"?"ReturnedNull":"Value",edge["rights"]!["state"]!.GetValue<string>());
            if(scenario=="permission-all")
            {
                Assert.Equal(3,evidence.Sections[1].Rows.Count);
                Assert.All(target["permissionReads"]!.AsArray(),read=>Assert.True(read!["selected"]!.GetValue<bool>()));
                var behalf=Assert.Single(evidence.Sections[1].Rows,row=>row["permissionKind"]!.GetValue<string>()=="SendOnBehalf");
                Assert.Equal("Unsupported",behalf["deny"]!["state"]!.GetValue<string>());Assert.Null(behalf["deny"]!["value"]);
                Assert.Equal("Unsupported",behalf["rights"]!["state"]!.GetValue<string>());Assert.Null(behalf["rights"]!["value"]);
            }
            if(scenario!="permission-values")Assert.NotEqual("Collected",evidence.Status);
        }
        Assert.Contains("Read Get-EXOMailboxPermission "+TestData.Office,log);
        if(scenario!="permission-all")
        {Assert.DoesNotContain("Read Get-EXORecipientPermission",log);Assert.False(target["permissionReads"]![1]!["selected"]!.GetValue<bool>());}
        Assert.DoesNotContain("Set-",log);Assert.DoesNotContain("Add-",log);Assert.DoesNotContain("Remove-",log);
    }
}
