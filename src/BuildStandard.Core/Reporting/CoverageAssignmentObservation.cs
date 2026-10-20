using System.Text.Json.Nodes;
namespace BuildStandard.Core.Reporting;

/// <summary>Configured assignment only; no evaluation of effective Intune targeting or installation.</summary>
public sealed record CoverageAssignmentObservation(string AssignmentRowId,string? RawTargetType,string? RawGroupId,string? GroupObjectId,string? RawIntent,string? RawFilterType,string? RawFilterId,string TargetMeaning,bool CanObserveGroup,bool UnfilteredTargetVerified,string Detail)
{
    public static CoverageAssignmentObservation Read(JsonObject row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var id=Text(row,"id",256);var intent=Text(row,"intent",128);
        if(!ReportValues.IsTextIdentity(id))throw new ConfigurationException("A coverage assignment requires its original report-row identity.");
        if(row["target"] is not null and not JsonObject)throw new ConfigurationException("The returned assignment target is not an object.");
        var target=row["target"] as JsonObject;
        var type=target is null?null:Text(target,"@odata.type",256);
        var rawGroup=target is null?null:Text(target,"groupId",256);
        var filterType=target is null?null:Text(target,"deviceAndAppManagementAssignmentFilterType",128);
        var filterId=target is null?null:Text(target,"deviceAndAppManagementAssignmentFilterId",256);
        var meaning=type switch
        {
            "#microsoft.graph.groupAssignmentTarget"=>"Configured group",
            "#microsoft.graph.exclusionGroupAssignmentTarget"=>"Configured group exclusion",
            "#microsoft.graph.allDevicesAssignmentTarget"=>"Configured all devices (Intune built-in)",
            "#microsoft.graph.allLicensedUsersAssignmentTarget"=>"Configured all users (Intune built-in)",
            _=>"Unknown target"
        };
        var groupType=type is "#microsoft.graph.groupAssignmentTarget" or "#microsoft.graph.exclusionGroupAssignmentTarget";
        string? groupId=null;
        if(groupType)
        {
            if(ReportValues.TryGuid(rawGroup,out var canonical))groupId=canonical;
            else meaning="Unknown group target — unusable group identity";
        }
        var known=meaning!="Unknown target"&&(!groupType||groupId is not null);
        var expectedKeys=new HashSet<string>(StringComparer.Ordinal){"@odata.type","deviceAndAppManagementAssignmentFilterType","deviceAndAppManagementAssignmentFilterId"};
        if(groupType)expectedKeys.Add("groupId");
        var unfiltered=known&&target is not null&&target.ContainsKey("deviceAndAppManagementAssignmentFilterType")
            &&target.ContainsKey("deviceAndAppManagementAssignmentFilterId")&&filterType=="none"&&filterId is null
            &&target.All(p=>expectedKeys.Contains(p.Key));
        var detail="Configured target observation, not effective targeting and not installation success. Built-in populations are not an enumerated estate. ";
        detail+=unfiltered?"Explicit filter metadata reports none; group membership, exclusions and compatibility remain separate observations."
            :"Assignment filter/extra targeting metadata is missing, unsupported or active; effective targeting requires manual review.";
        return new(id!,type,rawGroup,groupId,intent,filterType,filterId,meaning,groupId is not null,unfiltered,detail);
    }
    private static string? Text(JsonObject row,string key,int maximum)
    {
        if(row[key] is null)return null;
        if(row[key] is JsonValue value&&value.TryGetValue<string>(out var text)&&text.Length<=maximum&&!text.Any(char.IsControl))return text;
        throw new ConfigurationException("An assignment field has an unsupported type or exceeds its text bound; it was not coerced.");
    }
}
