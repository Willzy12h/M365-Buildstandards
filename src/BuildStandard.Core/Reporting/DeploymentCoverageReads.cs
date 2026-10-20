using BuildStandard.Core.Models;
namespace BuildStandard.Core.Reporting;

public enum CoverageFamily { ConditionalAccess, Application, ClassicCompliance, ClassicConfiguration }
public enum CoverageReadKind { Object, Assignments, ServiceResults }

/// <summary>Package-owned read descriptor, not an arbitrary URL, permission grant or execution target.</summary>
public sealed class PreparedCoverageRead
{
    public GraphApi Api {get;}
    public string Path {get;}
    public string Scope {get;}
    public string TargetId {get;}
    internal PreparedCoverageRead(GraphApi api,string path,string scope,string target)
    {Api=api;Path=path;Scope=scope;TargetId=target;}
}
public static class DeploymentCoverageReads
{
    public static PreparedCoverageRead Prepare(CoverageFamily family,string objectId,CoverageReadKind kind,GraphApi api=GraphApi.V1)
    {
        var id=Identity(objectId);
        if(api!=GraphApi.V1||!Enum.IsDefined(family)||!Enum.IsDefined(kind))
            throw new ConfigurationException("This coverage source/API is not registered. No beta or other endpoint fallback is performed.");
        var (root,scope)=family switch
        {
            CoverageFamily.ConditionalAccess=>("/identity/conditionalAccess/policies/","Policy.Read.All"),
            CoverageFamily.Application=>("/deviceAppManagement/mobileApps/","DeviceManagementApps.Read.All"),
            CoverageFamily.ClassicCompliance=>("/deviceManagement/deviceCompliancePolicies/","DeviceManagementConfiguration.Read.All"),
            _=>("/deviceManagement/deviceConfigurations/","DeviceManagementConfiguration.Read.All")
        };
        if(kind==CoverageReadKind.Assignments&&family==CoverageFamily.ConditionalAccess)
            throw new ConfigurationException("Conditional Access targeting is recorded in the policy. It has no Intune assignments endpoint.");
        if(kind==CoverageReadKind.ServiceResults&&family!=CoverageFamily.ClassicCompliance)
            throw new ConfigurationException("No service-result read is registered for this family. Application/classic-configuration results require the separately bounded modern export adapter; deprecated endpoints are never substituted. Conditional Access uses its separate hypothetical evaluation.");
        var suffix=kind switch{CoverageReadKind.Assignments=>"/assignments",CoverageReadKind.ServiceResults=>"/deviceStatuses",_=>""};
        return new(api,root+id+suffix,scope,id);
    }
    public static PreparedCoverageRead Group(string groupId,bool members)
    {
        var id=Identity(groupId);
        var suffix=members?"/members?$select=id&$top=100":"?$select=id,displayName,groupTypes,membershipRuleProcessingState,visibility";
        return new(GraphApi.V1,"/groups/"+id+suffix,"Group.Read.All",id);
    }
    private static string Identity(string value)=>ReportValues.TryGuid(value,out var id)?id
        :throw new ConfigurationException("Select an exact non-empty object GUID. URLs, paths and unverified names cannot become a coverage request.");
}
