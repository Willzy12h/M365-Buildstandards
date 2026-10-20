using System.Text.Json.Nodes;
namespace BuildStandard.Core.Reporting;
public sealed record CoverageGroupAdmission(string GroupId,bool CanReadDirectMembers,string Reason)
{
    public string Limitations=>"Microsoft Graph v1 direct members omit service principals. No ownership, transitive/hidden membership, tenant-wide denominator or effective targeting is established; direct membership is not effective targeting.";
    public static CoverageGroupAdmission Read(JsonObject row,string expectedGroupId)
    {
        if(row is null||!ReportValues.TryGuid(expectedGroupId,out var expected))
            throw new ConfigurationException("Select an exact group identity before reading coverage metadata.");
        var rawId=Text(row,"id");
        if(!ReportValues.TryGuid(rawId,out var id))throw new ConfigurationException("The source did not return a usable group identity. No member read is admitted.");
        if(!string.Equals(id,expected,StringComparison.Ordinal))throw new TenantMismatchException("The returned group is not the selected assignment group. No member read is admitted.");
        // Validate returned wire types even when another field already makes the population unsupported.
        var visibility=Text(row,"visibility");var processing=Text(row,"membershipRuleProcessingState");
        string[]? types=null;
        if(row["groupTypes"] is not null)
        {
            if(row["groupTypes"] is not JsonArray array||array.Count>16)
                throw new ConfigurationException("Group types have an unsupported shape or exceed their bound.");
            types=array.Select(n=>n is JsonValue v&&v.TryGetValue<string>(out var t)&&t.Length<=128?t
                :throw new ConfigurationException("A returned group type is not a bounded string.")).ToArray();
            if(types.Distinct(StringComparer.Ordinal).Count()!=types.Length)
                throw new ConfigurationException("Duplicate group types cannot establish a supported population.");
        }
        if(types is null)return new(id,false,"Group types were not returned completely; a static population is unknown.");
        if(types.Any(t=>t!="Unified"))return new(id,false,"Dynamic or unsupported group types cannot establish this adapter's static direct population.");
        if(!row.ContainsKey("membershipRuleProcessingState")||processing is not null)
            return new(id,false,"Static membership could not be established from the returned processing-state metadata.");
        if(visibility is not ("Public" or "Private"))return new(id,false,"Visibility is hidden, unknown or unavailable; no hidden-membership permission is requested.");
        return new(id,true,"Selected static visible group; a separately bounded direct-member read may be requested. No members or coverage have yet been established.");
    }
    private static string? Text(JsonObject row,string key)
    {
        if(row[key] is null)return null;
        if(row[key] is JsonValue value&&value.TryGetValue<string>(out var text)&&text.Length<=512)return text;
        throw new ConfigurationException("Returned group metadata has an unsupported type or exceeds its bound.");
    }
}
