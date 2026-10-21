using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;

namespace BuildStandard.App.Services;

/// <summary>Native orchestration over the shared typed reader, original recovery and existing connection.</summary>
public sealed class ConditionalAccessEvaluationController
{
    private readonly Workspace _workspace;
    private readonly GraphReportsController _reports;
    private readonly ConditionalAccessEvaluationWorkflow _workflow;
    public ConditionalAccessEvaluationController(Workspace workspace)
    {
        _workspace=workspace;_reports=new(workspace);
        _workflow=new(workspace.Evidence.SaveConditionalAccessEvaluation,()=>_reports.ContextKey);
    }
    public string ContextKey=>_reports.ContextKey;
    public ConditionalAccessEvaluationCapture? Pending=>_workflow.Pending;
    public string ReadBlocker
    {
        get
        {
            if(Pending is not null)return "Save, export or explicitly discard the original unsaved report before another read.";
            var blocker=_reports.ReadBlocker;if(blocker.Length>0)return blocker;
            var scopes=_workspace.Session?.Scopes;
            if(scopes is null||new[]{"Policy.Read.All","User.Read.All"}.Any(required=>!scopes.Any(scope=>string.Equals(scope,required,StringComparison.OrdinalIgnoreCase)||string.Equals(scope,"https://graph.microsoft.com/"+required,StringComparison.OrdinalIgnoreCase))))
                return "The current connection needs existing Policy.Read.All and User.Read.All for evaluation and exact member lookup. Review Client and connection deliberately; this page never requests consent.";
            return "";
        }
    }
    public async Task<ConditionalAccessEvaluationWorkflow.Outcome> ReadAsync(ConditionalAccessScenario scenario,bool acknowledged,
        string expectedContext,Func<bool> selectionCurrent)
    {
        _reports.RequireContext(expectedContext);
        if(ReadBlocker.Length>0)throw new ToolkitException(ReadBlocker);
        if(!acknowledged)throw new SafetyViolationException("Acknowledge this experimental hypothetical read and its limitations first. This grants no consent or write access.");
        var prepared=ConditionalAccessScenarioCodec.Prepare(scenario);var connection=_workspace.RequireConnection();
        var tenant=_workspace.Profile!.TenantId;
        ConditionalAccessEvaluationWorkflow.Outcome? result=null;
        bool Current()
        {
            try{_reports.RequireContext(expectedContext);return ReferenceEquals(connection,_workspace.Connection)&&selectionCurrent();}
            catch(ToolkitException){return false;}
        }
        await _workspace.RunExclusiveAsync("Evaluating a hypothetical sign-in",async progress=>
        {
            if(!Current())throw new TenantMismatchException("The client or reviewed scenario changed before the read.");
            progress.Report("Fresh exact member lookup, then one fixed hypothetical evaluation. At most 120 seconds; no tenant changes or interactive sign-in.");
            result=await _workflow.ReadAsync(async token=>
            {
                if(connection.Graph is not GraphClient graph)throw new ToolkitException("Use the existing verified Graph connection; no alternate transport is available.");
                using var evaluator=graph.ForConditionalAccessEvaluation(connection.Session,Current);
                return await new ConditionalAccessEvaluationService().CaptureAsync(connection.ForReports(),evaluator,connection.Session,prepared.Scenario,Current,token);
            },expectedContext,tenant,acknowledged,_workspace.OperationToken);
        });
        _reports.RequireContext(expectedContext);
        if(!selectionCurrent())throw new TenantMismatchException("The scenario changed. No result was attributed to the new selection; any saved original remains separate.");
        return result??throw new ToolkitException("Stopped before a current result was accepted. Review any original retained report; no successful evaluation is claimed.");
    }
    public async Task<ConditionalAccessEvaluationCapture> ImportAsync(string file,string context)
    {
        _reports.RequireContext(context);
        if(!_workspace.Idle||Pending is not null)throw new ToolkitException("Finish the current operation and resolve the original unsaved report before opening supplied evidence.");
        var tenant=_workspace.Profile!.TenantId;ConditionalAccessEvaluationCapture? capture=null;
        await _workspace.RunExclusiveAsync("Opening historical evaluation evidence",async _=>
            capture=await Task.Run(()=>ConditionalAccessEvaluationBundle.ReadFile(file,tenant)));
        _reports.RequireContext(context);
        return capture??throw new ToolkitException("No supplied evaluation report was admitted.");
    }
    public async Task<string> ExportAsync(ConditionalAccessEvaluationCapture capture,ExportFormat format,string context)
    {
        _reports.RequireContext(context);
        if(capture.Metadata.TenantId!=_workspace.Profile!.TenantId)throw new TenantMismatchException("This report belongs to another client.");
        var exporter=_workspace.Exporter;
        var file=await _workspace.ExportAsync(()=>
        {
            _reports.RequireContext(context);return exporter.ExportConditionalAccessEvaluation(capture,format);
        },"Exporting the original hypothetical evaluation","Preserving unknowns and the original identity; this is not a new check.");
        _reports.RequireContext(context);return file;
    }
    public async Task<string> RecoverAsync(string originalId,bool acknowledged,bool export)
    {
        var exporter=_workspace.Exporter;var file="";
        await _workspace.RunExclusiveAsync(export?"Exporting retained original":"Saving retained original",async _=>
            file=await Task.Run(()=>export?_workflow.ExportOriginal(exporter,originalId,acknowledged):_workflow.SavePending(originalId,acknowledged)));
        return file;
    }
    public void Discard(string originalId,bool acknowledged)
    {
        if(!_workspace.Idle)throw new ToolkitException("Wait for the current operation before discarding a retained original.");
        _workflow.DiscardPending(originalId,acknowledged);
    }
}
