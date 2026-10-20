using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public sealed partial class DeploymentCoverageWorkflow
{
    public sealed record CaptureOutcome(DeploymentCoverageCapture Capture,DeploymentCoverageEvidence? Evidence,string File,string? NotSavedReason);
    private DeploymentCoverageCapture? _pendingCapture;
    public DeploymentCoverageCapture? PendingCapture {get{lock(_gate)return _pendingCapture;}}
    public bool HasPendingOriginal {get{lock(_gate)return _pending is not null||_pendingCapture is not null;}}
    /// <summary>Runs the internally bounded coverage producer and retains its original before late Stop/context admission.</summary>
    /// <param name="capture">The owned DeploymentCoverageService producer, not arbitrary noncooperative extension code.
    /// Its existing request/capture deadline remains authoritative; no second time allowance or retry is started here.</param>
    public async Task<CaptureOutcome> ReadCaptureAsync(Func<CancellationToken,Task<DeploymentCoverageCapture>> capture,string expectedContext,string expectedTenant,bool acknowledged,CancellationToken ct)
    {
        lock(_gate)
        {
            if(_running||_pending is not null||_pendingCapture is not null)throw new SafetyViolationException("Resolve the running read or original retained report before another read. No original is replaced or query replayed.");
            _running=true;
        }
        try
        {
            ct.ThrowIfCancellationRequested();RequireContext(expectedContext);
            if(!ReportValues.IsCanonicalGuid(expectedTenant)||!acknowledged)throw new SafetyViolationException("Select the exact client and acknowledge the experimental report read first; this grants no access or change approval.");
            // The owned DeploymentCoverageService already bounds every external wait with the original linked
            // cancellation/deadline and returns earlier originals on Stop. Do not race its partial-result handoff
            // against that same cancellation again: exclusivity remains held until the owned producer settles.
            var raw=await capture(ct).ConfigureAwait(false);
            if(raw.Selection.TenantId!=expectedTenant)throw new TenantMismatchException("The returned source belongs to a different original tenant; no current-client result or save was admitted.");
            foreach(var source in raw.Sources)
                if(source.ModernExport is { } modern)
                {modern.RequireScope(raw.Selection);if(modern.IsHistorical)throw new SafetyViolationException("Imported originals cannot become a newly connected read. Use the historical evidence path.");}
            lock(_gate)_pendingCapture=raw;
            DeploymentCoverageEvidence? original=null;string? problem=null;
            try{original=DeploymentCoverageEvidence.Create(raw);lock(_gate)_pending=original;}
            catch(ToolkitException ex){problem="NOT SAVED — original source material retained. The strict report could not be formed; export original files for manual review. No report limit is raised or original silently dropped. "+SensitiveDataScrubber.Scrub(ex.Message);}
            ct.ThrowIfCancellationRequested();RequireContext(expectedContext);
            if(original is null)return new(raw,null,"",problem);
            try
            {
                var file=save(original);
                if(string.IsNullOrWhiteSpace(file))throw new IntegrityException("No durable saved path was verified.");
                lock(_gate)ClearPending();RequireContext(expectedContext);return new(raw,original,file,null);
            }
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ToolkitException)
            {
                RequireContext(expectedContext);
                return new(raw,original,"","NOT SAVED — exact original retained. A disk copy may already exist; inspect the original record ID before acknowledged recovery. No read is repeated. "+SensitiveDataScrubber.Scrub(ex.Message));
            }
        }
        finally{lock(_gate)_running=false;}
    }
    public string ExportOriginalFiles(ReportExporter exporter,string expectedId,bool acknowledged)
    {
        lock(_gate)
        {
            RequirePendingIdentity(expectedId,acknowledged);
            var raw=_pendingCapture??throw new SafetyViolationException("No raw capture is retained; export the available original JSON report instead.");
            var folder=exporter.ExportCoverageOriginalFiles(raw);ClearPending();return folder;
        }
    }
}
