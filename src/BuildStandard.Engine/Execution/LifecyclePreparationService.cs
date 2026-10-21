using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.Engine.Execution;

/// <summary>Owned immutable preview. A saved/exported JSON document cannot recreate this runtime object or approve a write.</summary>
public sealed class LifecyclePreview
{
    private readonly JsonObject _document;
    internal LifecyclePreview(JsonObject document,Action originalContext){_document=document.DeepClone().AsObject();AssertOriginalContext=originalContext;}
    internal Action AssertOriginalContext { get; }
    public string Id=>_document["id"]!.GetValue<string>();
    public string TenantId=>_document["tenantId"]!.GetValue<string>();
    public string IntegrityDigest=>_document["integrityDigest"]!.GetValue<string>();
    public bool PreservationRequired=>_document["preservationRequired"]!.GetValue<bool>();
    public JsonObject GetDocument()=>_document.DeepClone().AsObject();
}

/// <summary>Prepares one exact action from complete saved configuration plus fresh owned action-specific evidence.
/// It cannot execute, record consent, attest retention or turn an imported report into approval.</summary>
public sealed class LifecyclePreparationService(EvidenceStore evidence,IClock clock)
{
    public async Task<LifecyclePreview> PreviewAsync(ILifecycleReadSource source,TenantSession session,TenantProfile profile,
        StandardCatalogue standard,TenantSnapshot snapshot,LifecycleOperation selection,Action assertCurrent,CancellationToken ct)
    {
        var operation=LifecycleActionRegistry.RequireRegistered(selection);
        var binding=Binding(session);var profileDigest=CanonicalJson.Sha256Value(profile);var standardDigest=CanonicalJson.Sha256Value(standard);
        var originalExpiry=session.TokenExpiresAt;
        RequireCurrent();
        var stored=evidence.RequireDeploymentSnapshot(snapshot,standard);FreshSnapshot();
        var snapshotDigest=stored.IntegrityDigest;
        var capture=await new LifecycleBeforeCaptureService(clock).CaptureAsync(source,session,operation,RequireCurrent,ct).ConfigureAwait(false);
        evidence.SaveLifecycleBefore(capture.GetObservation());
        RequireCurrent();ct.ThrowIfCancellationRequested();FreshSnapshot();
        if(!capture.Complete)throw new SafetyViolationException("Lifecycle before-evidence is incomplete. Preserve the read diagnostics and correct readiness; no action can be approved.");
        var before=capture.GetObservation();
        LifecycleAuthorityGuard.Require(session,profile,operation,before);
        var preservation=LifecycleLicenceGuard.Require(operation,before);
        RequireChange(operation,before);
        var document=new JsonObject
        {
            ["kind"]="lifecycle-plan",["schemaVersion"]=1,["id"]=Guid.NewGuid().ToString("D"),
            ["tenantId"]=session.TenantId,["accountObjectId"]=session.AccountObjectId,["clientId"]=session.ClientId,["permissionMode"]=session.Mode.ToString(),
            ["createdAt"]=Timestamps.Format(clock.UtcNow),["originalTokenExpiresAt"]=originalExpiry,["toolkitVersion"]=ToolkitVersion.Current,
            ["snapshotId"]=stored.Id,["snapshotDigest"]=snapshotDigest,["snapshotAt"]=stored.CapturedAt,
            ["standardRelease"]=standard.Release,["standardDigest"]=standardDigest,["profileDigest"]=profileDigest,
            ["sessionBinding"]=binding,["operation"]=ToolkitJson.ToNode(operation),["before"]=before,
            ["beforeFingerprint"]=Fingerprint(before),["preservationRequired"]=preservation,
            ["consequence"]=Consequence(operation),["integrityDigest"]=""
        };
        document["integrityDigest"]=CanonicalJson.Sha256(document);
        RequireCurrent();ct.ThrowIfCancellationRequested();
        // Only local preview persistence. Existing unresolved writes are not reconciled by making this preview.
        evidence.SaveLifecyclePreview(document);
        return new(document,assertCurrent);
        void RequireCurrent()
        {
            assertCurrent();ct.ThrowIfCancellationRequested();
            if(session.Mode!=SessionMode.Deployment || !session.TenantVerified || !session.OperatorVerified
                || profile.TenantId!=session.TenantId || Binding(session)!=binding
                || CanonicalJson.Sha256Value(profile)!=profileDigest || CanonicalJson.Sha256Value(standard)!=standardDigest)
                throw new TenantMismatchException("Lifecycle client, account, access, profile or catalogue changed. Prepare a fresh preview.");
            if(!Timestamps.TryParse(originalExpiry,out var expires)||clock.UtcNow>=expires)
                throw new WriteDeniedException("The original lifecycle authorisation expired or its expiry is unknown. Reconnect deliberately and prepare a new preview.");
            if(!session.Scopes.Any(scope=>Scope(scope,operation.WriteScope)) || !session.Scopes.Any(scope=>Scope(scope,"User.Read.All")))
                throw new WriteDeniedException("The selected lifecycle action requires its explicit write access and User.Read.All. No access is granted by this preview.");
        }
        void FreshSnapshot()
        {
            if(stored.TenantId!=session.TenantId || !Timestamps.TryParse(stored.CapturedAt,out var at) || clock.UtcNow<at || clock.UtcNow-at>TimeSpan.FromMinutes(20))
                throw new SafetyViolationException("Capture and save a fresh complete deployment snapshot for this client before preparing a lifecycle action.");
        }
    }
    internal static string Binding(TenantSession session)=>CanonicalJson.Sha256Value(new {session.TenantId,session.AccountObjectId,session.OperatorObjectId,session.ClientId,session.Mode,session.TenantVerified,session.OperatorVerified,Scopes=session.Scopes.Order(StringComparer.Ordinal).ToArray()});
    internal static string Fingerprint(JsonObject before)
    {
        var values=new JsonObject();
        foreach(var key in new[]{"user","group","members","currentRoles","eligibleRoles","roleAssignableGroups","subscribedSkus","licenseDetails"})
            if(before.TryGetPropertyValue(key,out var value))values[key]=Normalise(value);
        return CanonicalJson.Sha256(values);
    }
    private static JsonNode? Normalise(JsonNode? value)=>value switch
    {
        JsonArray array=>new JsonArray(array.Select(Normalise).OrderBy(CanonicalJson.Serialize,StringComparer.Ordinal).ToArray()),
        JsonObject row=>new JsonObject(row.Select(pair=>new KeyValuePair<string,JsonNode?>(pair.Key,Normalise(pair.Value)))),
        _=>value?.DeepClone()
    };
    private static void RequireChange(LifecycleOperation operation,JsonObject before)
    {
        if(operation.ActionId is "enable-account" or "disable-account")
        {
            if(before["user"]!["accountEnabled"]!.GetValue<bool>()==(operation.ActionId=="enable-account"))throw new SafetyViolationException("The selected account setting already matches. No write is needed.");
        }
        if(operation.ActionId is "add-member" or "remove-member")
        {
            if(before["members"] is not JsonArray members)throw new SafetyViolationException("Complete direct membership is required.");
            var present=members.OfType<JsonObject>().Any(row=>string.Equals(row["id"]?.GetValue<string>(),operation.UserId,StringComparison.OrdinalIgnoreCase));
            if(present==(operation.ActionId=="add-member"))throw new SafetyViolationException("Direct membership already matches the requested result. No write is needed.");
        }
        if(operation.ActionId=="revoke-sessions" && !Timestamps.TryParse(before["user"]?["signInSessionsValidFromDateTime"]?.GetValue<string>(),out _))
            throw new SafetyViolationException("The original session-validity stamp is unknown; revocation cannot be verified.");
    }
    private static bool Scope(string raw,string expected)=>string.Equals(raw.StartsWith("https://graph.microsoft.com/",StringComparison.OrdinalIgnoreCase)?raw[28..]:raw,expected,StringComparison.OrdinalIgnoreCase);
    private static string Consequence(LifecycleOperation operation)=>operation.ActionId switch
    {
        "add-member"=>"Adds exactly one direct group member. Group-based licence/access effects may propagate later and require separate verification.",
        "remove-member"=>"Removes exactly one direct membership, not the user object. Group-based access/licensing can be withdrawn. Review preservation and downstream ownership.",
        "assign-license"=>"Adds or updates one direct SKU with exactly the reviewed disabled plans. New disabled plans may withdraw service; inherited entitlement remains separately observed.",
        "remove-license"=>"Removes one direct SKU only. Inherited entitlement is not removed. Preservation/retention and business-owner review are manual; restoration may lack capacity or fail to restore data.",
        "revoke-sessions"=>"Revokes this user's refresh sessions. The effect can be delayed and does not guarantee every application session ended. This is not reversible.",
        _=>"Changes only accountEnabled for this user. Downstream access and workload privileges require separate review. Reversal requires a fresh reviewed plan."
    };
}
