using BuildStandard.Core;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;
namespace BuildStandard.Tests;
[Collection("Exchange PowerShell fixtures")]
public sealed class TraceRunnerAdmissionTests
{
    [Theory][MemberData(nameof(ExchangeReportRunnerTests.Runtimes),MemberType=typeof(ExchangeReportRunnerTests))]
    public async Task Trace_preparation_cannot_be_attached_to_another_registered_family(string runtime)
    {
        using var root=new TempRoot();var actor=new ReadRunContext(TestData.TenantA,TestData.Operator,"operator@synthetic.example");
        var prepared=TraceRunPreparation.Prepare(TraceRunPreparationTests.Entry(),actor,TraceRunPreparationTests.Parameters(),runtime,new(2026,10,20,23,59,0,TimeSpan.Zero));
        var environment=ExchangeReportRunnerTests.FakeEnvironment(runtime,root.Root,"group-values");var runtimeProbes=0;
        var runner=new ExchangeReportRunner(new ReadRunnerHost(_=>{runtimeProbes++;return ExchangeReportRunnerTests.RuntimePath(runtime);},environment,null,Path.Combine(root.Root,"staging")),new SystemClock());
        await Assert.ThrowsAnyAsync<SafetyViolationException>(()=>runner.RunAsync(ScriptCatalogue.Shipped.Find("exo.distribution-group-observations"),actor with{TracePreparation=prepared},
            new Dictionary<string,string?>{["GroupGuids"]=TestData.Office},runtime,null,CancellationToken.None));
        Assert.Equal(0,runtimeProbes);Assert.False(Directory.Exists(Path.Combine(root.Root,"staging")));
    }
}
