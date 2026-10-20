using System.IO.Compression;
using System.Text;
using System.Reflection;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationPageTests
{
    internal static string Original(ConnectedReportsFixture f,string state="enabled")
    {
        var source=Encoding.UTF8.GetBytes("{\"value\":[{\"id\":\""+TestData.Office+"\",\"displayName\":\"Synthetic policy\",\"state\":\""+state+"\",\"policyApplies\":true,\"analysisReasons\":\"notSet\"}]}");
        var scenario=new ConditionalAccessScenario(TestData.Operator,TestData.TenantA,"windows","browser");
        var report=new ConditionalAccessEvaluationReport{Id=TestData.Emergency,TenantId=TestData.TenantA,AccountObjectId=TestData.Operator,ClientId=TestData.Emergency,
            StartedAt="2026-10-20T00:00:00.000Z",CompletedAt="2026-10-20T00:00:01.000Z",ToolkitVersion="synthetic",Scopes=["Policy.Read.All"],Scenario=scenario,
            RequestSha256=ConditionalAccessScenarioCodec.Prepare(scenario).Sha256,ResponseSha256=CanonicalJson.Sha256Hex(source),ResponseBytes=source.Length,Status=state=="enabled"?"Complete":"Partial"};
        ConditionalAccessEvaluationSchema.Seal(report,source);
        var file=Path.Combine(f.Root.Root,"synthetic-original.zip");
        using(var output=File.Create(file))using(var zip=new ZipArchive(output,ZipArchiveMode.Create))
        {
            Write("metadata.json",Encoding.UTF8.GetBytes(ToolkitJson.Serialize(report)));Write("response.json",source);Write("NOTICE.txt",Encoding.UTF8.GetBytes(ConditionalAccessEvaluationBundle.Notice));
            void Write(string name,byte[] bytes){using var entry=zip.CreateEntry(name).Open();entry.Write(bytes);}
        }
        return file;
    }
    [Fact] public void Navigation_and_typing_do_not_read_or_authenticate_and_macOS_is_not_offered()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Policy.Read.All");var shell=new ShellViewModel(f.Workspace);shell.Navigate("ca-evaluation");var page=shell.Page<ConditionalAccessEvaluationViewModel>();
        page.UserId=TestData.Operator;page.ApplicationId=TestData.TenantA;page.DeviceCompliant="False";
        Assert.DoesNotContain("macOS",page.Platforms);Assert.Contains("\"isCompliant\":false",page.RequestPreview);Assert.Contains("Omitted",page.RequestPreview);Assert.False(page.ReadCommand.CanExecute(null));
        Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);Assert.Contains("experimental",page.ReadGuidance,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void Changing_a_scenario_A_B_A_invalidates_the_read_acknowledgement()
    {
        using var f=new ConnectedReportsFixture();f.Workspace.Session!.Scopes.Add("Policy.Read.All");var page=new ConditionalAccessEvaluationViewModel(new(f.Workspace));page.UserId=TestData.Operator;page.ApplicationId=TestData.TenantA;page.Acknowledged=true;
        page.ApplicationId=TestData.TenantB;page.ApplicationId=TestData.TenantA;Assert.False(page.Acknowledged);Assert.False(page.ReadCommand.CanExecute(null));Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public async Task Supplied_original_opens_offline_and_exports_historical_meaning_without_a_read()
    {
        using var f=new ConnectedReportsFixture(connect:false);var page=new ConditionalAccessEvaluationViewModel(new(f.Workspace)){SourceFile=Original(f,"future")};
        await page.ImportAsync();Assert.Contains("Historical",page.Outcome);var rows=Assert.IsType<System.Data.DataView>(page.Rows);Assert.Single(rows.Cast<System.Data.DataRowView>());Assert.Contains("Unknown",rows[0]["Meaning"].ToString());
        await page.ExportAsync(ExportFormat.Html);Assert.True(File.Exists(page.LastExport));Assert.Contains("Historical",File.ReadAllText(page.LastExport));Assert.Equal(0,f.Handler.Calls);Assert.Equal(0,f.Tokens.Calls);
    }
    [Fact] public async Task Imported_results_show_original_scenario_A_separately_from_editable_scenario_B()
    {
        using var f=new ConnectedReportsFixture(connect:false);var page=new ConditionalAccessEvaluationViewModel(new(f.Workspace)){UserId=TestData.Operator,ApplicationId=TestData.TenantB,SourceFile=Original(f)};
        await page.ImportAsync();Assert.Equal(TestData.TenantB,page.ApplicationId);Assert.Contains(TestData.TenantB,page.RequestPreview);
        Assert.Contains("Scenario application ID: "+TestData.TenantA,page.OriginalReportDetails);Assert.DoesNotContain("Scenario application ID: "+TestData.TenantB,page.OriginalReportDetails);
        Assert.Contains("Engineer account ID: "+TestData.Operator,page.OriginalReportDetails);Assert.Contains("2026-10-20T00:00:00.000Z",page.OriginalReportDetails);Assert.Contains("not a real sign-in",page.OriginalReportDetails);
        Assert.False(page.Acknowledged);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public async Task A_failed_repeat_import_does_not_leave_the_previous_result_looking_current()
    {
        using var f=new ConnectedReportsFixture(connect:false);var file=Original(f);var page=new ConditionalAccessEvaluationViewModel(new(f.Workspace)){SourceFile=file};await page.ImportAsync();Assert.NotNull(page.Rows);
        File.WriteAllText(file,"Synthetic malformed replacement");await Assert.ThrowsAnyAsync<ToolkitException>(()=>page.ImportAsync());Assert.Null(page.Rows);Assert.False(page.ExportHtmlCommand.CanExecute(null));Assert.Contains("Could not",page.Outcome);
    }
    [Fact] public async Task A_context_change_invalidates_the_current_export_but_leaves_the_source_unchanged()
    {
        using var f=new ConnectedReportsFixture(connect:false);var file=Original(f);var bytes=File.ReadAllBytes(file);var page=new ConditionalAccessEvaluationViewModel(new(f.Workspace)){SourceFile=file};await page.ImportAsync();
        f.Workspace.Profile!.Notes="Changed context";page.Refresh();Assert.Null(page.Rows);Assert.False(page.ExportJsonCommand.CanExecute(null));Assert.Equal(bytes,File.ReadAllBytes(file));
    }
    [Fact] public async Task Pending_evaluation_close_guard_routes_to_original_recovery_until_explicit_discard()
    {
        using var f=new ConnectedReportsFixture(connect:false);var shell=new ShellViewModel(f.Workspace);var page=shell.Page<ConditionalAccessEvaluationViewModel>();
        var imported=ConditionalAccessEvaluationBundle.ReadFile(Original(f),TestData.TenantA);
        var constructor=typeof(ConditionalAccessEvaluationCapture).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,[typeof(ConditionalAccessEvaluationReport),typeof(byte[])],null)!;
        var original=(ConditionalAccessEvaluationCapture)constructor.Invoke([imported.Metadata,imported.OriginalResponseBytes]);
        var controller=typeof(ConditionalAccessEvaluationViewModel).GetField("_controller",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
        var flow=(ConditionalAccessEvaluationWorkflow)typeof(ConditionalAccessEvaluationController).GetField("_workflow",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(controller)!;
        var directory=Path.Combine(f.Root.Paths.TenantsDirectory,TestData.TenantA);Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"ca-evaluations"),"Synthetic blocked storage.");
        var context=new GraphReportsController(f.Workspace).ContextKey;
        await flow.ReadAsync(_=>Task.FromResult(original),context,TestData.TenantA,true,default);Assert.True(page.HasPending);shell.Navigate("home");
        var guard=typeof(BuildStandard.App.MainWindow).GetMethod("ShowPendingReport",BindingFlags.NonPublic|BindingFlags.Static)!;
        Assert.True((bool)guard.Invoke(null,[shell])!);Assert.Same(page,shell.CurrentPage);Assert.False(f.Workspace.ShutdownComplete);Assert.Contains("unsaved",shell.ErrorMessage,StringComparison.OrdinalIgnoreCase);
        flow.DiscardPending(original.Metadata.Id,true);Assert.False((bool)guard.Invoke(null,[shell])!);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);
    }
    [Fact] public void Coverage_navigation_exists_without_authentication_or_collection_on_selection()
    {using var f=new ConnectedReportsFixture(connect:false);var shell=new ShellViewModel(f.Workspace);shell.Navigate("coverage");Assert.Equal("Deployment coverage",Assert.IsAssignableFrom<PageViewModel>(shell.CurrentPage).Title);Assert.Equal(0,f.Tokens.Calls);Assert.Equal(0,f.Handler.Calls);}
}
