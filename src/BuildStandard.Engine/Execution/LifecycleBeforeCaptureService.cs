using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
namespace BuildStandard.Engine.Execution;

/// <summary>Immutable output of an owned capture, not approval. Historical JSON cannot construct this runtime result.</summary>
public sealed class LifecycleBeforeCapture
{
    private readonly JsonObject _observation;
    private readonly byte[] _original;
    internal LifecycleBeforeCapture(JsonObject observation)
    { _observation=observation.DeepClone().AsObject(); _original=Encoding.UTF8.GetBytes(_observation.ToJsonString()); }
    public bool Complete=>_observation["complete"]!.GetValue<bool>();
    public string Id=>_observation["id"]!.GetValue<string>();
    public string TenantId=>_observation["tenantId"]!.GetValue<string>();
    public string IntegrityDigest=>_observation["integrityDigest"]!.GetValue<string>();
    public JsonObject GetObservation()=>_observation.DeepClone().AsObject();
    public byte[] GetOriginalBytes()=>_original.ToArray();
}

/// <summary>Collects only fixed dependencies. Collection completeness is separate from target/licence readiness and approval.</summary>
public sealed class LifecycleBeforeCaptureService(IClock clock)
{
    private const int MaximumRows=5000, MaximumBytes=16*1024*1024, ReservedEnvelopeBytes=128*1024;
    public Task<LifecycleBeforeCapture> CaptureAsync(ILifecycleReadSource source,TenantSession session,LifecycleOperation selection,Action assertCurrent,CancellationToken ct)
        => Task.Run(()=>CaptureCoreAsync(source,session,selection,assertCurrent,ct),ct);
    private async Task<LifecycleBeforeCapture> CaptureCoreAsync(ILifecycleReadSource source,TenantSession session,LifecycleOperation selection,Action assertCurrent,CancellationToken ct)
    {
        var operation=LifecycleActionRegistry.RequireRegistered(selection); var requests=LifecycleReadRegistry.For(operation);
        var binding=Binding(session); var started=clock.UtcNow;
        RequireIdentity(); assertCurrent();
        foreach(var request in requests)
            if(!session.Scopes.Any(scope=>Scope(scope,request.Scope))) throw new WriteDeniedException("Required lifecycle read access is unavailable: "+request.Scope);
        var root=new JsonObject
        {
            ["kind"]="lifecycle-before",["schemaVersion"]=1,["id"]=Guid.NewGuid().ToString("D"),["tenantId"]=session.TenantId,
            ["accountObjectId"]=session.AccountObjectId,["clientId"]=session.ClientId,["permissionMode"]=session.Mode.ToString(),
            ["originalTokenExpiresAt"]=session.TokenExpiresAt,
            ["operation"]=JsonSerializer.SerializeToNode(operation,ToolkitJson.Options),["startedAt"]=Timestamps.Format(started),
            ["endedAt"]=Timestamps.Format(started),["toolkitVersion"]=ToolkitVersion.Current,["complete"]=false,
            ["requestCount"]=0,["sources"]=new JsonObject(),["integrityDigest"]=""
        };
        var states=root["sources"]!.AsObject(); foreach(var request in requests) states[request.Id]=new JsonObject { ["status"]="NotAttempted",["rowsRead"]=0,["problem"]=null,["httpStatus"]=null,["failureCategory"]=null };
        var rows=0; var bytes=0; string? active=null;
        try
        {
            foreach(var request in requests)
            {
                active=request.Id; var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while(true)
                {
                    CheckCurrent();
                    var page=await source.ReadNextAsync(request.Id,ct).ConfigureAwait(false);
                    CheckCurrent();
                    if(source.RequestCount>100 || page.Rows.Count>MaximumRows-rows || request.Single && (page.HasMore || page.Rows.Count!=1))
                        throw new SafetyViolationException("Lifecycle response exceeded its bounds or returned an incomplete singleton.");
                    var projected=new List<JsonObject>(); var pageBytes=0;
                    foreach(var item in page.Rows)
                    {
                        var value=Project(request.Id,item);
                        var key=request.Id is "subscribedSkus" or "licenseDetails"?"skuId":"id";
                        if(value[key] is not JsonValue identity || !identity.TryGetValue<string>(out var id) || !Guid.TryParseExact(id,"D",out var guid) || guid==Guid.Empty || !ids.Add(id))
                            throw new SafetyViolationException("Missing, malformed or duplicate lifecycle source identity.");
                        if(request.Id=="user" && !string.Equals(id,operation.UserId,StringComparison.OrdinalIgnoreCase)
                            || request.Id=="group" && !string.Equals(id,operation.GroupId,StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("Lifecycle target identity changed.");
                        if(request.Id is "currentRoles" or "eligibleRoles" && (value["principalId"] is not JsonValue principal || !principal.TryGetValue<string>(out var principalId)
                            || !string.Equals(principalId,operation.UserId,StringComparison.OrdinalIgnoreCase))) throw new SafetyViolationException("Role source did not identify the selected target.");
                        if(request.Id=="roleAssignableGroups" && (value["isAssignableToRole"] is not JsonValue role || !role.TryGetValue<bool>(out var assignable) || !assignable))
                            throw new SafetyViolationException("Role-group inventory did not retain its exact filter meaning.");
                        pageBytes=checked(pageBytes+Encoding.UTF8.GetByteCount(value.ToJsonString())); projected.Add(value);
                    }
                    if(pageBytes>MaximumBytes-ReservedEnvelopeBytes-bytes) throw new SafetyViolationException("Lifecycle before-evidence byte bound ended.");
                    // Admit a page only after its context, identity and shape checks all succeed.
                    if(request.Single) root[request.Id]=projected[0];
                    else
                    {
                        if(root[request.Id] is null) root[request.Id]=new JsonArray();
                        foreach(var value in projected) root[request.Id]!.AsArray().Add(value);
                    }
                    rows+=projected.Count; bytes+=pageBytes;
                    var state=states[request.Id]!.AsObject(); state["rowsRead"]=ids.Count; state["status"]=page.HasMore?"Partial":"Collected";
                    if(!page.HasMore) break;
                    if(rows>=MaximumRows) throw new SafetyViolationException("Lifecycle row cap ended before the collection completed.");
                }
            }
            CheckCurrent(); root["complete"]=true;
        }
        catch(Exception error) when(error is not OutOfMemoryException and not StackOverflowException)
        {
            if(active is not null)
            {
                var state=states[active]!.AsObject();
                state["status"]=state["rowsRead"]!.GetValue<int>()>0?"Partial":error is OperationCanceledException?"Cancelled":"Failed";
                state["httpStatus"]=error is GraphRequestException graph && graph.StatusCode is >=100 and <=599 ? JsonValue.Create(graph.StatusCode) : null;
                state["failureCategory"]=error is OperationCanceledException?"Stopped":error is TenantMismatchException?"ContextChanged":error is GraphRequestException?"HttpReadFailed":error is ToolkitException?"ReadRefused":"UnexpectedReadFailure";
                state["problem"]=error is OperationCanceledException?"Stopped during required lifecycle reads; earlier observations are incomplete."
                    :"Unable to establish complete lifecycle before-evidence. Review the original context, access, supported source shape and limits. No server body or authentication material was retained.";
            }
            root["complete"]=false;
        }
        root["endedAt"]=Timestamps.Format(clock.UtcNow); root["requestCount"]=source.RequestCount;
        root["integrityDigest"]=CanonicalJson.Sha256(root);
        return new(root);

        void RequireIdentity()
        {
            if(!session.TenantVerified || !session.OperatorVerified
                || !Guid.TryParseExact(session.AccountObjectId,"D",out var accountId) || accountId==Guid.Empty
                || !string.Equals(session.OperatorObjectId,session.AccountObjectId,StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.TenantId,session.TenantId,StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.AccountObjectId,session.AccountObjectId,StringComparison.OrdinalIgnoreCase)
                || !string.Equals(source.ClientId,session.ClientId,StringComparison.OrdinalIgnoreCase) || source.Mode!=session.Mode
                || source.Operation!=operation) throw new TenantMismatchException("Lifecycle source must match the exact verified account, tenant, registration, mode and action before reading.");
        }
        void CheckCurrent()
        {
            ct.ThrowIfCancellationRequested(); assertCurrent(); RequireIdentity();
            if(Binding(session)!=binding) throw new TenantMismatchException("Lifecycle identity or access changed during capture.");
            var now=clock.UtcNow; if(now<started || now-started>TimeSpan.FromMinutes(5)) throw new SafetyViolationException("Lifecycle preview time bound ended or the clock changed.");
        }
    }
    private static JsonObject Project(string source,JsonObject row)
    {
        string[] fields=source switch
        {
            "user"=>["id","displayName","userPrincipalName","userType","onPremisesSyncEnabled","accountEnabled","usageLocation","assignedLicenses","licenseAssignmentStates","signInSessionsValidFromDateTime"],
            "group"=>["id","displayName","groupTypes","securityEnabled","mailEnabled","onPremisesSyncEnabled","isAssignableToRole","assignedLicenses"],
            "members"=>["id"],"currentRoles" or "eligibleRoles"=>["id","principalId","roleDefinitionId","directoryScopeId","appScopeId"],
            "roleAssignableGroups"=>["id","displayName","isAssignableToRole"],
            "subscribedSkus"=>["skuId","skuPartNumber","capabilityStatus","consumedUnits","prepaidUnits","servicePlans"],
            "licenseDetails"=>["skuId","skuPartNumber","servicePlans"],_=>throw new ConfigurationException("Unregistered lifecycle source.")
        };
        var result=new JsonObject();
        foreach(var field in fields)
        {
            if(!row.TryGetPropertyValue(field,out var value))continue;
            result[field]=field switch
            {
                "assignedLicenses"=>Objects(value,["skuId","disabledPlans"]),
                "licenseAssignmentStates"=>Objects(value,["skuId","disabledPlans","assignedByGroup","state","error","lastUpdatedDateTime"]),
                "servicePlans"=>Objects(value,["servicePlanId","servicePlanName","provisioningStatus","appliesTo"]),
                "prepaidUnits"=>value is null?null:Scalars(value.AsObject(),["enabled","suspended","warning"]),
                "groupTypes"=>Values(value),_=>Scalar(value)
            };
        }
        return result;
    }
    private static JsonNode? Objects(JsonNode? value,string[] fields)
    {
        if(value is null)return null;
        if(value is not JsonArray array) throw new SafetyViolationException("Required lifecycle object collection has an unsupported shape.");
        var result=new JsonArray(); foreach(var item in array)
        {
            if(item is not JsonObject row)throw new SafetyViolationException("Lifecycle nested row has an unsupported shape.");
            var copy=new JsonObject(); foreach(var field in fields) if(row.TryGetPropertyValue(field,out var node)) copy[field]=field=="disabledPlans"?Values(node):Scalar(node);
            result.Add(copy);
        }
        return result;
    }
    private static JsonObject Scalars(JsonObject row,string[] fields)
    { var result=new JsonObject();foreach(var field in fields)if(row.TryGetPropertyValue(field,out var value))result[field]=Scalar(value);return result; }
    private static JsonNode? Values(JsonNode? node)
    {
        if(node is null)return null;
        if(node is not JsonArray array)throw new SafetyViolationException("Required lifecycle value collection has an unsupported shape.");
        var copy=new JsonArray();foreach(var value in array)copy.Add(Scalar(value));return copy;
    }
    private static JsonNode? Scalar(JsonNode? value)
    { if(value is null)return null;if(value is not JsonValue)throw new SafetyViolationException("Unexpected structured lifecycle field.");return value.DeepClone(); }
    private static bool Scope(string raw,string expected)
    { var value=raw.Trim();const string prefix="https://graph.microsoft.com/";if(value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))value=value[prefix.Length..];return string.Equals(value,expected,StringComparison.OrdinalIgnoreCase); }
    private static string Binding(TenantSession session)=>ToolkitJson.Serialize(new {session.TenantId,session.AccountObjectId,session.OperatorObjectId,session.ClientId,session.Mode,session.TenantVerified,session.OperatorVerified,Scopes=session.Scopes.Order(StringComparer.Ordinal).ToArray()});
}
