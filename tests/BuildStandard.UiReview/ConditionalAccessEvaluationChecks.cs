using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;

internal static partial class Program
{
    private static void PrepareConditionalAccessImport(ShellViewModel shell)
    {
        var source=Encoding.UTF8.GetBytes("{\"value\":[{\"id\":\""+Id(9701)+"\",\"displayName\":\"Synthetic policy — applicability unknown\",\"state\":\"future\",\"policyApplies\":null}]}");
        var scenario=new ConditionalAccessScenario(Operator,Tenant,"windows","browser");
        var report=new ConditionalAccessEvaluationReport{Id=Id(9702),TenantId=Tenant,AccountObjectId=Operator,ClientId=Id(9703),
            StartedAt="2026-10-20T00:00:00.000Z",CompletedAt="2026-10-20T00:00:01.000Z",ToolkitVersion="synthetic-harness",Scopes=["Policy.Read.All"],Scenario=scenario,
            RequestSha256=ConditionalAccessScenarioCodec.Prepare(scenario).Sha256,ResponseSha256=CanonicalJson.Sha256Hex(source),ResponseBytes=source.Length,Status="Partial"};
        ConditionalAccessEvaluationSchema.Seal(report,source);
        var file=Path.Combine(shell.Workspace.Paths.ReportsDirectory,"synthetic-ca-evaluation-input.zip");
        using(var output=File.Create(file))using(var zip=new ZipArchive(output,ZipArchiveMode.Create))
        {
            Write("metadata.json",Encoding.UTF8.GetBytes(ToolkitJson.Serialize(report)));Write("response.json",source);Write("NOTICE.txt",Encoding.UTF8.GetBytes(ConditionalAccessEvaluationBundle.Notice));
            void Write(string name,byte[] bytes){using var entry=zip.CreateEntry(name).Open();entry.Write(bytes);}
        }
        var page=shell.Page<ConditionalAccessEvaluationViewModel>();page.Refresh();page.UserId=Operator;page.ApplicationId=Id(9704);page.SourceFile=file;
    }
    private static void SeedConditionalAccessPage(ShellViewModel shell)
    {
        PrepareConditionalAccessImport(shell);var page=shell.Page<ConditionalAccessEvaluationViewModel>();
        var task=page.ImportAsync();var watch=Stopwatch.StartNew();
        while(!task.IsCompleted){if(watch.Elapsed>TimeSpan.FromSeconds(20))throw new InvalidOperationException("Synthetic CA import did not finish.");Pump();}
        task.GetAwaiter().GetResult();
        if(page.Rows?.Count!=1||!page.Outcome.Contains("Historical",StringComparison.Ordinal)||!page.Rows[0]["Meaning"].ToString()!.Contains("Unknown",StringComparison.Ordinal))
            throw new InvalidOperationException("The native evaluation did not preserve historical/partial/unknown meaning.");
        if(!page.OriginalReportDetails.Contains("Scenario application ID: "+Tenant,StringComparison.Ordinal)||!page.RequestPreview.Contains(Id(9704),StringComparison.Ordinal))
            throw new InvalidOperationException("The original scenario A must be visible separately from editable scenario B.");
        if(page.ReadCommand.CanExecute(null)||page.StopCommand.CanExecute(null))throw new InvalidOperationException("Opening synthetic evidence enabled a live read or Stop.");
    }
}
