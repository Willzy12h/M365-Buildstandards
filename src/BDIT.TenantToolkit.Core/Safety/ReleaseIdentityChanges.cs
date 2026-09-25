using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>Documented .12 identity settings; every payload is rebuilt from the complete reviewed before state.</summary>
public static class ReleaseIdentityChanges
{
    public const string ProvisioningAppId = "f1346770-5b25-470b-88bd-d5744ab7952c";
    public const string ProvisioningQuery = "/servicePrincipals?$filter=appId eq '" + ProvisioningAppId + "'&$select=id,appId,displayName";
    public static readonly string[] AuthenticatorAaguids = { "de1e552d-db1d-4423-a619-566b625cdc84", "90a3ccdf-635c-4729-a248-9b709135078f" };
    public static bool Supports(ReviewedChangeKind kind) => kind is ReviewedChangeKind.EnablePasskeys or ReviewedChangeKind.EnableSystemPreferredMfa
        or ReviewedChangeKind.EnableRegistrationCampaign or ReviewedChangeKind.DisableUserConsent or ReviewedChangeKind.EnableAdminConsent or ReviewedChangeKind.SetProvisioningOwner;

    public static (string Control, GraphApi Api, string Path, string Method, string Scope) Route(ReviewedChangeKind kind, string objectId) => kind switch
    {
        ReviewedChangeKind.EnablePasskeys => ("ID-004", GraphApi.V1, ReviewedChangeSafety.AuthenticationPath + "FIDO2", "PATCH", "Policy.ReadWrite.AuthenticationMethod"),
        ReviewedChangeKind.EnableSystemPreferredMfa => ("ID-005", GraphApi.Beta, "/policies/authenticationMethodsPolicy", "PATCH", "Policy.ReadWrite.AuthenticationMethod"),
        ReviewedChangeKind.EnableRegistrationCampaign => ("ID-006", GraphApi.V1, "/policies/authenticationMethodsPolicy", "PATCH", "Policy.ReadWrite.AuthenticationMethod"),
        ReviewedChangeKind.DisableUserConsent => ("ID-007", GraphApi.V1, "/policies/authorizationPolicy", "PATCH", "Policy.ReadWrite.Authorization"),
        ReviewedChangeKind.EnableAdminConsent => ("ID-008", GraphApi.V1, "/policies/adminConsentRequestPolicy", "PUT", "Policy.ReadWrite.ConsentRequest"),
        ReviewedChangeKind.SetProvisioningOwner when ProfileValidator.IsGuid(objectId) => ("PRE-011", GraphApi.V1, "/groups/" + objectId + "/owners/$ref", "POST", "Group.ReadWrite.All"),
        _ => throw new SafetyViolationException("Unsupported release identity action or object ID.")
    };

    public static JsonObject Payload(ReviewedChangePlan p)
    {
        var before = p.Before;
        switch (p.Kind)
        {
            case ReviewedChangeKind.EnablePasskeys:
                if (before["id"]?.ToString() != "FIDO2" || before["state"] is null || before["includeTargets"] is not JsonArray { Count: > 0 }
                    || before["@odata.type"]?.ToString() != "#microsoft.graph.fido2AuthenticationMethodConfiguration")
                    throw new SafetyViolationException("Capture the complete FIDO2 method and review its user targets first.");
                if (before["passkeyProfiles"] is JsonArray { Count: > 0 } || before["defaultPasskeyProfile"] is not null)
                    throw new SafetyViolationException("This tenant uses passkey profiles. Review Authenticator availability in those profiles manually; legacy restrictions must not replace them.");
                var restriction = Object(before, "keyRestrictions");
                if (restriction["isEnforced"] is not JsonValue enforced || !enforced.TryGetValue<bool>(out var active)
                    || restriction["aaGuids"] is not JsonArray aaguids || restriction["enforcementType"]?.ToString() is not ("allow" or "block")
                    || aaguids.Any(a => !ProfileValidator.IsGuid(a?.ToString())))
                    throw new SafetyViolationException("Passkey restriction evidence is incomplete.");
                var keys = (JsonObject)restriction.DeepClone();
                if (active)
                {
                    var ids = aaguids.Select(a => a!.ToString()).ToList();
                    ids = restriction["enforcementType"]!.ToString() == "allow"
                        ? ids.Concat(AuthenticatorAaguids).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : ids.Where(a => !AuthenticatorAaguids.Contains(a, StringComparer.OrdinalIgnoreCase)).ToList();
                    keys["aaGuids"] = new JsonArray(ids.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
                }
                return new JsonObject { ["@odata.type"] = "#microsoft.graph.fido2AuthenticationMethodConfiguration", ["state"] = "enabled", ["keyRestrictions"] = keys };
            case ReviewedChangeKind.EnableSystemPreferredMfa:
                var preferred = Object(before, "systemCredentialPreferences");
                if (preferred["state"] is null || preferred["includeTargets"] is not JsonArray || preferred["excludeTargets"] is not JsonArray)
                    throw new SafetyViolationException("System-preferred MFA targets are incomplete.");
                var preferences = (JsonObject)preferred.DeepClone(); preferences["state"] = "enabled";
                return new JsonObject { ["systemCredentialPreferences"] = preferences };
            case ReviewedChangeKind.EnableRegistrationCampaign:
                var enforcement = Object(before, "registrationEnforcement");
                var campaign = Object(enforcement, "authenticationMethodsRegistrationCampaign");
                if (campaign["state"] is null || campaign["excludeTargets"] is not JsonArray || campaign["snoozeDurationInDays"] is null)
                    throw new SafetyViolationException("Registration campaign evidence is incomplete.");
                var updated = (JsonObject)campaign.DeepClone(); updated["state"] = "enabled";
                updated["includeTargets"] = new JsonArray(new JsonObject { ["id"] = "all_users", ["targetType"] = "group", ["targetedAuthenticationMethod"] = "microsoftAuthenticator" });
                var registration = (JsonObject)enforcement.DeepClone(); registration["authenticationMethodsRegistrationCampaign"] = updated;
                return new JsonObject { ["registrationEnforcement"] = registration };
            case ReviewedChangeKind.DisableUserConsent:
                var permissions = Object(before, "defaultUserRolePermissions");
                if (permissions["permissionGrantPoliciesAssigned"] is not JsonArray grants || grants.Any(g => g is not JsonValue v || !v.TryGetValue<string>(out _)))
                    throw new SafetyViolationException("Capture the current permission-grant policies before changing user consent.");
                var role = (JsonObject)permissions.DeepClone();
                role["permissionGrantPoliciesAssigned"] = new JsonArray(grants.Where(g => !g!.ToString().StartsWith("managePermissionGrantsForSelf.", StringComparison.Ordinal)).Select(g => g!.DeepClone()).ToArray());
                return new JsonObject { ["defaultUserRolePermissions"] = role };
            case ReviewedChangeKind.EnableAdminConsent:
                if (before["notifyReviewers"] is not JsonValue notify || !notify.TryGetValue<bool>(out _)
                    || before["remindersEnabled"] is not JsonValue reminders || !reminders.TryGetValue<bool>(out _)
                    || before["requestDurationInDays"] is not JsonValue days || !days.TryGetValue<int>(out var duration) || duration is < 1 or > 60
                    || before["_reviewers"] is not JsonArray { Count: > 0 } reviewers)
                    throw new SafetyViolationException("Admin consent needs complete current settings and mandatory resolved client reviewers.");
                var targets = new JsonArray(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var reviewer in reviewers)
                {
                    var id = reviewer?["id"]?.ToString();
                    if (!ProfileValidator.IsGuid(id) || !seen.Add(id!) || reviewer?["accountEnabled"]?.ToString() != "true")
                        throw new SafetyViolationException("Each consent reviewer must be a distinct, enabled user resolved in this tenant.");
                    targets.Add(new JsonObject { ["query"] = "/users/" + id, ["queryType"] = "MicrosoftGraph" });
                }
                return new JsonObject { ["isEnabled"] = true, ["notifyReviewers"] = notify.DeepClone(), ["remindersEnabled"] = reminders.DeepClone(),
                    ["requestDurationInDays"] = duration, ["reviewers"] = targets };
            case ReviewedChangeKind.SetProvisioningOwner:
                if (p.ControlId != "PRE-011" || before["id"]?.ToString() != p.ObjectId || before["securityEnabled"]?.ToString() != "true"
                    || before["mailEnabled"]?.ToString() != "false" || before["groupTypes"] is not JsonArray { Count: 0 }
                    || before["_owners"] is not JsonArray || before["_members"] is not JsonArray { Count: 0 } || before["_provisioningClient"] is not JsonObject service
                    || service["appId"]?.ToString() != ProvisioningAppId || !ProfileValidator.IsGuid(service["id"]?.ToString()))
                    throw new SafetyViolationException("Resolve the recorded device preparation security group and exact Microsoft provisioning application first.");
                return new JsonObject { ["@odata.id"] = "https://graph.microsoft.com/v1.0/directoryObjects/" + service["id"] };
            default: throw new SafetyViolationException("Unsupported release identity payload.");
        }
    }

    private static JsonObject Object(JsonObject parent, string key) => parent[key] as JsonObject
        ?? throw new SafetyViolationException("Required before-state is missing: " + key + ".");
}
