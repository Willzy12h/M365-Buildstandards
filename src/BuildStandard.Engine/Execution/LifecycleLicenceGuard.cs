using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Safety;
namespace BuildStandard.Engine.Execution;

/// <summary>INT-137 licence readiness over owned complete before-evidence. Returns whether exact preservation review is required;
/// it never records that acknowledgement or grants approval. Missing/unknown state is refused, not guessed from product names.</summary>
internal static class LifecycleLicenceGuard
{
    internal static bool Require(LifecycleOperation operation,JsonObject observed)
    {
        _=LifecycleActionRegistry.RequireRegistered(operation);
        if(operation.ActionId is not ("assign-license" or "remove-license" or "add-member" or "remove-member"))return false;
        var user=Object(observed,"user");
        var assigned=Assignments(Array(user,"assignedLicenses"));
        var states=Array(user,"licenseAssignmentStates");
        var direct=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var stateKeys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in Rows(states))
        {
            var sku=Id(row,"skuId"); var plans=Ids(Array(row,"disabledPlans"));
            if(!row.ContainsKey("assignedByGroup"))throw Refuse("Licence assignment source is unknown.");
            var group=row["assignedByGroup"] is null?null:Id(row,"assignedByGroup");
            if(!stateKeys.Add(sku+":"+group) || !assigned.ContainsKey(sku))throw Refuse("Licence assignment state is duplicated or inconsistent with observed entitlements.");
            if(Text(row,"state")!="Active" || !row.ContainsKey("error") || row["error"] is not null && Text(row,"error")!="None")
                throw Refuse("An assignment is pending, failed or unclassified. Reconcile it before another licence-affecting change.");
            if(group is null)direct.Add(sku,plans);
        }
        foreach(var sku in assigned.Keys)
            if(!stateKeys.Any(key=>key.StartsWith(sku+":",StringComparison.OrdinalIgnoreCase)))throw Refuse("A current entitlement lacks assignment-state evidence.");
        var catalog=Unique(Array(observed,"subscribedSkus"),"skuId");
        var details=Unique(Array(observed,"licenseDetails"),"skuId");
        if(!assigned.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(details.Keys))throw Refuse("User licence detail inventory is incomplete or inconsistent.");
        foreach(var pair in details)
        {
            var plans=Unique(Array(pair.Value,"servicePlans"),"servicePlanId");
            if(plans.Count==0)throw Refuse("User service-plan status is unknown.");
            foreach(var plan in plans.Values)
                if(Text(plan,"provisioningStatus") is not ("Success" or "Disabled"))throw Refuse("User service-plan provisioning is pending, failed or unknown.");
        }
        if(operation.ActionId=="remove-license")
        {
            if(!direct.ContainsKey(operation.SkuId!))throw Refuse("No exact direct assignment exists. Inherited entitlement cannot be removed with a direct-licence action.");
            _=RequireSku(operation.SkuId!,null);
            return true;
        }
        if(operation.ActionId=="assign-license")
        {
            var payload=JsonNode.Parse(operation.PayloadJson!)!.AsObject();
            var proposed=Ids(payload["addLicenses"]![0]!["disabledPlans"]!.AsArray());
            _=RequireSku(operation.SkuId!,proposed);
            // Adding a parallel direct assignment never removes inherited entitlement. Existing direct service withdrawal needs review.
            return direct.TryGetValue(operation.SkuId!,out var before) && proposed.Except(before,StringComparer.OrdinalIgnoreCase).Any();
        }
        var groupLicences=Assignments(Array(Object(observed,"group"),"assignedLicenses"));
        foreach(var pair in groupLicences)_=RequireSku(pair.Key,operation.ActionId=="add-member"?pair.Value:null);
        return operation.ActionId=="remove-member" && groupLicences.Count>0;

        JsonObject RequireSku(string id,HashSet<string>? proposed)
        {
            if(!catalog.TryGetValue(id,out var sku))throw Refuse("The selected SKU is absent from complete subscribed-SKU evidence.");
            var knownPlans=Unique(Array(sku,"servicePlans"),"servicePlanId");
            if(knownPlans.Count==0)throw Refuse("SKU service-plan compatibility is unknown.");
            if(proposed is null)return sku; // removal cannot require purchasing capacity, but still needs an identified SKU.
            var location=Text(user,"usageLocation");
            if(location.Length!=2 || location.Any(c=>c is <'A' or >'Z'))throw Refuse("A returned two-letter usage location is required; geographic service availability remains subject to Microsoft validation.");
            if(Text(sku,"capabilityStatus")!="Enabled")throw Refuse("The selected SKU is suspended, disabled or unavailable.");
            var prepaid=Object(sku,"prepaidUnits");var capacity=Number(prepaid,"enabled");_=Number(prepaid,"suspended");_=Number(prepaid,"warning");var consumed=Number(sku,"consumedUnits");
            if(!assigned.ContainsKey(id) && consumed>=capacity)throw Refuse("No verified available seat remains. The tool does not purchase licences.");
            if(proposed.Any(plan=>!knownPlans.ContainsKey(plan)))throw Refuse("A proposed disabled service plan is not part of the selected SKU.");
            foreach(var plan in knownPlans.Values)
                if(Text(plan,"provisioningStatus")!="Success" || Text(plan,"appliesTo") is not ("User" or "Company"))throw Refuse("SKU service availability is pending, failed or unclassified.");
            return sku;
        }
    }
    private static Dictionary<string,HashSet<string>> Assignments(JsonArray rows)
    {var result=new Dictionary<string,HashSet<string>>(StringComparer.OrdinalIgnoreCase);foreach(var row in Rows(rows))if(!result.TryAdd(Id(row,"skuId"),Ids(Array(row,"disabledPlans"))))throw Refuse("Duplicate licence assignment.");return result;}
    private static Dictionary<string,JsonObject> Unique(JsonArray rows,string key)
    {var result=new Dictionary<string,JsonObject>(StringComparer.OrdinalIgnoreCase);foreach(var row in Rows(rows))if(!result.TryAdd(Id(row,key),row))throw Refuse("Duplicate licence identity.");return result;}
    private static HashSet<string> Ids(JsonArray array)
    {var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var value in array){var text=value is JsonValue scalar && scalar.TryGetValue<string>(out var id)?id:null;if(!Guid.TryParseExact(text,"D",out var guid)||guid==Guid.Empty||!result.Add(guid.ToString("D")))throw Refuse("Missing or duplicate service-plan identity.");}return result;}
    private static IEnumerable<JsonObject> Rows(JsonArray rows)
    {foreach(var row in rows)yield return row as JsonObject??throw Refuse("Malformed licence inventory row.");}
    private static string Id(JsonObject row,string key)
    {var text=Text(row,key);if(!Guid.TryParseExact(text,"D",out var guid)||guid==Guid.Empty)throw Refuse("Missing exact licence identity: "+key);return guid.ToString("D");}
    private static string Text(JsonObject row,string key)
    {if(row[key] is not JsonValue scalar||!scalar.TryGetValue<string>(out var text)||string.IsNullOrWhiteSpace(text)||text.Length>1024)throw Refuse("Required licence value is unknown: "+key);return text;}
    private static int Number(JsonObject row,string key)
    {if(row[key] is not JsonValue scalar||!scalar.TryGetValue<int>(out var number)||number<0)throw Refuse("Required licence capacity is unknown: "+key);return number;}
    private static JsonArray Array(JsonObject row,string key)=>row[key] as JsonArray??throw Refuse("Required licence collection is missing: "+key);
    private static JsonObject Object(JsonObject row,string key)=>row[key] as JsonObject??throw Refuse("Required licence object is missing: "+key);
    private static SafetyViolationException Refuse(string reason)=>new(reason);
}
