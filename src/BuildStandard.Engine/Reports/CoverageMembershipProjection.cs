using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public sealed record CoverageMemberObservation(string ObjectId,string? RawType,string IdentityNamespace,string Detail);
public sealed record CoveragePilotObservation(string ObjectId,string IdentityNamespace,string Observation,string Detail);
public sealed record CoverageMembershipResult(IReadOnlyList<CoverageMemberObservation> Members,IReadOnlyList<CoveragePilotObservation> Pilots,string PopulationState,string Limitations);

/// <summary>Exact typed direct-member observations only. No transitive traversal, name joins or effective-targeting evaluator.</summary>
public static class CoverageMembershipProjection
{
    private const string User="Directory user object ID",Device="Directory device object ID",Unknown="Unknown directory-object type";
    public static CoverageMembershipResult Project(CoverageGroupAdmission group,CoverageSourceCapture source,IEnumerable<string> userIds,IEnumerable<string> directoryDeviceIds)
    {
        ArgumentNullException.ThrowIfNull(group);ArgumentNullException.ThrowIfNull(source);
        if(!ReportValues.TryGuid(group.GroupId,out _))throw new ConfigurationException("Membership projection requires the exact selected group identity.");
        var members=new List<CoverageMemberObservation>();var identities=new HashSet<string>(StringComparer.Ordinal);
        foreach(var row in source.Rows)
        {
            var rawId=Text(row,"id");var type=Text(row,"@odata.type");
            if(members.Count>=5000||!ReportValues.TryGuid(rawId,out var id)||!identities.Add(id))
                throw new ConfigurationException("Direct-member rows have invalid/duplicate identities or exceed the bounded population; no membership conclusion is made.");
            var ns=type switch{"#microsoft.graph.user"=>User,"#microsoft.graph.device"=>Device,_=>Unknown};
            var detail=ns==Device?"Directory device object identity, not an Intune managed-device ID or azureADDeviceId mapping.":ns==User?"Exact returned directory user object identity; no display-name/UPN match.":"Nested, unsupported or unreturned object type; not expanded or inferred.";
            members.Add(new(id,type,ns,detail));
        }
        var namespaces=members.Select(m=>m.IdentityNamespace).Distinct(StringComparer.Ordinal).ToArray();
        var homogeneous=namespaces.Length<=1&&!namespaces.Contains(Unknown,StringComparer.Ordinal);
        var complete=source.State==CoverageSourceState.Complete&&group.CanReadDirectMembers;
        var observed=members.Select(m=>(m.ObjectId,m.IdentityNamespace)).ToHashSet();
        var pilots=new List<CoveragePilotObservation>();AddPilots(userIds,User);AddPilots(directoryDeviceIds,Device);
        var population=source.State+" · "+(!group.CanReadDirectMembers?group.Reason:!homogeneous?"Mixed, nested or unknown member types; supported population is unable to establish.":complete?"Requested direct source finished; selected typed-subject observations only.":"Incomplete source; selected membership remains unknown.");
        const string limitations="Point-in-time direct membership, not effective targeting, compatible Intune assignment or verification. Microsoft Graph v1 omits service principals; this is not a tenant-wide denominator. Exclusions, filters, nested groups and user/device compatibility remain separate; names and UPNs are never joined.";
        return new(Array.AsReadOnly(members.ToArray()),Array.AsReadOnly(pilots.ToArray()),population,limitations);

        void AddPilots(IEnumerable<string> values,string ns)
        {
            ArgumentNullException.ThrowIfNull(values);var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var value in values)
            {
                if(pilots.Count>=5000||!ReportValues.TryGuid(value,out var id)||!seen.Add(id))throw new ConfigurationException("Pilot IDs must be unique bounded directory GUIDs within their explicit namespace.");
                var compatible=complete&&homogeneous&&(namespaces.Length==0||namespaces[0]==ns);
                var status=compatible?(observed.Contains((id,ns))?"Observed present":"Observed absent"):"Unknown";
                var detail=compatible?"Observation within this completely read supported direct population only; not effective assignment or protection.":"Read state, group support or identity-type compatibility prevents a supported membership conclusion; no absence is inferred.";
                pilots.Add(new(id,ns,status,detail));
            }
        }
    }
    private static string? Text(JsonObject row,string key)
    {
        if(row[key] is null)return null;
        if(row[key] is JsonValue value&&value.TryGetValue<string>(out var text)&&text.Length<=256&&!text.Any(char.IsControl))return text;
        throw new ConfigurationException("A direct-member field is not a bounded string; no type or identity is guessed.");
    }
}
