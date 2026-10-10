using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Core.Safety;

public static class RecoverySafety
{
    // Conditional Access recovery remains v1.0; each Intune collection declares its own API version.
    public static bool Supports(GraphApi api, string path) => Supports(path)
        && (api == GraphApi.V1 || api == GraphApi.Beta && path != ConditionalAccessSafety.ConditionalAccessPolicyPath);
    public static bool Supports(string path) => path is ConditionalAccessSafety.ConditionalAccessPolicyPath
        or "/deviceManagement/deviceConfigurations" or "/deviceManagement/deviceCompliancePolicies"
        or "/deviceManagement/configurationPolicies" or "/deviceManagement/deviceEnrollmentConfigurations"
        or "/deviceManagement/windowsAutopilotDeploymentProfiles" or "/deviceAppManagement/mobileApps"
        or "/deviceAppManagement/iosManagedAppProtections" or "/deviceAppManagement/androidManagedAppProtections";

    public static void AssertPayload(string path, RecoveryAction action, JsonObject? payload, GraphApi api = GraphApi.V1)
    {
        if (!Supports(path) || !Enum.IsDefined(action)) throw new WriteDeniedException("Unsupported recovery route or action.");
        if (action == RecoveryAction.DeleteCreatedObject)
        {
            if (payload is not null) throw new WriteDeniedException("Deletion must not contain a payload.");
            return;
        }
        if (payload is null || payload.Count == 0) throw new WriteDeniedException("Recovery settings are missing.");
        if (action == RecoveryAction.DisableConditionalAccess
            && (path != ConditionalAccessSafety.ConditionalAccessPolicyPath || payload.Count != 1 || ConditionalAccessSafety.State(payload) != "disabled"))
            throw new WriteDeniedException("Containment may only set a Conditional Access policy to disabled.");
        WritePayloadGuard.Assert(new CollectionDefinition { Path = path, Api = api == GraphApi.Beta ? "beta" : "v1.0" }, payload);
    }
}
