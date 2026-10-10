using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>The Graph operation is a full PUT, not a one-property PATCH.</summary>
public static class EntraLapsSafety
{
    public const string Path = "/policies/deviceRegistrationPolicy";
    public const string WriteScope = "Policy.ReadWrite.DeviceConfiguration";
    private static readonly string[] Mutable = { "userDeviceQuota", "multiFactorAuthConfiguration", "azureADRegistration", "azureADJoin", "localAdminPassword" };
    private static readonly string[] Metadata = { "id", "displayName", "description", "@odata.context", "@odata.type", "@odata.etag" };

    public static JsonObject WritableState(JsonObject source)
    {
        if (source.Any(p => !Mutable.Contains(p.Key, StringComparer.Ordinal) && !Metadata.Contains(p.Key, StringComparer.Ordinal)))
            throw new SafetyViolationException("Device registration policy contains an unsupported property. Review the Graph schema before a full replacement.");
        if (source["userDeviceQuota"] is not JsonValue quota || !quota.TryGetValue<int>(out var n) || n < 0
            || source["multiFactorAuthConfiguration"]?.ToString() is not ("required" or "notRequired")
            || source["azureADRegistration"] is not JsonObject registration || !Boolean(registration["isAdminConfigurable"]) || !Membership(registration["allowedToRegister"])
            || source["azureADJoin"] is not JsonObject join || !Boolean(join["isAdminConfigurable"]) || !Membership(join["allowedToJoin"])
            || join["localAdmins"] is not JsonObject admins || !Boolean(admins["enableGlobalAdmins"]) || !Membership(admins["registeringUsers"])
            || source["localAdminPassword"] is not JsonObject laps || laps["isEnabled"] is not JsonValue enabled || !enabled.TryGetValue<bool>(out _))
            throw new SafetyViolationException("Device registration policy is incomplete or unsupported. Refusing to reset missing settings during LAPS enablement.");
        var result = new JsonObject();
        foreach (var key in Mutable) result[key] = source[key]!.DeepClone();
        return result;
    }

    public static bool IsEnabled(JsonObject source) => WritableState(source)["localAdminPassword"]!["isEnabled"]!.GetValue<bool>();

    public static JsonObject EnablePayload(JsonObject source)
    {
        var result = WritableState(source);
        result["localAdminPassword"]!["isEnabled"] = true;
        return result;
    }

    public static bool SameState(JsonObject a, JsonObject b) =>
        CanonicalJson.Sha256(WritableState(a)) == CanonicalJson.Sha256(WritableState(b));

    private static bool Boolean(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out _);

    private static bool Membership(JsonNode? node)
    {
        if (node is not JsonObject membership) return false;
        var type = membership["@odata.type"]?.ToString().TrimStart('#');
        if (type is "microsoft.graph.allDeviceRegistrationMembership" or "microsoft.graph.noDeviceRegistrationMembership") return true;
        return type == "microsoft.graph.enumeratedDeviceRegistrationMembership"
            && membership["users"] is JsonArray users && membership["groups"] is JsonArray groups
            && users.Concat(groups).All(v => v is JsonValue id && id.TryGetValue<string>(out var text) && ProfileValidator.IsGuid(text));
    }
}
