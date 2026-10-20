using BuildStandard.Core;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class CalendarPermissionAdapterTests
{
    public static IEnumerable<object[]> Cases()=>ExchangeReportRunnerTests.Runtimes().Cast<object[]>().SelectMany(runtime=>
        new[]{"calendar-values","calendar-ambiguous","calendar-missing","calendar-failed","calendar-missing-rights","calendar-duplicate","wrong-account","wrong-tenant"}.Select(scenario=>new object[]{runtime[0],scenario}));
    [Theory][MemberData(nameof(Cases))]
    public async Task Pinned_calendar_reads_only_the_verified_unique_localised_folder_and_keeps_unknowns(string runtime,string scenario)
    {
        using var root=new TempRoot();var entry=ScriptCatalogue.Shipped.Find("exo.calendar-permission-observations");
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,scenario);
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var result=await runner.RunAsync(entry,new(TestData.TenantA,TestData.Operator,"operator@synthetic.example"),new Dictionary<string,string?>{["MailboxGuids"]=TestData.Office},runtime,null,CancellationToken.None);
        var log=File.Exists(environment["BDIT_FAKE_LOG"])?File.ReadAllText(environment["BDIT_FAKE_LOG"]):"";
        if(scenario.StartsWith("wrong-",StringComparison.Ordinal)){Assert.Null(result.Evidence);Assert.DoesNotContain("Read ",log);return;}
        Assert.True(result.Evidence is not null,"Outcome="+result.Outcome+"; "+result.Reason);var evidence=result.Evidence!;
        var target=Assert.Single(evidence.Sections[0].Rows);
        if(scenario is "calendar-ambiguous" or "calendar-missing")
        {
            Assert.Equal("Partial",target["folderReadStatus"]!.GetValue<string>());Assert.Null(target["nativeFolderIdentity"]);
            Assert.Equal("NotAttempted",target["permissionReadStatus"]!.GetValue<string>());
            Assert.Empty(evidence.Sections[1].Rows);Assert.DoesNotContain("Read Get-EXOMailboxFolderPermission",log);
        }
        else
        {
            Assert.Equal(TestData.Office+":\\Kalender",target["nativeFolderIdentity"]!.GetValue<string>());
            Assert.Contains("Read Get-EXOMailboxFolderPermission "+TestData.Office+":\\Kalender",log);
            if(scenario is "calendar-failed" or "calendar-duplicate")
            {Assert.Equal("Failed",target["permissionReadStatus"]!.GetValue<string>());Assert.Null(target["returnedEntries"]);Assert.Empty(evidence.Sections[1].Rows);}
            else
            {
                var edge=Assert.Single(evidence.Sections[1].Rows);Assert.Equal("SpecialPrincipal",edge["principal"]!["namespace"]!.GetValue<string>());
                Assert.Equal("Default",edge["principal"]!["value"]!.GetValue<string>());Assert.Null(edge["principal"]!["externalDirectoryObjectId"]);
                if(scenario=="calendar-missing-rights") {Assert.Equal("Missing",edge["rights"]!["state"]!.GetValue<string>());Assert.Null(edge["rights"]!["value"]);}
                else Assert.Equal("AvailabilityOnly",Assert.Single(edge["rights"]!["value"]!.AsArray())!.GetValue<string>());
                Assert.Empty(edge["sharingPermissionFlags"]!["value"]!.AsArray());
            }
        }
        Assert.Equal(scenario=="calendar-values"?"Collected":"Partial",evidence.Status);
        Assert.DoesNotContain("Set-",log);Assert.DoesNotContain("Add-",log);Assert.DoesNotContain("Remove-",log);
    }
}
