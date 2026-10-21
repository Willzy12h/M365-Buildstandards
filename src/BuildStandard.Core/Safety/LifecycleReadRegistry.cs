namespace BuildStandard.Core.Safety;

/// <summary>One registered v1.0 GET dependency; never a caller-supplied URL or an execution permission.</summary>
public sealed record LifecycleReadRequest(string Id, string Path, bool Single, string Scope);
public static class LifecycleReadRegistry
{
    public static IReadOnlyList<LifecycleReadRequest> For(LifecycleOperation selection)
    {
        var operation = LifecycleActionRegistry.RequireRegistered(selection);
        var user = operation.UserId;
        var principal = Uri.EscapeDataString("principalId eq '" + user + "'");
        var requests = new List<LifecycleReadRequest>
        {
            new("user", "/users/" + user + "?$select=id,displayName,userPrincipalName,userType,onPremisesSyncEnabled,accountEnabled,usageLocation,assignedLicenses,licenseAssignmentStates,signInSessionsValidFromDateTime", true, "User.Read.All"),
            new("currentRoles", "/roleManagement/directory/roleAssignments?$filter=" + principal + "&$select=id,principalId,roleDefinitionId,directoryScopeId,appScopeId", false, "RoleManagement.Read.Directory"),
            new("eligibleRoles", "/roleManagement/directory/roleEligibilityScheduleInstances?$filter=" + principal + "&$select=id,principalId,roleDefinitionId,directoryScopeId,appScopeId", false, "RoleManagement.Read.Directory"),
            new("roleAssignableGroups", "/groups?$filter=" + Uri.EscapeDataString("isAssignableToRole eq true") + "&$select=id,displayName,isAssignableToRole&$top=100", false, "Group.Read.All")
        };
        if (operation.GroupId is { } group)
        {
            requests.Add(new("group", "/groups/" + group + "?$select=id,displayName,groupTypes,securityEnabled,mailEnabled,onPremisesSyncEnabled,isAssignableToRole,assignedLicenses", true, "Group.Read.All"));
            requests.Add(new("members", "/groups/" + group + "/members?$select=id&$top=100", false, "Group.Read.All"));
        }
        if (operation.GroupId is not null || operation.SkuId is not null)
        {
            requests.Add(new("subscribedSkus", "/subscribedSkus?$select=skuId,skuPartNumber,capabilityStatus,consumedUnits,prepaidUnits,servicePlans", false, "Organization.Read.All"));
            requests.Add(new("licenseDetails", "/users/" + user + "/licenseDetails?$select=skuId,skuPartNumber,servicePlans", false, "User.Read.All"));
        }
        return requests.AsReadOnly();
    }
}
