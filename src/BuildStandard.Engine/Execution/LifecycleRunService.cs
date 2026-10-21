using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.Engine.Execution;

/// <summary>Local runtime approval only. Historical JSON, saved plans and imported reports cannot construct it.</summary>
public sealed class LifecycleApproval
{
    private int _used;
    internal LifecycleApproval(LifecyclePreview preview,JsonObject acknowledgement){Preview=preview;Acknowledgement=acknowledgement.DeepClone().AsObject();}
    internal LifecyclePreview Preview { get; }
    internal JsonObject Acknowledgement { get; }
    internal void Consume(){if(Interlocked.CompareExchange(ref _used,1,0)!=0)throw new SafetyViolationException("Lifecycle approval was already used. Do not replay it; prepare a new plan after reconciliation.");}
}

/// <summary>One action, one durable intent, one send, independent readback. Never batches, retries or resumes a write.</summary>
public sealed class LifecycleRunService(EvidenceStore evidence,IClock clock)
{
    public LifecycleApproval Approve(LifecyclePreview preview,TenantSession session,TenantProfile profile,StandardCatalogue standard,
        TenantSnapshot snapshot,string exactDigest,bool protectedAccountsReviewed,bool downstreamReviewed,bool preservationReviewed,Action assertCurrent)
    {
        var document=RequireCurrent(preview,session,profile,standard,snapshot,assertCurrent);
        if(exactDigest!=preview.IntegrityDigest || !protectedAccountsReviewed || !downstreamReviewed || preview.PreservationRequired && !preservationReviewed)
            throw new SafetyViolationException("Review the exact preview, protected accounts, downstream ownership and any required preservation/retention/business-owner consequences before approval.");
        if(File.Exists(IntentFile(preview)))throw new SafetyViolationException("This lifecycle plan already has an attempt. Reconcile it; approval cannot revive or replay a saved plan.");
        return new(preview,new JsonObject { ["planDigest"]=preview.IntegrityDigest,["accountObjectId"]=session.AccountObjectId,
            ["approvedAt"]=Timestamps.Format(clock.UtcNow),["protectedAccountsReviewed"]=true,["downstreamReviewed"]=true,
            ["preservationReviewed"]=preservationReviewed,["preservationRequired"]=document["preservationRequired"]!.DeepClone() });
    }

    public async Task<JsonObject> ExecuteAsync(IGraphClient graph,TenantSession session,TenantProfile profile,StandardCatalogue standard,
        TenantSnapshot snapshot,LifecycleApproval approval,Func<LifecycleOperation,ILifecycleReadSource> reads,Action assertCurrent,CancellationToken ct)
    {
        var preview=approval.Preview;var plan=RequireCurrent(preview,session,profile,standard,snapshot,assertCurrent);
        if(graph.Mode!=SessionMode.Deployment || !string.Equals(graph.TenantId,preview.TenantId,StringComparison.OrdinalIgnoreCase))throw new TenantMismatchException("Lifecycle writer is not the verified deployment connection for this client.");
        ct.ThrowIfCancellationRequested();approval.Consume();
        using var lease=evidence.AcquireTenantWriteLease(preview.TenantId);
        AssertOtherWritesResolved(preview.TenantId,standard);
        var operation=ToolkitJson.Deserialize<LifecycleOperation>(plan["operation"]!.ToJsonString());
        _=LifecycleActionRegistry.RequireRegistered(operation);
        var intent=Record("intent",preview.Id,preview.TenantId);
        intent["planDigest"]=preview.IntegrityDigest;intent["acknowledgement"]=approval.Acknowledgement.DeepClone();
        intent["startedAt"]=Timestamps.Format(clock.UtcNow);intent["operation"]=ToolkitJson.ToNode(operation);
        intent=evidence.SaveLifecycleIntent(intent); // must be durably published before preflight or dispatch
        var run=Record("run",preview.Id,preview.TenantId);run["planDigest"]=preview.IntegrityDigest;run["startedAt"]=intent["startedAt"]!.DeepClone();
        run["writeAcceptance"]=WriteAcceptance.NotAttempted;run["verification"]=ConfigurationVerification.NotRun;run["status"]=RunStatus.ReviewRequired;
        run["actionId"]=operation.ActionId;run["errorCode"]=null;run["afterDigest"]=null;run["responseAccepted"]=null;
        var boundaryEntered=false;var responseReturned=false;
        try
        {
            CheckApproval();ct.ThrowIfCancellationRequested();
            using(var source=reads(operation))
            {
                var preflight=await new LifecycleBeforeCaptureService(clock).CaptureAsync(source,session,operation,CheckApproval,ct).ConfigureAwait(false);
                evidence.SaveLifecycleBefore(preflight.GetObservation());
                CheckApproval();ct.ThrowIfCancellationRequested();
                if(!preflight.Complete || LifecyclePreparationService.Fingerprint(preflight.GetObservation())!=plan["beforeFingerprint"]!.GetValue<string>())
                    throw new SafetyViolationException("Lifecycle preflight is incomplete or action-relevant state changed. No write is allowed; prepare a fresh plan.");
                LifecycleAuthorityGuard.Require(session,profile,operation,preflight.GetObservation());
                var preservation=LifecycleLicenceGuard.Require(operation,preflight.GetObservation());
                if(preservation!=preview.PreservationRequired)throw new SafetyViolationException("Lifecycle preservation consequences changed after review.");
            }
            if(!Timestamps.TryParse(plan["snapshotAt"]!.GetValue<string>(),out var snapshotAt)||!Timestamps.TryParse(plan["createdAt"]!.GetValue<string>(),out var planAt))throw new SafetyViolationException("Lifecycle review times are invalid.");
            var admission=new ReviewedWriteAdmission(snapshotAt,planAt,TimeSpan.FromMinutes(20),TimeSpan.FromMinutes(10),clock,CheckApproval,()=>ct.IsCancellationRequested);
            boundaryEntered=true;
            var response=await graph.ApplyLifecycleAsync(session,operation,admission,CancellationToken.None).ConfigureAwait(false);
            responseReturned=true;run["writeAcceptance"]=WriteAcceptance.Accepted;run["verification"]=ConfigurationVerification.Unknown;
            // Never persist an assignLicense user response or arbitrary response fields. Only the revocation acknowledgement is relevant.
            if(operation.ActionId=="revoke-sessions" && response["value"] is JsonValue returned && returned.TryGetValue<bool>(out var accepted))run["responseAccepted"]=accepted;
            using(var source=reads(operation))
            {
                // Stop cannot cancel a possibly sent write. Readback has its own bounded read-source lifetime.
                var after=await new LifecycleBeforeCaptureService(clock).CaptureAsync(source,session,operation,RequireReadContext,CancellationToken.None).ConfigureAwait(false);
                evidence.SaveLifecycleBefore(after.GetObservation());run["afterDigest"]=after.IntegrityDigest;run["afterId"]=after.Id;
                if(LifecycleObservedEffect.Verified(operation,plan["before"]!.AsObject(),after.GetObservation(),response))
                {run["verification"]=ConfigurationVerification.Pass;run["status"]=RunStatus.Completed;}
            }
        }
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
        {
            if(responseReturned){run["writeAcceptance"]=WriteAcceptance.Accepted;run["verification"]=ConfigurationVerification.Unknown;run["errorCode"]="ReadbackIncomplete";}
            else if(error is WriteNotSentException || !boundaryEntered)
            {
                run["writeAcceptance"]=WriteAcceptance.NotAttempted;run["errorCode"]="KnownNotSent";
                if(ct.IsCancellationRequested && (error is OperationCanceledException || error is WriteNotSentException {InnerException:OperationCanceledException}))run["status"]=RunStatus.Stopped;
            }
            else if(error is GraphRequestException request && request.StatusCode is >=400 and <500 && request.StatusCode!=408)
            {run["writeAcceptance"]=WriteAcceptance.Rejected;run["httpStatus"]=request.StatusCode;run["errorCode"]="ServerRejected";}
            else {run["writeAcceptance"]=WriteAcceptance.Unknown;run["errorCode"]="AcceptanceUnknown";}
        }
        run["endedAt"]=Timestamps.Format(clock.UtcNow);
        return evidence.SaveLifecycleOutcome(run); // failure here leaves the durable intent unresolved, never replayable
        void CheckApproval(){RequireCurrent(preview,session,profile,standard,snapshot,assertCurrent);}
        void RequireReadContext()
        {
            preview.AssertOriginalContext();assertCurrent();
            if(LifecyclePreparationService.Binding(session)!=plan["sessionBinding"]!.GetValue<string>()
                || CanonicalJson.Sha256Value(profile)!=plan["profileDigest"]!.GetValue<string>() || CanonicalJson.Sha256Value(standard)!=plan["standardDigest"]!.GetValue<string>())
                throw new TenantMismatchException("Lifecycle readback context changed. Original evidence remains for explicit reconciliation.");
        }
    }
    /// <summary>Registered GETs only. Appends an observation; never rewrites acceptance, creates approval or sends/replays a write.</summary>
    public async Task<JsonObject> ReconcileAsync(TenantSession session,TenantProfile profile,StandardCatalogue standard,string runId,
        Func<LifecycleOperation,ILifecycleReadSource> reads,Action assertCurrent,CancellationToken ct)
    {
        if(!session.TenantVerified || !session.OperatorVerified || session.AccountObjectId!=session.OperatorObjectId
            || !Guid.TryParseExact(session.AccountObjectId,"D",out var actor) || actor==Guid.Empty || profile.TenantId!=session.TenantId)
            throw new TenantMismatchException("Read-only lifecycle reconciliation needs a verified account and selected client.");
        using var lease=evidence.AcquireTenantWriteLease(session.TenantId);
        var plan=evidence.RequireLifecycleOriginal(session.TenantId,"plan",runId);
        var intent=evidence.RequireLifecycleOriginal(session.TenantId,"intent",runId);
        if(intent["planDigest"]?.GetValue<string>()!=plan["integrityDigest"]?.GetValue<string>() || CanonicalJson.Sha256Value(standard)!=plan["standardDigest"]?.GetValue<string>())
            throw new SafetyViolationException("Reconciliation requires the intact original plan and its exact verified catalogue; historical meaning is not rebound.");
        var path=Path.Combine(evidence.TenantDirectory(session.TenantId),"lifecycle","run-"+runId+".json");
        var run=File.Exists(path)?evidence.RequireLifecycleOriginal(session.TenantId,"run",runId):null;
        if(run is not null && (run["planDigest"]?.GetValue<string>()!=plan["integrityDigest"]?.GetValue<string>() || run["intentDigest"]?.GetValue<string>()!=intent["integrityDigest"]?.GetValue<string>()))
            throw new SafetyViolationException("Lifecycle outcome does not cite the intact original plan and intent.");
        var operation=LifecycleActionRegistry.RequireRegistered(ToolkitJson.Deserialize<LifecycleOperation>(plan["operation"]!.ToJsonString()));
        var binding=LifecyclePreparationService.Binding(session);var profileDigest=CanonicalJson.Sha256Value(profile);var standardDigest=CanonicalJson.Sha256Value(standard);
        CheckContext();
        var verification=Record("verification",Guid.NewGuid().ToString("D"),session.TenantId);
        verification["runId"]=runId;verification["runDigest"]=(run??intent)["integrityDigest"]!.DeepClone();
        verification["originalWriteAcceptance"]=run?["writeAcceptance"]?.DeepClone()??JsonValue.Create(WriteAcceptance.Unknown);
        verification["intentOnly"]=run is null;verification["accountObjectId"]=session.AccountObjectId;verification["checkedAt"]=Timestamps.Format(clock.UtcNow);
        verification["verified"]=false;verification["afterDigest"]=null;verification["errorCode"]=null;
        try
        {
            using var source=reads(operation);
            var after=await new LifecycleBeforeCaptureService(clock).CaptureAsync(source,session,operation,CheckContext,ct).ConfigureAwait(false);
            evidence.SaveLifecycleBefore(after.GetObservation());CheckContext();ct.ThrowIfCancellationRequested();
            var reply=new JsonObject();
            if(run?["responseAccepted"] is JsonValue value && value.TryGetValue<bool>(out var accepted))reply["value"]=accepted;
            verification["verified"]=LifecycleObservedEffect.Verified(operation,plan["before"]!.AsObject(),after.GetObservation(),reply);
            verification["afterId"]=after.Id;verification["afterDigest"]=after.IntegrityDigest;
        }
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
        {verification["verified"]=false;verification["errorCode"]=error is OperationCanceledException?"Stopped":"ReadbackIncomplete";}
        verification["acceptanceRemainsUnresolved"]=run is null || WriteAcceptance.IsUncertain(run["writeAcceptance"]?.GetValue<string>());
        return evidence.SaveLifecycleVerification(verification);
        void CheckContext()
        {
            assertCurrent();ct.ThrowIfCancellationRequested();
            if(LifecyclePreparationService.Binding(session)!=binding || CanonicalJson.Sha256Value(profile)!=profileDigest || CanonicalJson.Sha256Value(standard)!=standardDigest)
                throw new TenantMismatchException("Read-only reconciliation context changed. Older evidence remains intact.");
        }
    }

    private JsonObject RequireCurrent(LifecyclePreview preview,TenantSession session,TenantProfile profile,StandardCatalogue standard,TenantSnapshot snapshot,Action assertCurrent)
    {
        preview.AssertOriginalContext();assertCurrent();var plan=preview.GetDocument();
        var stored=evidence.RequireLifecycleOriginal(preview.TenantId,"plan",preview.Id);
        if(CanonicalJson.Sha256(stored)!=CanonicalJson.Sha256(plan) || session.TenantId!=preview.TenantId
            || session.Mode!=SessionMode.Deployment || LifecyclePreparationService.Binding(session)!=plan["sessionBinding"]!.GetValue<string>()
            || CanonicalJson.Sha256Value(profile)!=plan["profileDigest"]!.GetValue<string>() || CanonicalJson.Sha256Value(standard)!=plan["standardDigest"]!.GetValue<string>())
            throw new TenantMismatchException("Lifecycle plan, client, access, profile or catalogue changed. Prepare and review a new plan.");
        var saved=evidence.RequireDeploymentSnapshot(snapshot,standard);
        if(saved.Id!=plan["snapshotId"]!.GetValue<string>() || saved.IntegrityDigest!=plan["snapshotDigest"]!.GetValue<string>())throw new SafetyViolationException("Lifecycle before-change snapshot changed.");
        if(!Timestamps.TryParse(plan["createdAt"]!.GetValue<string>(),out var created)||!Timestamps.TryParse(plan["snapshotAt"]!.GetValue<string>(),out var at)
            ||!Timestamps.TryParse(plan["originalTokenExpiresAt"]!.GetValue<string>(),out var expires)||clock.UtcNow<created||clock.UtcNow<at||clock.UtcNow>=expires
            ||clock.UtcNow-created>TimeSpan.FromMinutes(10)||clock.UtcNow-at>TimeSpan.FromMinutes(20))throw new SafetyViolationException("Original lifecycle approval, snapshot or authorisation expired. Renewal cannot extend it.");
        return plan;
    }
    private void AssertOtherWritesResolved(string tenant,StandardCatalogue standard)
    {
        evidence.AssertNoUnresolvedLifecycle(tenant);
        var controls=standard.Controls.Select(c=>c.Id).Concat(evidence.RequireIntactRuns(tenant).SelectMany(run=>run.Results.Select(row=>row.ControlId)))
            .Concat(evidence.LoadRecoveryRuns(tenant).Select(run=>run.ControlId)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        evidence.AssertCompletionWritesResolved(tenant,controls);
        if(evidence.LoadPackageRuns(tenant).Any(run=>run.Steps.Any(step=>WriteAcceptance.IsUncertain(step.Acceptance))))throw new SafetyViolationException("An earlier package write has unknown acceptance. Reconcile before lifecycle actions.");
    }
    private string IntentFile(LifecyclePreview preview)=>Path.Combine(evidence.TenantDirectory(preview.TenantId),"lifecycle","intent-"+preview.Id+".json");
    private static JsonObject Record(string family,string id,string tenant)=>new(){["kind"]="lifecycle-"+family,["schemaVersion"]=1,["id"]=id,["tenantId"]=tenant,["integrityDigest"]=""};
}
