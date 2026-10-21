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
    public bool IsCollection {get;}
    public bool IsGroupMembers {get;}
    internal PreparedCoverageRead(GraphApi api,string path,string scope,string target,bool collection,bool groupMembers=false)
    {Api=api;Path=path;Scope=scope;TargetId=target;IsCollection=collection;IsGroupMembers=groupMembers;}
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
        return new(api,root+id+suffix,scope,id,kind!=CoverageReadKind.Object);
    }
    public static PreparedCoverageRead Group(string groupId,bool members)
    {
        var id=Identity(groupId);
        var suffix=members?"/members?$select=id&$top=100":"?$select=id,displayName,groupTypes,membershipRuleProcessingState,visibility";
        return new(GraphApi.V1,"/groups/"+id+suffix,"Group.Read.All",id,members,members);
    }
    /// <summary>Admit only a continuation of this same compiled relation, retaining its exact projection.</summary>
    public static PreparedCoverageRead Continue(PreparedCoverageRead request,string nextLink)
    {
        ArgumentNullException.ThrowIfNull(request);
        if(!request.IsCollection||nextLink is not {Length:>0 and <=8192}||nextLink.Any(char.IsControl)
            ||!Uri.TryCreate(nextLink,UriKind.Absolute,out var next)||next.Scheme!=Uri.UriSchemeHttps
            ||!string.Equals(next.Host,"graph.microsoft.com",StringComparison.OrdinalIgnoreCase)||next.Port!=443
            ||next.UserInfo.Length!=0||next.Fragment.Length!=0)
            throw new ConfigurationException("The further page is not a supported public-cloud Graph continuation. Earlier pages remain partial.");
        var split=request.Path.Split('?',2);var relation=split[0];
        if(next.AbsolutePath!="/v1.0"+relation)
            throw new ConfigurationException("The further page changes the selected API/object relation. It was not followed.");
        var original=Query(split.Length==2?split[1]:"");original.Remove("$skiptoken");original.Remove("$skip");
        var query=Query(next.Query.TrimStart('?'));
        var cursors=query.Keys.Count(k=>k is "$skiptoken" or "$skip");
        if(cursors!=1)throw new ConfigurationException("A further page requires exactly one supported cursor.");
        foreach(var pair in query)
        {
            if(pair.Key is "$skiptoken" or "$skip")continue;
            if(!original.TryGetValue(pair.Key,out var expected)||pair.Value!=expected)
                throw new ConfigurationException("A further page adds or changes the registered query/projection. It was not followed.");
        }
        var cursor=query.ContainsKey("$skiptoken")?"$skiptoken":"$skip";var value=query[cursor];
        if(value.Length is 0 or >4096||value.Any(char.IsControl)
            ||cursor=="$skip"&&(!value.All(char.IsAsciiDigit)||!int.TryParse(value,out _)))
            throw new ConfigurationException("The further-page cursor is malformed or exceeds its bound.");
        var parameters=original.Select(p=>p.Key+"="+Uri.EscapeDataString(p.Value)).ToList();parameters.Add(cursor+"="+Uri.EscapeDataString(value));
        return new(request.Api,relation+"?"+string.Join('&',parameters),request.Scope,request.TargetId,true,request.IsGroupMembers);
    }
    private static Dictionary<string,string> Query(string query)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        if(query.Length==0)return result;
        foreach(var component in query.Split('&'))
        {
            var pair=component.Split('=',2);
            if(pair.Length!=2)throw new ConfigurationException("The further-page query is malformed.");
            var key=Uri.UnescapeDataString(pair[0]);var value=Uri.UnescapeDataString(pair[1]);
            if(key is not ("$select" or "$top" or "$skiptoken" or "$skip")||!result.TryAdd(key,value))
                throw new ConfigurationException("The further-page query is duplicated or outside the registered surface.");
        }
        return result;
    }
    private static string Identity(string value)=>ReportValues.TryGuid(value,out var id)?id
        :throw new ConfigurationException("Select an exact non-empty object GUID. URLs, paths and unverified names cannot become a coverage request.");
}
