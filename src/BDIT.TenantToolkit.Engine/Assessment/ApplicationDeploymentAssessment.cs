using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Collection;

namespace BDIT.TenantToolkit.Engine.Assessment;

/// <summary>Assignment records are observations, not proof of installation or effective group membership.</summary>
public static class ApplicationDeploymentAssessment
{
    public sealed record Result(bool? Established, string Reason);

    public static Result Evaluate(JsonObject app, ApplicationDeploymentExpectation? expected)
    {
        if (app[TenantCollector.AssignmentsUnknownKey] is not null || app[TenantCollector.AssignmentsKey] is not JsonArray assignments)
            return new(null, "Application assignments were not completely captured.");
        if (assignments.Count == 0) return new(false, "No application deployment assignments were observed.");
        if (expected is null || expected.Intent != "required" || expected.Population is not (AssignmentPopulation.AllUsers or AssignmentPopulation.AllDevices))
            return new(null, "Assignment records were observed, but the standard does not establish a supported expected deployment scope.");
        if (!string.IsNullOrEmpty(expected.ExclusionControlId))
            return new(null, $"Review exclusion prerequisite {expected.ExclusionControlId}, its intended members and compatible user/device targeting. Its name or GUID does not establish effective deployment scope.");

        var requiredType = "#microsoft.graph." + (expected.Population == AssignmentPopulation.AllUsers
            ? "allLicensedUsersAssignmentTarget" : "allDevicesAssignmentTarget");
        var hasRequired = false;
        foreach (var assignment in assignments)
        {
            if (assignment is not JsonObject row || row["target"] is not JsonObject target)
                return new(null, "An application assignment has no recognised target; effective scope is unknown.");
            var type = target["@odata.type"]?.ToString();
            if (type != requiredType)
                return new(null, "Assignment targets, exclusions or group populations differ from the declared scope; review effective targeting.");
            if (target.Any(p => p.Key is not ("@odata.type" or "deviceAndAppManagementAssignmentFilterId" or "deviceAndAppManagementAssignmentFilterType"))
                || !string.IsNullOrEmpty(target["deviceAndAppManagementAssignmentFilterId"]?.ToString())
                || target["deviceAndAppManagementAssignmentFilterType"]?.ToString() is not (null or "none"))
                return new(null, "An assignment filter or additional targeting property needs manual scope review.");
            if (row["intent"]?.ToString() == "required") hasRequired = true;
            else if (row["intent"]?.ToString() is not ("available" or "availableWithoutEnrollment" or "uninstall"))
                return new(null, "The application assignment intent is missing or unsupported.");
            else if (assignments.Count > 1)
                return new(null, "Mixed application intents require review; required installation cannot be inferred.");
        }
        return hasRequired
            ? new(true, "Required deployment targets the declared built-in population without exclusions or filters. This verifies assignment configuration, not installation success.")
            : new(false, "Only available or uninstall intent was observed; this does not establish required deployment.");
    }
}
