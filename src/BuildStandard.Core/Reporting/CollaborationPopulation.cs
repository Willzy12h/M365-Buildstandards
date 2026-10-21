using System.Text.Json.Nodes;
namespace BuildStandard.Core.Reporting;
/// <summary>Only demonstrable returned population facts; absence of proof never establishes effective rights.</summary>
public static class CollaborationPopulation
{
    public static bool OwnersUnavailable(JsonObject value)
    {
        if (Flag(value,"onPremisesSyncEnabled") == true) return true;
        return value["groupTypes"] is JsonArray types && types.All(item=>item is JsonValue v && v.TryGetValue<string>(out _))
            && !types.Any(item=>item!.GetValue<string>()=="Unified") && Flag(value,"mailEnabled")==true && Flag(value,"securityEnabled")==false;
    }
    public static bool? HasTeam(JsonObject value)
    {
        if (value["resourceProvisioningOptions"] is not JsonArray options || options.Count>1) return null;
        if (options.Count==0) return false;
        return options[0] is JsonValue v && v.TryGetValue<string>(out var option) && option=="Team" ? true : null;
    }
    private static bool? Flag(JsonObject value,string key)=>value[key] is JsonValue v&&v.TryGetValue<bool>(out var flag)?flag:null;
}
