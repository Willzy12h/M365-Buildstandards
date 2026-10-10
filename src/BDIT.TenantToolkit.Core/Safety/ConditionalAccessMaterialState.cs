using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;

namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>A bounded CA comparison, not a replacement for the generic payload subset contract.</summary>
public static class ConditionalAccessMaterialState
{
    private static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
    { "id", "displayName", "description", "createdDateTime", "modifiedDateTime", "@odata.context", "@odata.etag", "state" };
    private static readonly HashSet<string> Nullable = new(StringComparer.Ordinal)
    {
        "sessionControls", "grantControls", "grantControls.authenticationStrength",
        "sessionControls.applicationEnforcedRestrictions", "sessionControls.cloudAppSecurity",
        "sessionControls.persistentBrowser", "sessionControls.signInFrequency",
        "conditions.devices", "conditions.locations", "conditions.platforms",
        "conditions.clientApplications", "conditions.authenticationFlows", "conditions.applications.applicationFilter",
        "conditions.users.includeGuestsOrExternalUsers", "conditions.users.excludeGuestsOrExternalUsers"
    };
    private static readonly HashSet<string> EmptyLists = new(StringComparer.Ordinal)
    {
        "conditions.applications.excludeApplications", "conditions.applications.includeUserActions",
        "conditions.applications.includeAuthenticationContextClassReferences", "conditions.users.excludeUsers",
        "conditions.users.excludeGroups", "conditions.users.excludeRoles", "conditions.users.includeUsers",
        "conditions.users.includeGroups", "conditions.users.includeRoles", "conditions.locations.excludeLocations",
        "conditions.platforms.excludePlatforms", "conditions.userRiskLevels", "conditions.signInRiskLevels",
        "conditions.servicePrincipalRiskLevels", "grantControls.customAuthenticationFactors", "grantControls.termsOfUse"
    };

    public static bool HasPolicyDefinition(JsonObject policy) =>
        policy["conditions"] is JsonObject conditions && conditions["users"] is JsonObject
        && conditions["applications"] is JsonObject
        && (policy["grantControls"] is JsonObject || policy["sessionControls"] is JsonObject);

    public static bool Same(JsonObject left, JsonObject right) =>
        CanonicalJson.Sha256(Normalise(left, "")) == CanonicalJson.Sha256(Normalise(right, ""));

    private static JsonNode? Normalise(JsonNode? node, string path)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (key, value) in obj)
            {
                var child = path.Length == 0 ? key : path + "." + key;
                if (path.Length == 0 && Metadata.Contains(key)) continue;
                if (path.Length == 0 && key == "@odata.type" && value?.ToString() == "#microsoft.graph.conditionalAccessPolicy") continue;
                if (value is null && Nullable.Contains(child)) continue;
                if (value is JsonArray { Count: 0 } && EmptyLists.Contains(child)) continue;
                var normalised = Normalise(value, child);
                if (child == "sessionControls" && normalised is JsonObject { Count: 0 }) continue;
                result[key] = normalised;
            }
            return result;
        }
        // Documented scalar sets are order independent. Unknown structures remain exact and fail closed.
        if (node is JsonArray array && (EmptyLists.Contains(path) || path is "conditions.clientAppTypes"
            or "conditions.applications.includeApplications" or "conditions.locations.includeLocations"
            or "conditions.platforms.includePlatforms" or "grantControls.builtInControls")
            && array.All(n => n is JsonValue v && v.TryGetValue<string>(out _)))
            return new JsonArray(array.OrderBy(n => n!.ToString(), StringComparer.Ordinal).Select(n => n!.DeepClone()).ToArray());
        return node?.DeepClone();
    }
}
