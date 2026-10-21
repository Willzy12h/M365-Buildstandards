using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Reporting;

namespace BuildStandard.Engine.Reports;

/// <summary>Separate unsaved-original handling; carries no authentication or execution authority.</summary>
public sealed partial class DeploymentCoverageWorkflow(Func<DeploymentCoverageEvidence,string> save,Func<string> context)
{
    public sealed record Outcome(DeploymentCoverageEvidence Capture,string File,string? NotSavedReason);
    private readonly object _gate=new();
    private bool _running;
    private DeploymentCoverageEvidence? _pending;
    public DeploymentCoverageEvidence? Pending {get{lock(_gate)return _pending;}}
    public async Task<Outcome> ReadAsync(Func<CancellationToken,Task<DeploymentCoverageEvidence>> capture,
        string expectedContext,string expectedTenant,bool acknowledged,CancellationToken ct)
    {
        lock(_gate)
        {
            if(_running||_pending is not null||_pendingCapture is not null)throw new SafetyViolationException("Finish the current read or save, export or explicitly discard its original unsaved report before another read.");
            _running=true;
        }
        try
        {
            ct.ThrowIfCancellationRequested();RequireContext(expectedContext);
            if(!ReportValues.IsCanonicalGuid(expectedTenant)||!acknowledged)
                throw new SafetyViolationException("Select the exact client and acknowledge the experimental selected-control coverage read first. This grants no consent or tenant-change approval.");
            var task=capture(ct);DeploymentCoverageEvidence original;
            try{original=await task.WaitAsync(ct).ConfigureAwait(false);}
            catch
            {
                _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                throw;
            }
            if(original.IsHistorical||original.Metadata.SourceMode!="live")
                throw new SafetyViolationException("Supplied or historical evidence cannot be admitted as a new connected read. Open it through the separate offline path.");
            if(original.Metadata.TenantId!=expectedTenant)
                throw new TenantMismatchException("The original report belongs to a different tenant; no current-client result was admitted or saved.");
            // Preserve an original before checking late presentation changes or attempting local I/O.
            lock(_gate)_pending=original;
            ct.ThrowIfCancellationRequested();RequireContext(expectedContext);
            string file;
            try
            {
                file=save(original);
                if(string.IsNullOrWhiteSpace(file))throw new IntegrityException("No durable saved path was returned.");
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ToolkitException)
            {
                RequireContext(expectedContext);
                return new(original,"","NOT SAVED — original report retained. A disk copy may already exist; inspect local evidence before an explicit save or original export. No read is repeated. "+SensitiveDataScrubber.Scrub(ex.Message));
            }
            lock(_gate)ClearPending();
            RequireContext(expectedContext);
            return new(original,file,null);
        }
        finally{lock(_gate)_running=false;}
    }
    public string SavePending(string expectedId,bool acknowledged)
    {
        lock(_gate)
        {
            var original=RequirePending(expectedId,acknowledged);var file=save(original);
            if(string.IsNullOrWhiteSpace(file))throw new IntegrityException("No saved path was verified; the original remains retained.");
            ClearPending();return file;
        }
    }
    public string ExportOriginal(ReportExporter exporter,string expectedId,bool acknowledged)
    {
        lock(_gate)
        {
            var original=RequirePending(expectedId,acknowledged);
            var file=exporter.ExportDeploymentCoverage(original,ExportFormat.Json);
            ClearPending();return file;
        }
    }
    public void DiscardPending(string expectedId,bool acknowledged)
    {lock(_gate){RequirePendingIdentity(expectedId,acknowledged);ClearPending();}}
    private DeploymentCoverageEvidence RequirePending(string expectedId,bool acknowledged)
    {
        RequirePendingIdentity(expectedId,acknowledged);
        return _pending??throw new SafetyViolationException("A conforming saved-report bundle is unavailable. Export the retained original files for manual review or explicitly discard; the strict report cap is unchanged.");
    }
    private void RequirePendingIdentity(string expectedId,bool acknowledged)
    {
        if(_running||!acknowledged||(_pendingCapture?.Id??_pending?.Metadata.Id)!=expectedId)
            throw new SafetyViolationException("Review and explicitly acknowledge this exact retained original before saving, copying or discarding it. This cannot repeat a tenant operation.");
    }
    private void ClearPending(){_pending=null;_pendingCapture=null;}
    private void RequireContext(string expected)
    {
        if(string.IsNullOrWhiteSpace(expected)||context()!=expected)
            throw new TenantMismatchException("The client, connection or selected control changed. No result or saved-file notification was admitted for the new selection; original evidence remains separate.");
    }
}
