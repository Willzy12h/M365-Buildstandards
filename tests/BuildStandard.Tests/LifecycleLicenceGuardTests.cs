using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Safety;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleLicenceGuardTests
{
    private const string User="11111111-1111-4111-8111-111111111111",Sku="22222222-2222-4222-8222-222222222222",Plan="33333333-3333-4333-8333-333333333333",Group="44444444-4444-4444-8444-444444444444";
    private static JsonObject Fixture(bool direct=false,bool inherited=false)=>new()
    {
        ["user"]=new JsonObject { ["usageLocation"]="GB", ["assignedLicenses"]=direct||inherited?new JsonArray(new JsonObject { ["skuId"]=Sku,["disabledPlans"]=new JsonArray() }):new JsonArray(),
            ["licenseAssignmentStates"]=direct||inherited?new JsonArray(new JsonObject { ["skuId"]=Sku,["disabledPlans"]=new JsonArray(),["assignedByGroup"]=inherited?Group:null,["state"]="Active",["error"]="None" }):new JsonArray() },
        ["subscribedSkus"]=new JsonArray(new JsonObject { ["skuId"]=Sku,["capabilityStatus"]="Enabled",["consumedUnits"]=2,["prepaidUnits"]=new JsonObject { ["enabled"]=3,["suspended"]=0,["warning"]=0 },
            ["servicePlans"]=new JsonArray(new JsonObject { ["servicePlanId"]=Plan,["provisioningStatus"]="Success",["appliesTo"]="User" }) }),
        ["licenseDetails"]=direct||inherited?new JsonArray(new JsonObject { ["skuId"]=Sku,["servicePlans"]=new JsonArray(new JsonObject { ["servicePlanId"]=Plan,["provisioningStatus"]="Success" }) }):new JsonArray(),
        ["group"]=new JsonObject { ["assignedLicenses"]=new JsonArray(new JsonObject { ["skuId"]=Sku,["disabledPlans"]=new JsonArray() }) }
    };
    private static bool Require(JsonObject observed,string action="assign-license",string[]? disabled=null)
    {
        var type=typeof(BuildStandard.Engine.Execution.ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecycleLicenceGuard");Assert.NotNull(type);
        var method=type.GetMethod("Require",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(method);
        var operation=LifecycleActionRegistry.Create(action,User,action.EndsWith("member",StringComparison.Ordinal)?Group:null,action.EndsWith("license",StringComparison.Ordinal)?Sku:null,disabled);
        try{return (bool)method.Invoke(null,new object[]{operation,observed})!;}
        catch(TargetInvocationException error)when(error.InnerException is not null){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    [Fact] public void Verified_available_licence_is_not_a_preservation_acknowledgement()
    {Assert.False(Require(Fixture()));Assert.False(Require(Fixture(direct:true)));}
    [Theory][InlineData("location")][InlineData("capacity")][InlineData("capacity-missing")][InlineData("suspended")][InlineData("sku-missing")][InlineData("plans-null")][InlineData("plan-unknown")][InlineData("plan-pending")][InlineData("assignment-unknown")][InlineData("assignment-error")][InlineData("details-missing")][InlineData("duplicate-sku")]
    public void Unknown_or_unavailable_entitlement_is_blocked(string fault)
    {
        var data=Fixture();string[]? disabled=null;
        switch(fault)
        {
            case "location":data["user"]!.AsObject().Remove("usageLocation");break;
            case "capacity":data["subscribedSkus"]![0]!["consumedUnits"]=3;break;
            case "capacity-missing":data["subscribedSkus"]![0]!.AsObject().Remove("consumedUnits");break;
            case "suspended":data["subscribedSkus"]![0]!["capabilityStatus"]="Suspended";break;
            case "sku-missing":data["subscribedSkus"]=new JsonArray();break;
            case "plans-null":data["subscribedSkus"]![0]!["servicePlans"]=null;break;
            case "plan-unknown":disabled=[Group];break;
            case "plan-pending":data["subscribedSkus"]![0]!["servicePlans"]![0]!["provisioningStatus"]="PendingActivation";break;
            case "assignment-unknown":data["user"]!["licenseAssignmentStates"]=null;break;
            case "assignment-error":data=Fixture(direct:true);data["user"]!["licenseAssignmentStates"]![0]!["state"]="ActiveWithError";break;
            case "details-missing":data=Fixture(direct:true);data["licenseDetails"]=new JsonArray();break;
            case "duplicate-sku":data["subscribedSkus"]!.AsArray().Add(data["subscribedSkus"]![0]!.DeepClone());break;
        }
        Assert.ThrowsAny<ToolkitException>(()=>Require(data,disabled:disabled));
    }
    [Fact] public void Removal_cannot_claim_to_remove_an_inherited_assignment()
    {Assert.ThrowsAny<ToolkitException>(()=>Require(Fixture(inherited:true),"remove-license"));Assert.True(Require(Fixture(direct:true),"remove-license"));}
    [Fact] public void Newly_disabling_an_existing_service_requires_plan_bound_preservation()
    {Assert.True(Require(Fixture(direct:true),disabled:[Plan]));}
    [Fact] public void Existing_user_assignment_does_not_consume_a_second_seat()
    {var data=Fixture(direct:true);data["subscribedSkus"]![0]!["consumedUnits"]=3;Assert.False(Require(data));}
    [Fact] public void Licence_bearing_group_removal_requires_preservation_and_unknown_licensing_blocks()
    {Assert.True(Require(Fixture(direct:true),"remove-member"));var data=Fixture();data["group"]!["assignedLicenses"]=null;Assert.ThrowsAny<ToolkitException>(()=>Require(data,"add-member"));}
}
