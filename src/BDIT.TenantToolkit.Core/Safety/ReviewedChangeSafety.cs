using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

/// <summary>Closed set of consequential operations. Generic candidate writes cannot call these routes.</summary>
public static class ReviewedChangeSafety
{
    public const string AuthenticationPath = "/policies/authenticationMethodsPolicy/authenticationMethodConfigurations/";
    public const string UpdatesPath = "/admin/windows/updates/updatableAssets";
    public static bool IsUpdates(ReviewedChangeKind kind) => kind is ReviewedChangeKind.EnrolFeatureUpdates or ReviewedChangeKind.UnenrolFeatureUpdates or ReviewedChangeKind.EnrolQualityUpdates or ReviewedChangeKind.UnenrolQualityUpdates;
    public static bool IsUnenrol(ReviewedChangeKind kind) => kind is ReviewedChangeKind.UnenrolFeatureUpdates or ReviewedChangeKind.UnenrolQualityUpdates;
    public static string UpdateCategory(ReviewedChangeKind kind) => kind is ReviewedChangeKind.EnrolFeatureUpdates or ReviewedChangeKind.UnenrolFeatureUpdates ? "feature" : "quality";
    public static bool IsObjectAction(ReviewedChangeKind kind) => kind >= ReviewedChangeKind.EnableConditionalAccess && kind <= ReviewedChangeKind.RemoveAssignments;
    public static JsonObject UpdatePayload(ReviewedChangePlan p) => new()
    {
        ["updateCategory"] = UpdateCategory(p.Kind),
        ["assets"] = new JsonArray(p.DeviceIds.Select(id => (JsonNode?)new JsonObject { ["@odata.type"] = "#microsoft.graph.windowsUpdates.azureADDevice", ["id"] = id }).ToArray())
    };
    public static bool IsAssignment(ReviewedChangeKind kind) => kind is ReviewedChangeKind.AssignGroups or ReviewedChangeKind.RemoveAssignments;
    public static string AssignmentType(string root) => root switch
    {
        "/deviceManagement/deviceConfigurations" => "deviceConfigurationAssignment",
        "/deviceManagement/deviceCompliancePolicies" => "deviceCompliancePolicyAssignment",
        "/deviceManagement/configurationPolicies" => "deviceManagementConfigurationPolicyAssignment",
        "/deviceManagement/deviceEnrollmentConfigurations" => "enrollmentConfigurationAssignment",
        "/deviceManagement/windowsAutopilotDeploymentProfiles" => "windowsAutopilotDeploymentProfileAssignment",
        "/deviceAppManagement/mobileApps" => "mobileAppAssignment",
        "/deviceAppManagement/iosManagedAppProtections" or "/deviceAppManagement/androidManagedAppProtections" => "targetedManagedAppPolicyAssignment",
        _ => throw new SafetyViolationException("Assignment is not supported for this collection.")
    };
    public static string AssignmentKey(string root) => root == "/deviceAppManagement/mobileApps" ? "mobileAppAssignments" : "assignments";
    public static JsonObject AssignmentPayload(string root, IEnumerable<string> included, IEnumerable<string> excluded, AssignmentPopulation population = AssignmentPopulation.Groups)
    {
        var assignments = new JsonArray();
        if (!Enum.IsDefined(population)) throw new SafetyViolationException("Unknown assignment population.");
        if (population != AssignmentPopulation.Groups)
        {
            if (included.Any()) throw new SafetyViolationException("Built-in populations cannot be combined with included groups.");
            if (root is not ("/deviceManagement/deviceConfigurations" or "/deviceManagement/deviceCompliancePolicies"
                or "/deviceManagement/configurationPolicies" or "/deviceAppManagement/mobileApps"))
                throw new SafetyViolationException("Use explicitly reviewed groups for this resource; built-in population assignment is unsupported.");
            var row = new JsonObject { ["@odata.type"] = "#microsoft.graph." + AssignmentType(root),
                ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph." + (population == AssignmentPopulation.AllUsers ? "allLicensedUsersAssignmentTarget" : "allDevicesAssignmentTarget") } };
            if (root == "/deviceAppManagement/mobileApps") row["intent"] = "required";
            assignments.Add(row);
        }
        foreach (var (ids, exclusion) in new[] { (included, false), (excluded, true) })
            foreach (var id in ids)
            {
                if (!ProfileValidator.IsGuid(id)) throw new SafetyViolationException("Assignment requires resolved group GUIDs.");
                var row = new JsonObject { ["@odata.type"] = "#microsoft.graph." + AssignmentType(root),
                    ["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph." + (exclusion ? "exclusionGroupAssignmentTarget" : "groupAssignmentTarget"), ["groupId"] = id } };
                if (root == "/deviceAppManagement/mobileApps") row["intent"] = "required";
                assignments.Add(row);
            }
        return new JsonObject { [AssignmentKey(root)] = assignments };
    }

    public static void Assert(ReviewedChangePlan p)
    {
        if (p.Population is { } population && (!Enum.IsDefined(population) || p.Kind != ReviewedChangeKind.AssignGroups))
            throw new SafetyViolationException("An assignment population applies only to a reviewed assignment.");
        if (!Enum.IsDefined(p.Kind)) throw new SafetyViolationException("Unknown reviewed change.");
        if (!IsAssignment(p.Kind) && (p.ExcludeGroups.Count > 0 || p.Kind != ReviewedChangeKind.ConfigureTap && p.IncludeGroups.Count > 0))
            throw new SafetyViolationException("This action does not accept group targeting. Existing targets are preserved unless the preview explicitly replaces them.");
        if (!IsUpdates(p.Kind) && p.DeviceIds.Count > 0) throw new SafetyViolationException("Device IDs apply only to Autopatch actions.");
        if (p.IncludeGroups.Concat(p.ExcludeGroups).Any(g => !ProfileValidator.IsGuid(g))
            || p.IncludeGroups.Intersect(p.ExcludeGroups, StringComparer.OrdinalIgnoreCase).Any()
            || p.IncludeGroups.Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.IncludeGroups.Count
            || p.ExcludeGroups.Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.ExcludeGroups.Count)
            throw new SafetyViolationException("Invalid, duplicate or overlapping group targets.");
        string path, scope; var api = GraphApi.V1; var method = "PATCH";
        JsonObject expected;
        switch (p.Kind)
        {
            case ReviewedChangeKind.EnrolFeatureUpdates:
            case ReviewedChangeKind.UnenrolFeatureUpdates:
            case ReviewedChangeKind.EnrolQualityUpdates:
            case ReviewedChangeKind.UnenrolQualityUpdates:
                if (p.DeviceIds.Count is < 1 or > 50 || p.DeviceIds.Any(id => !ProfileValidator.IsGuid(id))
                    || p.DeviceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.DeviceIds.Count || p.IncludeGroups.Count + p.ExcludeGroups.Count > 0)
                    throw new SafetyViolationException("Select 1 to 50 distinct Entra device IDs, without group targets.");
                path = UpdatesPath + (IsUnenrol(p.Kind) ? "/unenrollAssets" : "/enrollAssets");
                scope = "WindowsUpdates.ReadWrite.All"; api = GraphApi.Beta; method = "POST";
                expected = UpdatePayload(p); break;
            case ReviewedChangeKind.SecureCompliance:
                path = "/deviceManagement"; scope = "DeviceManagementConfiguration.ReadWrite.All";
                if (p.Before["settings"] is not JsonObject settings || settings["secureByDefault"] is null)
                    throw new SafetyViolationException("Complete tenant compliance settings are required.");
                var copy = (JsonObject)settings.DeepClone(); copy["secureByDefault"] = true;
                expected = new JsonObject { ["settings"] = copy }; break;
            case ReviewedChangeKind.MdmAll:
                if (!ProfileValidator.IsGuid(p.ObjectId)) throw new SafetyViolationException("Select the target-tenant MDM policy ID.");
                path = "/policies/mobileDeviceManagementPolicies/" + p.ObjectId; api = GraphApi.Beta; scope = "Policy.ReadWrite.MobilityManagement";
                if (p.Before["discoveryUrl"]?.ToString() != "https://enrollment.manage.microsoft.com/enrollmentserver/discovery.svc")
                    throw new SafetyViolationException("This action only supports the verified Microsoft Intune MDM policy.");
                expected = new JsonObject { ["appliesTo"] = "all" }; break;
            case ReviewedChangeKind.DisableSms:
            case ReviewedChangeKind.DisableVoice:
            case ReviewedChangeKind.EnableAuthenticator:
                var name = p.Kind == ReviewedChangeKind.DisableSms ? "Sms" : p.Kind == ReviewedChangeKind.DisableVoice ? "Voice" : "MicrosoftAuthenticator";
                path = AuthenticationPath + name; scope = "Policy.ReadWrite.AuthenticationMethod";
                expected = new JsonObject { ["@odata.type"] = p.Before["@odata.type"]?.DeepClone(),
                    ["state"] = p.Kind == ReviewedChangeKind.EnableAuthenticator ? "enabled" : "disabled" };
                if (p.Before["state"] is null || p.Before["@odata.type"] is null) throw new SafetyViolationException("Authentication method read is incomplete.");
                break;
            case ReviewedChangeKind.ConfigureTap:
                path = AuthenticationPath + "TemporaryAccessPass"; scope = "Policy.ReadWrite.AuthenticationMethod";
                if (p.IncludeGroups.Count != 1) throw new SafetyViolationException("TAP requires one explicitly resolved onboarding group.");
                expected = new JsonObject { ["@odata.type"] = "#microsoft.graph.temporaryAccessPassAuthenticationMethodConfiguration", ["state"] = "enabled",
                    ["defaultLength"] = 16, ["defaultLifetimeInMinutes"] = 60, ["minimumLifetimeInMinutes"] = 10, ["maximumLifetimeInMinutes"] = 60, ["isUsableOnce"] = true,
                    ["includeTargets"] = new JsonArray(new JsonObject { ["id"] = p.IncludeGroups[0], ["targetType"] = "group" }) };
                break;
            default:
                if (!ProfileValidator.IsGuid(p.ObjectId)) throw new SafetyViolationException("A recorded object GUID is required.");
                var suffix = "/" + p.ObjectId;
                var root = p.Path.EndsWith("/assign", StringComparison.Ordinal) ? p.Path[..^7] : p.Path;
                if (!root.EndsWith(suffix, StringComparison.Ordinal)) throw new SafetyViolationException("Object path does not match the recorded ID.");
                root = root[..^suffix.Length];
                api = p.Api;
                if (IsAssignment(p.Kind))
                {
                    _ = AssignmentType(root);
                    path = root + suffix + "/assign"; method = "POST";
                    scope = root.StartsWith("/deviceAppManagement/", StringComparison.Ordinal) ? "DeviceManagementApps.ReadWrite.All"
                        : root is "/deviceManagement/deviceEnrollmentConfigurations" or "/deviceManagement/windowsAutopilotDeploymentProfiles"
                            ? "DeviceManagementServiceConfig.ReadWrite.All" : "DeviceManagementConfiguration.ReadWrite.All";
                    if (p.Kind == ReviewedChangeKind.AssignGroups && (p.Population ?? AssignmentPopulation.Groups) == AssignmentPopulation.Groups && p.IncludeGroups.Count == 0) throw new SafetyViolationException("Select at least one included group.");
                    if (p.Kind == ReviewedChangeKind.RemoveAssignments && p.IncludeGroups.Concat(p.ExcludeGroups).Any())
                        throw new SafetyViolationException("Containment removes all assignments; no replacement targets are allowed.");
                    expected = AssignmentPayload(root, p.IncludeGroups, p.ExcludeGroups, p.Population ?? AssignmentPopulation.Groups);
                }
                else
                {
                    if (root != ConditionalAccessSafety.ConditionalAccessPolicyPath || api != GraphApi.V1) throw new SafetyViolationException("Unsupported activation route.");
                    path = root + suffix; scope = "Policy.ReadWrite.ConditionalAccess";
                    expected = new JsonObject { ["state"] = p.Kind switch { ReviewedChangeKind.EnableConditionalAccess => "enabled",
                        ReviewedChangeKind.ReportOnlyConditionalAccess => "enabledForReportingButNotEnforced", ReviewedChangeKind.DisableConditionalAccess => "disabled",
                        _ => throw new SafetyViolationException("Unsupported Conditional Access state.") } };
                }
                break;
        }
        if (p.Path != path || p.RequiredScope != scope || p.Method != method || p.Api != api || CanonicalJson.Sha256(expected) != CanonicalJson.Sha256(p.Payload))
            throw new SafetyViolationException("Reviewed change contains an unsupported route, permission or payload.");
    }
}
