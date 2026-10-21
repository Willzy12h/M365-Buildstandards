using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Safety;
namespace BuildStandard.Engine.Execution;

/// <summary>Readback of one exact action, not proof of full offboarding, effective access or instant service propagation.</summary>
internal static class LifecycleObservedEffect
{
    internal static bool Verified(LifecycleOperation operation,JsonObject before,JsonObject after,JsonObject response)
    {
        try
        {
            _=LifecycleActionRegistry.RequireRegistered(operation);
            if(!True(before,"complete") || !True(after,"complete") || !Same(Text(before,"tenantId"),Text(after,"tenantId")))return false;
            var user=after["user"] as JsonObject;var original=before["user"] as JsonObject;
            if(user is null || original is null || !Same(Text(user,"id"),operation.UserId) || !Same(Text(original,"id"),operation.UserId))return false;
            switch(operation.ActionId)
            {
                case "enable-account":case "disable-account":
                    return user["accountEnabled"] is JsonValue value && value.TryGetValue<bool>(out var enabled) && enabled==(operation.ActionId=="enable-account");
                case "revoke-sessions":
                    return True(response,"value") && Timestamps.TryParse(Text(original,"signInSessionsValidFromDateTime"),out var was)
                        && Timestamps.TryParse(Text(user,"signInSessionsValidFromDateTime"),out var now) && now>was;
                case "add-member":case "remove-member":
                    if(after["group"] is not JsonObject group || !Same(Text(group,"id"),operation.GroupId) || after["members"] is not JsonArray members)return false;
                    var ids=UniqueIds(members,"id");return ids.Contains(operation.UserId)==(operation.ActionId=="add-member");
                case "assign-license":case "remove-license":
                    if(original["licenseAssignmentStates"] is not JsonArray oldStates || user["licenseAssignmentStates"] is not JsonArray states || user["assignedLicenses"] is not JsonArray assigned)return false;
                    var sku=operation.SkuId!;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var current=Rows(states).ToArray();var old=Rows(oldStates).ToArray();
                    foreach(var state in current)
                    {
                        var stateSku=Id(state,"skuId");if(!state.ContainsKey("assignedByGroup"))return false;
                        var source=state["assignedByGroup"] is null?"direct":Id(state,"assignedByGroup");
                        if(!seen.Add(stateSku+":"+source) || Text(state,"state")!="Active" || !state.ContainsKey("error") || state["error"] is not null && Text(state,"error")!="None")return false;
                        _=PlanIds(state);
                    }
                    bool Target(JsonObject state)=>Same(Text(state,"skuId"),sku) && state.ContainsKey("assignedByGroup") && state["assignedByGroup"] is null;
                    // Every inherited and unrelated assignment must remain exactly observed; never claim them removed.
                    if(!Stable(old.Where(row=>!Target(row))).SequenceEqual(Stable(current.Where(row=>!Target(row))),StringComparer.Ordinal))return false;
                    var selected=current.Where(Target).ToArray();var aggregate=UniqueIds(assigned,"skuId");
                    if(operation.ActionId=="remove-license")return selected.Length==0 && aggregate.Contains(sku)==current.Any(row=>Same(Text(row,"skuId"),sku));
                    var expected=JsonNode.Parse(operation.PayloadJson!)!["addLicenses"]![0]!.AsObject();
                    return selected.Length==1 && aggregate.Contains(sku) && PlanIds(selected[0]).SetEquals(PlanIds(expected));
                default:return false;
            }
        }
        catch(Exception error)when(error is ToolkitException or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {return false;}
    }
    private static string[] Stable(IEnumerable<JsonObject> rows)=>rows.Select(CanonicalJson.Serialize).Order(StringComparer.Ordinal).ToArray();
    private static HashSet<string> PlanIds(JsonObject row)
    {if(row["disabledPlans"] is not JsonArray plans)throw Bad();var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var value in plans){if(value is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || !Guid.TryParseExact(text,"D",out var guid) || guid==Guid.Empty || !ids.Add(guid.ToString("D")))throw Bad();}return ids;}
    private static IEnumerable<JsonObject> Rows(JsonArray rows)
    {foreach(var value in rows)yield return value as JsonObject??throw Bad();}
    private static HashSet<string> UniqueIds(JsonArray rows,string key)
    {var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var row in Rows(rows))if(!ids.Add(Id(row,key)))throw Bad();return ids;}
    private static string Id(JsonObject row,string key)
    {if(!Guid.TryParseExact(Text(row,key),"D",out var id)||id==Guid.Empty)throw Bad();return id.ToString("D");}
    private static string? Text(JsonObject row,string key)=>row[key] is JsonValue value && value.TryGetValue<string>(out var text)?text:null;
    private static bool True(JsonObject row,string key)=>row[key] is JsonValue value && value.TryGetValue<bool>(out var boolean) && boolean;
    private static bool Same(string? a,string? b)=>a is not null && b is not null && string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    private static SafetyViolationException Bad()=>new("Readback contains unknown, missing or duplicate identities.");
}
