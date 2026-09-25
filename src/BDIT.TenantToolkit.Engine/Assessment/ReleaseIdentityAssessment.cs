using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Assessment;

internal static class ReleaseIdentityAssessment
{
    public static bool Apply(ControlDefinition control, TenantSnapshot snapshot, ControlFinding finding)
    {
        if (control.Id is not ("ID-004" or "ID-005" or "ID-006" or "ID-007" or "ID-008" or "UPD-001")) return false;
        if (control.Id == "UPD-001")
        {
            finding.Status = FindingStatus.RequiresManualReview;
            foreach (var key in new[] { "configuration", "featureUpdates" })
            {
                if (!snapshot.Collections.TryGetValue(key, out var updates) || !updates.Usable)
                {
                    finding.Status = FindingStatus.UnableToAssess;
                    finding.Notes.Add(key + " evidence unavailable; update conflicts are unknown.");
                    continue;
                }
                var policies = updates.Items.Where(o => key == "featureUpdates" || o["@odata.type"]?.ToString() == "#microsoft.graph.windowsUpdateForBusinessConfiguration").ToList();
                finding.Notes.Add(key + ": " + policies.Count + " policies captured. Review each assigned population and management authority for conflicts.");
                finding.ObservedObjects.AddRange(policies.Select(p => p["displayName"] + " [" + p["id"] + "]; assignments: " + (p["_assignments"]?.ToJsonString() ?? "unknown")));
            }
            finding.Reason = "Autopatch processor configuration, Windows licence verification and every eligible device require manual reconciliation. Captured update policies cannot establish those prerequisites.";
            return true;
        }
        if (!snapshot.Collections.TryGetValue(control.Collection!, out var capture) || !capture.Usable)
        {
            finding.Status = FindingStatus.UnableToAssess; finding.Reason = "Required identity evidence is unavailable or incomplete; absence is not inferred."; return true;
        }
        var items = control.Id == "ID-004" ? capture.Items.Where(i => i["id"]?.ToString() == "FIDO2").ToList() : capture.Items;
        if (items.Count != 1)
        {
            finding.Status = FindingStatus.UnableToAssess; finding.Reason = "Expected one complete identity policy; missing or duplicate evidence cannot establish its state."; return true;
        }
        var obj = items[0]; bool? match = null;
        switch (control.Id)
        {
            case "ID-004":
                if (snapshot.Collections.TryGetValue("passkeyProfiles", out var profiles) && profiles.Usable && profiles.Items.Count == 0
                    && Text(obj["state"]) is "enabled" or "disabled" && obj["keyRestrictions"] is JsonObject restrictions
                    && restrictions["isEnforced"] is JsonValue v && v.TryGetValue<bool>(out var enforced)
                    && restrictions["aaGuids"] is JsonArray keys && restrictions["enforcementType"]?.ToString() is "allow" or "block"
                    && keys.All(k => Text(k) is { } key && Guid.TryParse(key, out _))
                    && obj["passkeyProfiles"] is null or JsonArray { Count: 0 } && obj["defaultPasskeyProfile"] is null)
                {
                    var ids = keys.Select(k => k?.ToString()).ToList();
                    var allowed = !enforced || (restrictions["enforcementType"]!.ToString() == "allow"
                        ? ReleaseIdentityChanges.AuthenticatorAaguids.All(a => ids.Contains(a, StringComparer.OrdinalIgnoreCase))
                        : ReleaseIdentityChanges.AuthenticatorAaguids.All(a => !ids.Contains(a, StringComparer.OrdinalIgnoreCase)));
                    match = obj["state"]!.ToString() == "enabled" && allowed;
                }
                finding.Notes.Add("Review included/excluded users, attestation and any passkey profiles; registration and Authenticator use are not verified."); break;
            case "ID-005":
                if (Text(Value(obj, "systemCredentialPreferences", "state")) is { } preferred && preferred is "enabled" or "disabled" or "default") match = preferred == "enabled";
                finding.Notes.Add("Review included/excluded users and a user's actual MFA experience."); break;
            case "ID-006":
                if (Value(obj, "registrationEnforcement", "authenticationMethodsRegistrationCampaign") is JsonObject campaign
                    && Text(campaign["state"]) is "enabled" or "disabled" or "default" && campaign["includeTargets"] is JsonArray targets
                    && targets.All(t => t is JsonObject target && !string.IsNullOrWhiteSpace(Text(target["id"])) && !string.IsNullOrWhiteSpace(Text(target["targetedAuthenticationMethod"]))))
                    match = Text(campaign["state"]) == "enabled" && targets.OfType<JsonObject>().Any(t => Text(t["id"]) == "all_users" && Text(t["targetedAuthenticationMethod"]) == "microsoftAuthenticator");
                finding.Notes.Add("Review excluded users and completed registration; campaign configuration does not prove enrolment."); break;
            case "ID-007":
                if (Value(obj, "defaultUserRolePermissions", "permissionGrantPoliciesAssigned") is JsonArray grants
                    && grants.All(g => g is JsonValue value && value.TryGetValue<string>(out _)))
                    match = !grants.Any(g => g!.ToString().StartsWith("managePermissionGrantsForSelf.", StringComparison.Ordinal));
                break;
            case "ID-008":
                if (obj["isEnabled"] is JsonValue enabled && enabled.TryGetValue<bool>(out var flag) && obj["reviewers"] is JsonArray reviewers
                    && reviewers.All(r => r is JsonObject reviewer && !string.IsNullOrWhiteSpace(Text(reviewer["query"])) && Text(reviewer["queryType"]) == "MicrosoftGraph"))
                    match = flag && reviewers.Count > 0;
                finding.Notes.Add("Compare the reviewers with client-approved users and test a request manually; presence is not proof of correct reviewers."); break;
        }
        finding.Status = match is null ? FindingStatus.UnableToAssess : match.Value ? FindingStatus.RequiresManualReview : FindingStatus.PartialMatch;
        finding.Reason = match is null ? "Required properties or passkey-profile evidence are incomplete. Check the policy manually; no missing-state claim."
            : match.Value ? "The captured settings match the configured target. Complete the stated user-scope and behaviour checks."
            : "The captured settings differ from the standard; preview the supported reviewed action or follow the manual steps.";
        return true;
    }

    // A successful collection does not make a malformed nested value reliable evidence.
    private static string? Text(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : null;

    private static JsonNode? Value(JsonObject parent, params string[] path)
    {
        JsonNode? current = parent;
        foreach (var part in path) current = (current as JsonObject)?[part];
        return current;
    }
}
