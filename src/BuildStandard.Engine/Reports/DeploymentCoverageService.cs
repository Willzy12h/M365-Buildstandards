using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Core.Safety;
using BuildStandard.Graph;
namespace BuildStandard.Engine.Reports;

public sealed record CoverageCapturedSource(string Key,string Path,string Scope,string StartedAt,string CompletedAt,CoverageSourceCapture Capture)
{
    public IntuneExportObservation? ModernExport {get;init;}
}
public sealed record CoverageCapturedGroup(string GroupObjectId,CoverageMembershipResult Membership);
public sealed record DeploymentCoverageCapture(DeploymentCoverageSelection Selection,IReadOnlyList<CoverageCapturedSource> Sources,IReadOnlyList<CoverageAssignmentObservation> Assignments,IReadOnlyList<CoverageCapturedGroup> Groups,IReadOnlyList<ComplianceCoverageObservation> ComplianceObservations,string CandidateState,bool IsCurrentAtCompletion,bool SupportedReadsComplete,string Limitations);

/// <summary>One read-only selected-control journey, preserving original source states without creating deployment evidence.</summary>
public sealed class DeploymentCoverageService
{
    private readonly TimeProvider _clock;
    public DeploymentCoverageService(TimeProvider? clock=null){_clock=clock??TimeProvider.System;}
    public async Task<DeploymentCoverageCapture> CaptureAsync(DeploymentCoverageSelection selection,TenantSession session,GraphClient graph,Func<bool> current,CancellationToken ct,bool broaderTokenAcknowledged=false)
    {
        if(!string.Equals(graph.TenantId,selection.TenantId,StringComparison.OrdinalIgnoreCase)||graph.Mode!=selection.PermissionMode)
            throw new TenantMismatchException("The Graph context does not match the frozen coverage scope.");
        var modern=selection.Family is CoverageFamily.Application or CoverageFamily.ClassicConfiguration;
        if(modern&&broaderTokenAcknowledged)RequireModernAccess(selection,session);
        using var reader=graph.ForDeploymentCoverage(session,()=>Matches(selection,session)&&current());
        return await CaptureAsync(selection,session,reader.ReadAsync,current,ct,modern&&broaderTokenAcknowledged?ReadModern:null).ConfigureAwait(false);
        async Task<IntuneExportReceipt> ReadModern(CancellationToken token)
        {
            using var export=graph.ForIntuneExports(session,()=>Matches(selection,session)&&current(),true,100-reader.RequestsSent);
            return await export.ReadAsync(IntuneExportRequest.Prepare(selection.Family,selection.ObjectId),token).ConfigureAwait(false);
        }
    }
    internal async Task<DeploymentCoverageCapture> CaptureAsync(DeploymentCoverageSelection selection,TenantSession session,Func<PreparedCoverageRead,CancellationToken,Task<CoverageReadResponse>> read,Func<bool> current,CancellationToken ct,Func<CancellationToken,Task<IntuneExportReceipt>>? readModern=null)
    {
        ArgumentNullException.ThrowIfNull(selection);ArgumentNullException.ThrowIfNull(session);ArgumentNullException.ThrowIfNull(read);ArgumentNullException.ThrowIfNull(current);
        if(!Matches(selection,session)||!current())throw new TenantMismatchException("The connection changed after selecting the exact coverage scope. Prepare a fresh report scope; no read was started.");
        if(readModern is not null&&selection.Family is (CoverageFamily.Application or CoverageFamily.ClassicConfiguration))RequireModernAccess(selection,session);
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(5),_clock);
        using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct,deadline.Token);
        var sources=new List<CoverageCapturedSource>();var assignments=new List<CoverageAssignmentObservation>();
        var groups=new List<CoverageCapturedGroup>();var compliance=new List<ComplianceCoverageObservation>();var warnings=new List<string>();
        var collector=new CoverageSourceCollector(_clock);var candidate="Candidate state unknown";var pages=0;var rows=0;var bytes=0;
        var objectRequest=DeploymentCoverageReads.Prepare(selection.Family,selection.ObjectId,CoverageReadKind.Object);
        var obj=await Read("object",objectRequest).ConfigureAwait(false);
        if(obj.State!=CoverageSourceState.Complete||!Current()||operation.IsCancellationRequested)return Finish();
        var observedObject=obj.Rows.Single();
        if(selection.Family==CoverageFamily.ConditionalAccess)
        {
            var policy=observedObject;var state=Text(policy,"state");
            candidate=ConditionalAccessMaterialState.HasPolicyDefinition(policy)?state switch
            {"disabled"=>"Observed disabled CA policy","enabled"=>"Observed enabled CA policy","enabledForReportingButNotEnforced"=>"Observed report-only CA policy",_=>"CA policy state unknown"}:"CA policy definition incomplete";
            Unavailable("service-results",CoverageSourceState.Unsupported,"Actual sign-in/effective protection is not established by policy configuration. Use the separate hypothetical CA evaluation or the documented manual sign-in acceptance.");
            return Finish();
        }
        var assignmentSource=await Read("assignments",DeploymentCoverageReads.Prepare(selection.Family,selection.ObjectId,CoverageReadKind.Assignments)).ConfigureAwait(false);
        if(!Current()||operation.IsCancellationRequested)return Finish();
        try
        {
            foreach(var row in assignmentSource.Rows)assignments.Add(CoverageAssignmentObservation.Read(row));
            candidate=assignmentSource.State==CoverageSourceState.Complete?(assignments.Count==0?"Observed unassigned":"Observed assignment configuration; effectiveness unknown"):"Assignment state unknown";
            if(assignmentSource.State==CoverageSourceState.Complete&&observedObject["isAssigned"] is JsonValue flag&&flag.TryGetValue<bool>(out var assigned)&&assigned!=(assignments.Count>0))
            {
                candidate="Assignment state unknown";
                warnings.Add("The object's assigned flag and separately read assignment list conflict. Review their intervals/current portal state; no single assignment conclusion is inferred.");
            }
        }
        catch(ConfigurationException){warnings.Add("An assignment target was malformed; assignment/population completeness is unknown. Original source remains available.");candidate="Assignment state unknown";}
        if(warnings.Count==0)
        {
            var selectedGroups=assignments.Where(a=>a.CanObserveGroup).Select(a=>a.GroupObjectId!).Distinct(StringComparer.Ordinal).ToArray();
            if(selectedGroups.Length>32)warnings.Add("The 32 assignment-group limit was reached; remaining populations were not read and coverage remains partial.");
            foreach(var id in selectedGroups.Take(32))
            {
                if(!Current()||operation.IsCancellationRequested)break;
                var metadata=await Read("group:"+id+":metadata",DeploymentCoverageReads.Group(id,false)).ConfigureAwait(false);
                if(!Current()||operation.IsCancellationRequested)break;
                CoverageGroupAdmission admission;
                try{admission=metadata.State==CoverageSourceState.Complete?CoverageGroupAdmission.Read(metadata.Rows.Single(),id):new(id,false,"Group metadata could not be established.");}
                catch(ConfigurationException){admission=new(id,false,"Group metadata was malformed; no member read is admitted.");warnings.Add("A group's metadata is incomplete or malformed; its population is unknown.");}
                CoverageSourceCapture members;
                if(admission.CanReadDirectMembers)members=await Read("group:"+id+":members",DeploymentCoverageReads.Group(id,true)).ConfigureAwait(false);
                else members=Unavailable("group:"+id+":members",CoverageSourceState.NotAttempted,admission.Reason);
                try{groups.Add(new(id,CoverageMembershipProjection.Project(admission,members,selection.PilotUserObjectIds,selection.PilotDirectoryDeviceObjectIds)));}
                catch(ConfigurationException)
                {
                    warnings.Add("A direct-member row was malformed; its original source is retained and the supported member population remains unknown.");
                    var unknown=new CoverageSourceCapture(CoverageSourceState.Failed,[],[],"Member interpretation failed; this is not an empty successful population.");
                    groups.Add(new(id,CoverageMembershipProjection.Project(new(id,false,"Member interpretation failed."),unknown,selection.PilotUserObjectIds,selection.PilotDirectoryDeviceObjectIds)));
                }
            }
        }
        if(!Current()||operation.IsCancellationRequested)return Finish();
        if(selection.Family==CoverageFamily.ClassicCompliance)
        {
            var resultSource=await Read("service-results",DeploymentCoverageReads.Prepare(selection.Family,selection.ObjectId,CoverageReadKind.ServiceResults)).ConfigureAwait(false);
            foreach(var row in resultSource.Rows)
            {
                try{compliance.Add(ComplianceCoverageObservation.Read(row,_clock.GetUtcNow()));}
                catch(ConfigurationException){warnings.Add("A service-result row was malformed; no verification is inferred. Its original page remains available.");}
            }
        }
        else if(readModern is not null)await CaptureModern().ConfigureAwait(false);
        else Unavailable("service-results",CoverageSourceState.NotAttempted,"Modern service results were not requested. Deliberately select existing deployment access and acknowledge its broader token for reporting before this fixed export; there is no automatic mode/consent change. Alternatively review the selected app/policy result in Intune manually. Assignment is not installation or configuration success.");
        return Finish();

        async Task CaptureModern()
        {
            if(pages>=100||rows>=5000||bytes>=CoverageResponseCodec.MaximumBytes)
            {Unavailable("service-results",CoverageSourceState.NotAttempted,"The capture-wide source limit was reached; no modern report job was requested.");return;}
            var request=IntuneExportRequest.Prepare(selection.Family,selection.ObjectId);
            var started=Timestamps.Format(_clock.GetUtcNow());var completed=started;IntuneExportObservation? original=null;
            CoverageSourceState state;string detail;
            try
            {
                var task=readModern!(operation.Token);IntuneExportReceipt receipt;
                try{receipt=await task.WaitAsync(operation.Token).ConfigureAwait(false);}
                catch
                {
                    _=task.ContinueWith(static t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);throw;
                }
                original=IntuneExportObservation.Create(selection,receipt);started=original.StartedAt;completed=original.CompletedAt;
                state=!Current()?CoverageSourceState.ContextChanged:operation.IsCancellationRequested?(ct.IsCancellationRequested?CoverageSourceState.Stopped:CoverageSourceState.Partial):CoverageSourceState.Complete;
                detail=state==CoverageSourceState.Complete?"Original fixed modern CSV report was downloaded and admitted; not effective deployment.":"Original modern source retained separately after stop, deadline or context change; no current-client completion is claimed.";
                var archiveBytes=original.Archive.OriginalArchiveBytes.Length;
                if(original.Archive.Table.Rows.Count>5000-rows||archiveBytes>CoverageResponseCodec.MaximumBytes-bytes||pages>=100)
                {state=CoverageSourceState.Partial;detail="NOT SAVED: combined original sources exceed the report row/byte/page limits. Original material is retained separately; no smaller complete report or raised limit is inferred.";warnings.Add(detail);}
                detail+=" "+original.Archive.Table.Limitations;
            }
            catch(OperationCanceledException)
            {
                state=!Current()?CoverageSourceState.ContextChanged:ct.IsCancellationRequested?CoverageSourceState.Stopped:CoverageSourceState.Failed;
                detail="Modern report stopped or reached the original capture deadline. Earlier admitted JSON originals remain available; a report job may still exist and is never automatically replayed. No successful empty source is claimed.";
                completed=Timestamps.Format(_clock.GetUtcNow());
            }
            catch(Exception ex)when(ex is ToolkitException or IOException or HttpRequestException)
            {
                state=Current()?CoverageSourceState.Failed:CoverageSourceState.ContextChanged;
                detail="Modern report could not be admitted. Earlier source observations remain available; there is no successful empty inventory, permission fallback or automatic report-job replay. No signed address, service body or token is included in this failure.";
                completed=Timestamps.Format(_clock.GetUtcNow());
            }
            sources.Add(new("service-results",request.Path,request.RequiredScope,started,completed,new(state,[],[],detail)){ModernExport=original});
        }
        bool Current()=>Matches(selection,session)&&current();
        async Task<CoverageSourceCapture> Read(string key,PreparedCoverageRead request)
        {
            var started=Timestamps.Format(_clock.GetUtcNow());CoverageSourceCapture capture;
            if(pages>=100||rows>=5000||bytes>=CoverageResponseCodec.MaximumBytes)
                capture=new(CoverageSourceState.NotAttempted,[],[],"The capture-wide page/row/byte limit was reached. No further source was read.");
            else if(!selection.Scopes.Any(s=>string.Equals(s,request.Scope,StringComparison.OrdinalIgnoreCase)||string.Equals(s,"https://graph.microsoft.com/"+request.Scope,StringComparison.OrdinalIgnoreCase)))
                capture=new(CoverageSourceState.Failed,[],[],"The frozen connection lacks this registered read scope. No consent, access-mode change or broader fallback was attempted.");
            else capture=await collector.CollectAsync(request,read,Current,operation.Token).ConfigureAwait(false);
            if(deadline.IsCancellationRequested&&!ct.IsCancellationRequested&&capture.State==CoverageSourceState.Stopped)
                capture=new(capture.OriginalPages.Count>0?CoverageSourceState.Partial:CoverageSourceState.Failed,capture.Rows,capture.OriginalPages,"The capture-wide deadline ended. Earlier admitted pages remain available; this was not an engineer stop.");
            var originals=capture.OriginalPages;var sourceBytes=originals.Sum(p=>p.Length);var sourceRows=capture.Rows.Count;
            if(originals.Count>100-pages||sourceRows>5000-rows||sourceBytes>CoverageResponseCodec.MaximumBytes-bytes)
                capture=new(CoverageSourceState.Partial,[],[],"NOT SAVED: this source exceeds the capture-wide evidence limits; earlier admitted sources remain available.");
            else{pages+=originals.Count;rows+=sourceRows;bytes+=sourceBytes;}
            sources.Add(new(key,request.Path,request.Scope,started,Timestamps.Format(_clock.GetUtcNow()),capture));return capture;
        }
        CoverageSourceCapture Unavailable(string key,CoverageSourceState state,string reason)
        {
            var capture=new CoverageSourceCapture(state,[],[],reason);var stamp=Timestamps.Format(_clock.GetUtcNow());sources.Add(new(key,"","",stamp,stamp,capture));return capture;
        }
        DeploymentCoverageCapture Finish()
        {
            if(selection.Family!=CoverageFamily.ConditionalAccess&&!sources.Any(s=>s.Key=="assignments"))
                Unavailable("assignments",CoverageSourceState.NotAttempted,"Assignments were not reached after the earlier source failure, stop or context change. This is not an empty assignment list.");
            if(!sources.Any(s=>s.Key=="service-results"))
                Unavailable("service-results",CoverageSourceState.NotAttempted,"Service results were not reached after the earlier source failure, stop or context change. No successful empty result is inferred.");
            var isCurrent=Current();var complete=isCurrent&&!operation.IsCancellationRequested&&warnings.Count==0&&sources.All(s=>s.Capture.State==CoverageSourceState.Complete);
            var limitations="Source observations, not effective targeting, installation, complete deployment before-evidence or permission to act. Pilot scope is not assignment approval; no unresolved write is cleared. "+string.Join(" ",warnings);
            if(!isCurrent)limitations+=" Client context changed; retain originals separately and do not admit this as current-client coverage.";
            if(operation.IsCancellationRequested)limitations+=" Collection stopped or reached its deadline; unrequested sources were not checked.";
            return new(selection,Array.AsReadOnly(sources.ToArray()),Array.AsReadOnly(assignments.ToArray()),Array.AsReadOnly(groups.ToArray()),Array.AsReadOnly(compliance.ToArray()),candidate,isCurrent,complete,limitations);
        }
    }
    private static void RequireModernAccess(DeploymentCoverageSelection selection,TenantSession session)
    {
        var request=IntuneExportRequest.Prepare(selection.Family,selection.ObjectId);
        if(selection.PermissionMode!=SessionMode.Deployment||session.Mode!=SessionMode.Deployment||!selection.Scopes.Any(s=>string.Equals(s,request.RequiredScope,StringComparison.OrdinalIgnoreCase)||string.Equals(s,"https://graph.microsoft.com/"+request.RequiredScope,StringComparison.OrdinalIgnoreCase)))
            throw new SafetyViolationException("Modern service reports require deliberately selected deployment access and the fixed existing broader reporting scope; no source is read, automatic upgrade or consent requested.");
    }
    private static bool Matches(DeploymentCoverageSelection selection,TenantSession session)=>session.TenantVerified&&session.OperatorVerified&&session.Mode==selection.PermissionMode
        &&string.Equals(session.TenantId,selection.TenantId,StringComparison.OrdinalIgnoreCase)&&string.Equals(session.AccountObjectId,selection.AccountObjectId,StringComparison.OrdinalIgnoreCase)
        &&string.Equals(session.OperatorObjectId,selection.AccountObjectId,StringComparison.OrdinalIgnoreCase)&&string.Equals(session.ClientId,selection.ClientId,StringComparison.OrdinalIgnoreCase)
        &&session.Scopes is not null&&new HashSet<string>(selection.Scopes,StringComparer.OrdinalIgnoreCase).SetEquals(session.Scopes);
    private static string? Text(JsonObject row,string key)=>row[key] is JsonValue value&&value.TryGetValue<string>(out var text)?text:null;
}
