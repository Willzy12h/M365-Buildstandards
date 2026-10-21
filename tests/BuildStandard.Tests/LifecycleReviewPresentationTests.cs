using System.Reflection;
using System.Text.Json.Nodes;
using BuildStandard.Engine.Execution;
using Xunit;
namespace BuildStandard.Tests;
public sealed class LifecycleReviewPresentationTests
{
    [Theory][InlineData("remove-member")][InlineData("assign-license")][InlineData("disable-account")]
    public void Owned_review_exposes_original_values_entitlement_sources_and_selected_access_without_mutation(string action)
    {
        var before=new JsonObject
        {
            ["user"]=new JsonObject { ["id"]=TestData.Office,["displayName"]="Synthetic target",["accountEnabled"]=true,
                ["assignedLicenses"]=new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["disabledPlans"]=new JsonArray(TestData.Emergency) }),
                ["licenseAssignmentStates"]=new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["assignedByGroup"]=TestData.Mam,["state"]="Active" }) },
            ["group"]=new JsonObject { ["id"]=TestData.Mam,["displayName"]="Synthetic licensed group",["assignedLicenses"]=new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["disabledPlans"]=new JsonArray(TestData.Emergency) }) },
            ["members"]=new JsonArray(new JsonObject { ["id"]=TestData.Office }),
            ["subscribedSkus"]=new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["skuPartNumber"]="SYNTHETIC_SKU" }),
            ["licenseDetails"]=new JsonArray(new JsonObject { ["skuId"]=TestData.ClientId,["servicePlans"]=new JsonArray(new JsonObject { ["servicePlanId"]=TestData.Emergency,["provisioningStatus"]="Disabled" }) })
        };
        var operation=BuildStandard.Core.Safety.LifecycleActionRegistry.Create(action,TestData.Office,action=="remove-member"?TestData.Mam:null,action=="assign-license"?TestData.ClientId:null);
        var original=new JsonObject { ["before"]=before,["operation"]=BuildStandard.Core.Json.ToolkitJson.ToNode(operation),["permissionMode"]="Deployment" };
        var preview=new LifecyclePreview(original,()=>{});var frozen=preview.GetDocument().ToJsonString();
        var property=typeof(LifecyclePreview).GetProperty("ReviewDetails",BindingFlags.Public|BindingFlags.Instance);Assert.NotNull(property);
        var text=(string)property.GetValue(preview)!;
        Assert.Contains("Synthetic target",text);Assert.Contains(TestData.Office,text);Assert.Contains(operation.WriteScope,text);Assert.Contains("Deployment",text);
        Assert.Contains("accountEnabled",text);Assert.Contains("true",text);Assert.Contains("assignedByGroup",text);Assert.Contains(TestData.Mam,text);Assert.Contains("Disabled",text);Assert.Contains(TestData.Emergency,text);
        if(action=="remove-member"){Assert.Contains("Synthetic licensed group",text);Assert.Contains("assignedLicenses",text);Assert.Contains("disabledPlans",text);Assert.Contains("propagation",text,StringComparison.OrdinalIgnoreCase);}
        Assert.Equal(frozen,preview.GetDocument().ToJsonString());
        before["user"]!["displayName"]="changed caller data";Assert.Equal(text,property.GetValue(preview));
    }
}
