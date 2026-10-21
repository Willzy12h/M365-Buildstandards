using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
namespace BuildStandard.App.Services;

/// <summary>Native orchestration only. The shared engine owns provenance, collection, interpretation and original recovery.</summary>
public sealed class DeploymentCoverageController
{
    private readonly Workspace _workspace;
    private readonly GraphReportsController _context;
    private readonly DeploymentCoverageWorkflow _workflow;
    public DeploymentCoverageController(Workspace workspace)
    {_workspace=workspace;_context=new(workspace);_workflow=new(workspace.Evidence.SaveDeploymentCoverage,()=>_context.ContextKey);}
    public string ContextKey=>_context.ContextKey;
    public bool HasPending=>_workflow.HasPendingOriginal;
    public DeploymentCoverageCapture? PendingCapture=>_workflow.PendingCapture;
    public DeploymentCoverageEvidence? PendingEvidence=>_workflow.Pending;
    public string PendingId=>PendingCapture?.Id??PendingEvidence?.Metadata.Id??"";
    public string ReadBlocker
    {
        get
        {
            if(HasPending)return "Save, export or explicitly discard the exact retained original before another read.";
            var connection=_context.ReadBlocker;if(connection.Length>0)return connection;
            if(_workspace.Standard is null)return "Select a verified standard in Settings before checking a control.";
            if(_workspace.Snapshot is null)return "Capture or open the original tenant configuration in Configuration first. Local creation/readback evidence must establish the selected object's toolkit ownership before any read.";
            if(_workspace.Snapshot.StandardRelease!=_workspace.Standard.Release)return "Select the original catalogue release matching this capture, or collect a new configuration. Historical mappings are never silently rebound.";
            return "";
        }
    }
    public async Task<DeploymentCoverageWorkflow.CaptureOutcome> ReadAsync(string controlId,string[] users,string[] devices,bool acknowledged,bool modernAcknowledged,string expectedContext,Func<bool> selectionCurrent)
    {
        _context.RequireContext(expectedContext);if(ReadBlocker.Length>0)throw new ToolkitException(ReadBlocker);
        if(!acknowledged)throw new SafetyViolationException("Acknowledge this experimental source read first. It grants no change approval or consent.");
        var connection=_workspace.RequireConnection();var profile=_workspace.Profile!;var standard=_workspace.RequireStandard();var snapshot=_workspace.Snapshot!;
        var control=standard.Controls.SingleOrDefault(c=>c.Id==controlId)??throw new ConfigurationException("Select one exact registered standard control.");
        if(modernAcknowledged&&control.Collection is ("apps" or "configuration")&&connection.Session.Mode!=SessionMode.Deployment)
            throw new SafetyViolationException("Modern Intune results require deliberately selected deployment access and the existing broader report scope. This page never changes permission mode or requests consent.");
        DeploymentCoverageWorkflow.CaptureOutcome? result=null;
        bool Current()
        {try{_context.RequireContext(expectedContext);return ReferenceEquals(connection,_workspace.Connection)&&selectionCurrent();}catch(ToolkitException){return false;}}
        await _workspace.RunExclusiveAsync("Checking selected-control coverage",async progress=>
        {
            if(!Current())throw new TenantMismatchException("The control or connection changed before preparation.");
            progress.Report("Checking exact local mapping and original creation/readback evidence. Names alone do not establish ownership; no tenant call yet.");
            using var preparation=CancellationTokenSource.CreateLinkedTokenSource(_workspace.OperationToken);preparation.CancelAfter(TimeSpan.FromSeconds(30));
            var task=Task.Run(()=>
            {
                preparation.Token.ThrowIfCancellationRequested();
                var mappings=_workspace.Evidence.LoadMappings(profile.TenantId);
                var runs=_workspace.Evidence.LoadRuns(profile.TenantId);
                preparation.Token.ThrowIfCancellationRequested();
                return DeploymentCoverageSelection.Prepare(standard,snapshot,mappings,runs,connection.Session,controlId,users,devices);
            });
            DeploymentCoverageSelection scope;
            try{scope=await task.WaitAsync(preparation.Token);}
            catch
            {
                _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                if(preparation.IsCancellationRequested&&!_workspace.OperationToken.IsCancellationRequested)throw new ToolkitException("Local ownership preparation did not finish within 30 seconds. No tenant read was started. Review local evidence/storage and try deliberately; missing or unreadable proof is not ownership.");
                throw;
            }
            if(!Current())throw new TenantMismatchException("The selected client/evidence/control changed; no tenant read was started.");
            progress.Report("Reading only this control's registered sources, with explicit partial/unknown states and a five-minute, 100-request bound. No configuration or assignment is changed.");
            if(connection.Graph is not GraphClient graph)throw new ToolkitException("Use the existing verified Graph connection; no alternate transport is available.");
            result=await _workflow.ReadCaptureAsync(token=>new DeploymentCoverageService().CaptureAsync(scope,connection.Session,graph,Current,token,modernAcknowledged),expectedContext,profile.TenantId,acknowledged,_workspace.OperationToken);
        });
        _context.RequireContext(expectedContext);
        if(!selectionCurrent())throw new TenantMismatchException("The selection changed. Any original remains saved or retained separately, not accepted as the new client's result.");
        return result??throw new ToolkitException("No current-client coverage result was accepted. Review any retained original; this is not an empty successful check.");
    }
    public async Task<DeploymentCoverageEvidence> ImportAsync(string file,string expectedContext)
    {
        _context.RequireContext(expectedContext);
        if(!_workspace.Idle||HasPending)throw new ToolkitException("Finish the current operation and resolve retained originals before opening another report.");
        var tenant=_workspace.Profile!.TenantId;DeploymentCoverageEvidence? evidence=null;
        await _workspace.RunExclusiveAsync("Opening historical coverage",async _=>evidence=await Task.Run(()=>DeploymentCoverageBundle.ReadFile(file,tenant)));
        _context.RequireContext(expectedContext);return evidence??throw new ToolkitException("No historical coverage report was admitted.");
    }
    public async Task<string> ExportAsync(DeploymentCoverageEvidence evidence,ExportFormat format,string expectedContext)
    {
        _context.RequireContext(expectedContext);
        if(evidence.Metadata.TenantId!=_workspace.Profile!.TenantId)throw new TenantMismatchException("The report belongs to another original client.");
        var exporter=_workspace.Exporter;
        var file=await _workspace.ExportAsync(()=>{_context.RequireContext(expectedContext);return exporter.ExportDeploymentCoverage(evidence,format);},"Exporting original coverage observations","Original identity and unknown values retained; this is not a new tenant check.");
        _context.RequireContext(expectedContext);return file;
    }
    public async Task<string> RecoverAsync(string expectedId,bool acknowledged,string action)
    {
        var exporter=_workspace.Exporter;var file="";
        await _workspace.RunExclusiveAsync("Recovering the exact original coverage",async _=>file=await Task.Run(()=>action switch
        {"save"=>_workflow.SavePending(expectedId,acknowledged),"json"=>_workflow.ExportOriginal(exporter,expectedId,acknowledged),"files"=>_workflow.ExportOriginalFiles(exporter,expectedId,acknowledged),_=>throw new ConfigurationException("Choose a registered original recovery action.")}));
        return file;
    }
    public void Discard(string id,bool acknowledged)
    {if(!_workspace.Idle)throw new ToolkitException("Wait for the current operation to finish before discard.");_workflow.DiscardPending(id,acknowledged);}
}
