using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Execution;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleActionJourneyTests
{
    private sealed class Tenant
    {
        public bool Enabled=true,Member,Direct,Inherited;public string Stamp="2026-09-11T09:00:00Z";public string[] Disabled=[];
        public JsonArray States()=>new((Direct?new[]{new JsonObject { ["skuId"]=TestData.ClientId,["assignedByGroup"]=null,["disabledPlans"]=new JsonArray(Disabled.Select(id=>(JsonNode?)JsonValue.Create(id)).ToArray()),["state"]="Active",["error"]="None" }}:Array.Empty<JsonObject>()).Concat(Inherited?new[]{new JsonObject { ["skuId"]=TestData.ClientId,["assignedByGroup"]=TestData.Mam,["disabledPlans"]=new JsonArray(),["state"]="Active",["error"]="None" }}:Array.Empty<JsonObject>()).Cast<JsonNode?>().ToArray());
        public IReadOnlyList<JsonObject> Rows(string id)=>id switch
        {
            "user"=>[new JsonObject { ["id"]=TestData.Office,["displayName"]="Synthetic member",["userPrincipalName"]="member@synthetic.invalid",["userType"]="Member",["accountEnabled"]=Enabled,["onPremisesSyncEnabled"]=false,["usageLocation"]="GB",["signInSessionsValidFromDateTime"]=Stamp,
                ["assignedLicenses"]=Direct||Inherited?new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["disabledPlans"]=new JsonArray() }):new JsonArray(),["licenseAssignmentStates"]=States() }],
            "group"=>[new JsonObject { ["id"]=TestData.Mam,["displayName"]="Synthetic cloud security group",["groupTypes"]=new JsonArray(),["securityEnabled"]=true,["mailEnabled"]=false,["isAssignableToRole"]=false,["onPremisesSyncEnabled"]=null,["assignedLicenses"]=new JsonArray() }],
            "members"=>Member?[new JsonObject { ["id"]=TestData.Office }]:[],
            "subscribedSkus"=>[new JsonObject { ["skuId"]=TestData.ClientId,["capabilityStatus"]="Enabled",["consumedUnits"]=1,["prepaidUnits"]=new JsonObject { ["enabled"]=2,["suspended"]=0,["warning"]=0 },["servicePlans"]=new JsonArray(new JsonObject { ["servicePlanId"]=TestData.Emergency,["provisioningStatus"]="Success",["appliesTo"]="User" }) }],
            "licenseDetails"=>Direct||Inherited?[new JsonObject { ["skuId"]=TestData.ClientId,["servicePlans"]=new JsonArray(new JsonObject { ["servicePlanId"]=TestData.Emergency,["provisioningStatus"]="Success" }) }]:[],_=>[]
        };
    }
    private sealed class Source(Tenant tenant,LifecycleOperation operation) : ILifecycleReadSource
    {
        public string TenantId=>TestData.TenantA;public string AccountObjectId=>TestData.Operator;public string ClientId=>TestData.ClientId;public SessionMode Mode=>SessionMode.Deployment;public LifecycleOperation Operation=>operation;public int RequestCount{get;private set;}
        public Task<LifecycleReadPage> ReadNextAsync(string id,CancellationToken ct){ct.ThrowIfCancellationRequested();RequestCount++;return Task.FromResult(new LifecycleReadPage(tenant.Rows(id),false));}
        public void Dispose(){}
    }
    private sealed class Graph(Tenant tenant) : IGraphClient
    {
        public string TenantId=>TestData.TenantA;public SessionMode Mode=>SessionMode.Deployment;public int Writes;
        public Task<JsonObject> GetAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<IReadOnlyList<JsonObject>> GetAllAsync(GraphApi api,string path,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<JsonObject> WriteAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject payload,CancellationToken ct)=>throw new InvalidOperationException("Generic lifecycle write forbidden");
        public Task<JsonObject> WriteReviewedAsync(GraphApi api,GraphWriteMethod method,string path,JsonObject payload,ReviewedWriteAdmission admission,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<JsonObject> ApplyLifecycleAsync(TenantSession session,LifecycleOperation operation,ReviewedWriteAdmission admission,CancellationToken ct)
        {
            _=LifecycleActionRegistry.RequireRegistered(operation);admission.AssertAllowed();Writes++;
            switch(operation.ActionId){case "enable-account":tenant.Enabled=true;break;case "disable-account":tenant.Enabled=false;break;case "add-member":tenant.Member=true;break;case "remove-member":tenant.Member=false;break;case "assign-license":tenant.Direct=true;tenant.Disabled=JsonNode.Parse(operation.PayloadJson!)!["addLicenses"]![0]!["disabledPlans"]!.AsArray().Select(value=>value!.GetValue<string>()).ToArray();break;case "remove-license":tenant.Direct=false;break;case "revoke-sessions":tenant.Stamp="2026-09-11T10:00:01Z";return Task.FromResult(new JsonObject { ["value"]=true });}
            return Task.FromResult(new JsonObject());
        }
    }
    public static IEnumerable<object[]> Actions=>LifecycleActionRegistry.ActionIds.Select(action=>new object[]{action});
    [Theory][MemberData(nameof(Actions))]
    public async Task Every_registered_action_has_exact_approval_one_send_observed_effect_and_immutable_history(string action)
    {
        using var root=new TempRoot();var clock=new FixedClock();var evidence=new EvidenceStore(root.Paths,NullLog.Instance);var standard=TestData.Standard();var profile=TestData.Profile();var snapshot=TestData.Snapshot(standard);evidence.SaveSnapshot(snapshot);
        var operation=LifecycleActionRegistry.Create(action,TestData.Office,action.EndsWith("member",StringComparison.Ordinal)?TestData.Mam:null,action.EndsWith("license",StringComparison.Ordinal)?TestData.ClientId:null);
        var session=TestData.Session();session.TokenExpiresAt=Timestamps.Format(clock.UtcNow.AddMinutes(10));session.Scopes=["User.Read.All","Group.Read.All","Organization.Read.All","RoleManagement.Read.Directory",operation.WriteScope];
        var tenant=new Tenant {Enabled=action!="enable-account",Member=action=="remove-member",Direct=action=="remove-license",Inherited=action=="remove-license"};
        using var source=new Source(tenant,operation);var preview=await new LifecyclePreparationService(evidence,clock).PreviewAsync(source,session,profile,standard,snapshot,operation,()=>{},default);
        var service=new LifecycleRunService(evidence,clock);var graph=new Graph(tenant);
        var approval=service.Approve(preview,session,profile,standard,snapshot,preview.IntegrityDigest,true,true,preview.PreservationRequired,()=>{});
        var run=await service.ExecuteAsync(graph,session,profile,standard,snapshot,approval,selected=>new Source(tenant,selected),()=>{},default);
        Assert.Equal(1,graph.Writes);Assert.Equal(WriteAcceptance.Accepted,run["writeAcceptance"]?.GetValue<string>());Assert.Equal(ConfigurationVerification.Pass,run["verification"]?.GetValue<string>());
        evidence.AssertNoUnresolvedLifecycle(TestData.TenantA);
        if(action=="remove-license"){Assert.True(tenant.Inherited);Assert.False(tenant.Direct);Assert.True(preview.PreservationRequired);}
        await Assert.ThrowsAnyAsync<ToolkitException>(()=>service.ExecuteAsync(graph,session,profile,standard,snapshot,approval,selected=>new Source(tenant,selected),()=>{},default));Assert.Equal(1,graph.Writes);
    }
}
