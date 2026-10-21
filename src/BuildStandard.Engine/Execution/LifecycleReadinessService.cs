using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.Engine.Execution;

public sealed record LifecycleReadinessRow(string Source,string Scope,string Status,int RowsAdmitted,string Detail);
/// <summary>Read-only target observations, never complete configuration before-evidence, plan approval or a non-privilege guarantee.</summary>
public sealed record LifecycleReadinessResult(bool Ready,bool? PreservationRequired,string Guidance,LifecycleBeforeCapture Original,IReadOnlyList<LifecycleReadinessRow> Rows);

public sealed class LifecycleReadinessService(EvidenceStore evidence,IClock clock)
{
    public async Task<LifecycleReadinessResult> CaptureAsync(ILifecycleReadSource source,TenantSession session,TenantProfile profile,
        LifecycleOperation selection,Action assertCurrent,CancellationToken ct)
    {
        var operation=LifecycleActionRegistry.RequireRegistered(selection);var digest=CanonicalJson.Sha256Value(profile);
        var original=await new LifecycleBeforeCaptureService(clock).CaptureAsync(source,session,operation,CheckCurrent,ct).ConfigureAwait(false);
        evidence.SaveLifecycleBefore(original.GetObservation());CheckCurrent();
        var before=original.GetObservation();var states=before["sources"]!.AsObject();
        var rows=LifecycleReadRegistry.For(operation).Select(request=>
        {
            var state=states[request.Id]!.AsObject();
            var http=state["httpStatus"] is JsonValue status && status.TryGetValue<int>(out var number)?" HTTP "+number+".":"";
            return new LifecycleReadinessRow(Label(request.Id),request.Scope,state["status"]!.GetValue<string>(),state["rowsRead"]!.GetValue<int>(),http+(state["problem"]?.GetValue<string>()??"Registered source observed. Completeness, target authority and licence readiness are separate checks."));
        }).ToArray();
        if(!original.Complete)return new(false,null,"Readiness is incomplete. Review failed/partial sources and their required read access, administrator role or licence/API availability. Missing privilege or licence evidence is not an empty-safe result. Keep the action manual until readiness can be established.",original,rows);
        try
        {
            LifecycleAuthorityGuard.Require(session,profile,operation,before);
            var preservation=LifecycleLicenceGuard.Require(operation,before);
            return new(true,preservation,"Target prerequisites observed; this is not approval. It is not proof of absence of every workload/external privilege. Deployment still requires a fresh complete saved configuration snapshot, selected write access, exact review, preflight and single-use approval.",original,rows);
        }
        catch(ToolkitException error)
        {return new(false,null,"Target readiness is blocked: "+error.Message+" No write was approved or sent.",original,rows);}
        void CheckCurrent()
        {
            assertCurrent();ct.ThrowIfCancellationRequested();
            if(profile.TenantId!=session.TenantId || CanonicalJson.Sha256Value(profile)!=digest)throw new TenantMismatchException("The selected client/protected-account inputs changed during readiness. Original observations remain under their source client.");
        }
    }
    private static string Label(string source)=>source switch
    {
        "user"=>"User identity and authority","currentRoles"=>"Current Entra directory roles","eligibleRoles"=>"Eligible Entra directory roles",
        "roleAssignableGroups"=>"Role-assignable group inventory","group"=>"Selected group authority and licensing","members"=>"Complete direct group membership",
        "subscribedSkus"=>"SKU capacity and service plans","licenseDetails"=>"User service-plan provisioning",_=>"Unclassified source"
    };
}
