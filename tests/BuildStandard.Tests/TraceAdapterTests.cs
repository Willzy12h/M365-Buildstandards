using BuildStandard.Core;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class TraceAdapterTests
{
    public static IEnumerable<object[]> Cases()=>ExchangeReportRunnerTests.Runtimes().Cast<object[]>().SelectMany(runtime=>
        new[]{"trace-values","trace-empty","trace-general","trace-subject","trace-second-failed","wrong-account","wrong-tenant"}.Select(scenario=>new object[]{runtime[0],scenario}));
    [Theory][MemberData(nameof(ExchangeReportRunnerTests.Runtimes),MemberType=typeof(ExchangeReportRunnerTests))]
    public async Task Stop_in_second_window_retains_first_and_marks_third_unattempted(string runtime)
    {
        using var root=new TempRoot();using var stop=new CancellationTokenSource();var now=DateTimeOffset.UtcNow;
        var entry=ScriptCatalogue.Shipped.Find("exo.message-trace-observations");var actor=new ReadRunContext(TestData.TenantA,TestData.Operator,"operator@synthetic.example");
        var inputs=new Dictionary<string,string?>{["StartDate"]=now.AddDays(-22).ToString("yyyy-MM-dd"),["EndDate"]=now.AddDays(-1).ToString("yyyy-MM-dd"),["MessageTraceId"]=TestData.Office,["MaximumRows"]="100"};
        var preparation=TraceRunPreparation.Prepare(entry,actor,ScriptReadRequest.Bind(entry,actor,inputs,now),runtime,now);
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,"trace-stop");var reads=0;
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var progress=new InlineProgress(text=>{if(text.StartsWith("Reading Get-MessageTraceV2",StringComparison.Ordinal)&&++reads==2)stop.Cancel();});
        var result=await runner.RunAsync(entry,actor with{TracePreparation=preparation},inputs,runtime,progress,stop.Token);
        Assert.Equal(ReadRunOutcome.Cancelled,result.Outcome);Assert.NotNull(result.Evidence);Assert.Equal("Cancelled",result.Evidence.Status);
        Assert.Equal(new[]{"Collected","Failed","NotAttempted"},result.Evidence.Sections[0].Rows.Select(row=>row["readStatus"]!.GetValue<string>()));
        Assert.Single(result.Evidence.Sections[1].Rows);Assert.Null(result.Evidence.Sections[0].Rows[1]["returnedEntries"]);Assert.Null(result.Evidence.Sections[0].Rows[2]["requestedResultSize"]);
        Assert.Equal(2,reads);
    }
    private sealed class InlineProgress(Action<string> report):IProgress<string>{public void Report(string text)=>report(text);}
    [Theory][MemberData(nameof(Cases))]
    public async Task Exact_frozen_trace_runs_preserve_scope_subject_choice_and_failed_windows(string runtime,string scenario)
    {
        using var root=new TempRoot();var now=DateTimeOffset.UtcNow;var entry=ScriptCatalogue.Shipped.Find("exo.message-trace-observations");
        var actor=new ReadRunContext(TestData.TenantA,TestData.Operator,"operator@synthetic.example");
        var inputs=new Dictionary<string,string?>{["StartDate"]=now.AddDays(-12).ToString("yyyy-MM-dd"),["EndDate"]=now.AddDays(-1).ToString("yyyy-MM-dd"),
            ["MessageTraceId"]=scenario=="trace-general"?"":TestData.Office,["Sender"]=scenario=="trace-general"?"sender@synthetic.example":"",["IncludeSubject"]=scenario=="trace-subject"?"true":"false",["MaximumRows"]="100"};
        var preparation=TraceRunPreparation.Prepare(entry,actor,ScriptReadRequest.Bind(entry,actor,inputs,now),runtime,now);
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,scenario);
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>ExchangeReportRunnerTests.RuntimePath(runtime),environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        var result=await runner.RunAsync(entry,actor with{TracePreparation=preparation},inputs,runtime,null,CancellationToken.None);
        var log=File.Exists(environment["BDIT_FAKE_LOG"])?File.ReadAllText(environment["BDIT_FAKE_LOG"]):"";
        if(scenario.StartsWith("wrong-",StringComparison.Ordinal)){Assert.Null(result.Evidence);Assert.DoesNotContain("Read ",log);return;}
        Assert.True(result.Evidence is not null,"Outcome="+result.Outcome+"; "+result.Reason);var report=result.Evidence!;
        Assert.Equal(2,report.Sections[0].Rows.Count);Assert.All(report.Sections[0].Rows,row=>Assert.Equal(preparation.RequestContext()["frozenAtUtc"]!.GetValue<string>(),row["frozenAtUtc"]!.GetValue<string>()));
        Assert.Contains("Read Get-MessageTraceV2",log);Assert.DoesNotContain("Set-",log);
        if(scenario=="trace-empty"){Assert.Empty(report.Sections[1].Rows);Assert.Equal("Collected",report.Status);Assert.All(report.Sections[0].Rows,row=>Assert.Equal(0,row["returnedEntries"]!.GetValue<int>()));}
        else
        {
            Assert.Equal(scenario=="trace-second-failed"?1:2,report.Sections[1].Rows.Count);
            Assert.All(report.Sections[1].Rows,row=>{Assert.Equal(scenario=="trace-subject"?"Value":"NotAttempted",row["subject"]!["state"]!.GetValue<string>());if(scenario!="trace-subject")Assert.Null(row["subject"]!["value"]);});
            if(scenario!="trace-subject")Assert.DoesNotContain("synthetic subject",BuildStandard.Core.Json.ToolkitJson.Serialize(report));
            Assert.Equal(scenario is "trace-general" or "trace-second-failed"?"Partial":"Collected",report.Status);
            if(scenario=="trace-second-failed"){Assert.Equal("Failed",report.Sections[0].Rows[1]["readStatus"]!.GetValue<string>());Assert.Null(report.Sections[0].Rows[1]["returnedEntries"]);}
        }
    }
}
