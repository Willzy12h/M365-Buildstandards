using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Safety;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleObservedEffectTests
{
    private static bool Check(string action,JsonObject before,JsonObject after,JsonObject? reply=null)
    {
        var type=typeof(BuildStandard.Engine.Execution.ReviewedChangeService).Assembly.GetType("BuildStandard.Engine.Execution.LifecycleObservedEffect");Assert.NotNull(type);
        var method=type.GetMethod("Verified",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(method);
        var operation=LifecycleActionRegistry.Create(action,TestData.Office,action.EndsWith("member",StringComparison.Ordinal)?TestData.Mam:null,action.EndsWith("license",StringComparison.Ordinal)?TestData.ClientId:null);
        return (bool)method.Invoke(null,new object[]{operation,before,after,reply??new JsonObject()})!;
    }
    private static JsonObject State()=>new() { ["complete"]=true,["tenantId"]=TestData.TenantA,["user"]=new JsonObject { ["id"]=TestData.Office,["accountEnabled"]=true,["signInSessionsValidFromDateTime"]="2026-10-21T09:00:00Z",["assignedLicenses"]=new JsonArray(),["licenseAssignmentStates"]=new JsonArray() },["group"]=new JsonObject { ["id"]=TestData.Mam },["members"]=new JsonArray() };
    [Theory][InlineData(false)][InlineData(true)]public void Account_effect_requires_actual_bool_matching_reviewed_value(bool enable)
    {var before=State();before["user"]!["accountEnabled"]=!enable;var after=before.DeepClone().AsObject();after["user"]!["accountEnabled"]=enable;Assert.True(Check(enable?"enable-account":"disable-account",before,after));after["user"]!.AsObject().Remove("accountEnabled");Assert.False(Check(enable?"enable-account":"disable-account",before,after));}
    [Fact]public void Direct_membership_requires_complete_exact_group_identity_and_observed_presence()
    {var before=State();var after=State();after["members"]!.AsArray().Add(new JsonObject { ["id"]=TestData.Office });Assert.True(Check("add-member",before,after));Assert.True(Check("remove-member",after,before));after["group"]!["id"]=TestData.Emergency;Assert.False(Check("add-member",before,after));}
    [Theory][InlineData("partial")][InlineData("tenant")][InlineData("target")][InlineData("null")]
    public void Missing_or_wrong_context_never_claims_observed_protection(string fault)
    {var before=State();var after=State();after["user"]!["accountEnabled"]=false;if(fault=="partial")after["complete"]=false;if(fault=="tenant")after["tenantId"]=TestData.TenantB;if(fault=="target")after["user"]!["id"]=TestData.Emergency;if(fault=="null")after["user"]!["accountEnabled"]=null;Assert.False(Check("disable-account",before,after));}
    [Theory][InlineData(true,true)][InlineData(true,false)][InlineData(false,true)]public void Revoke_needs_both_accepted_boolean_and_observed_stamp_advancement(bool accepted,bool advanced)
    {var before=State();var after=State();if(advanced)after["user"]!["signInSessionsValidFromDateTime"]="2026-10-21T09:01:00Z";Assert.Equal(accepted&&advanced,Check("revoke-sessions",before,after,new JsonObject { ["value"]=accepted }));}
    [Fact]public void Removing_direct_assignment_preserves_and_identifies_inherited_entitlement()
    {var before=State();before["user"]!["licenseAssignmentStates"]!.AsArray().Add(new JsonObject { ["skuId"]=TestData.ClientId,["assignedByGroup"]=null,["disabledPlans"]=new JsonArray(),["state"]="Active",["error"]="None" });var after=State();after["user"]!["assignedLicenses"]!.AsArray().Add(new JsonObject { ["skuId"]=TestData.ClientId,["disabledPlans"]=new JsonArray() });after["user"]!["licenseAssignmentStates"]!.AsArray().Add(new JsonObject { ["skuId"]=TestData.ClientId,["assignedByGroup"]=TestData.Mam,["disabledPlans"]=new JsonArray(),["state"]="Active",["error"]="None" });before["user"]!["licenseAssignmentStates"]!.AsArray().Add(after["user"]!["licenseAssignmentStates"]![0]!.DeepClone());Assert.True(Check("remove-license",before,after));after["user"]!["licenseAssignmentStates"]=null;Assert.False(Check("remove-license",before,after));}
    [Fact]public void Direct_licence_add_requires_exact_plans_active_state_and_unchanged_unrelated_assignments()
    {var before=State();var after=State();after["user"]!["assignedLicenses"]!.AsArray().Add(new JsonObject { ["skuId"]=TestData.ClientId,["disabledPlans"]=new JsonArray() });after["user"]!["licenseAssignmentStates"]!.AsArray().Add(new JsonObject { ["skuId"]=TestData.ClientId,["assignedByGroup"]=null,["disabledPlans"]=new JsonArray(),["state"]="Active",["error"]="None" });Assert.True(Check("assign-license",before,after));after["user"]!["licenseAssignmentStates"]![0]!["state"]="ActiveWithError";Assert.False(Check("assign-license",before,after));}
    [Fact]public void Missing_tenant_binding_cannot_be_verified_even_if_both_documents_omit_it()
    {var before=State();var after=State();before.Remove("tenantId");after.Remove("tenantId");after["user"]!["accountEnabled"]=false;Assert.False(Check("disable-account",before,after));}

}
