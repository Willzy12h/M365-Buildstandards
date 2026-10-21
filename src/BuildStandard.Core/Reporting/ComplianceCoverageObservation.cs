using System.Text.Json;
using System.Text.Json.Nodes;
namespace BuildStandard.Core.Reporting;

/// <summary>A policy report row, never a managed-device identity or proof of effective targeting.</summary>
public sealed record ComplianceCoverageObservation(string? ReportRowId,string? DeviceDisplayName,string? RawStatus,string? RawReportedAt,string Observation,string Freshness,string Detail)
{
    public string IdentityMeaning=>"Policy report row ID; no authoritative managed-device ID is returned by this resource. No device/name/UPN join or device-specific verification is established.";
    public static ComplianceCoverageObservation Read(JsonObject row,DateTimeOffset collectedAt,DateTimeOffset? selectedChangeAt=null)
    {
        if(row is null||selectedChangeAt>collectedAt)throw new ConfigurationException("The coverage row/reference interval is invalid. No result is inferred.");
        var id=Text(row,"id",256);var name=Text(row,"deviceDisplayName",512);var status=Text(row,"status",128);var rawTime=Text(row,"lastReportedDateTime",64);
        var freshness="Unknown — no selected reference time";string? reason=null;
        DateTimeOffset? observed=null;
        if(rawTime is not null)
        {
            try{observed=JsonSerializer.Deserialize<DateTimeOffset>(JsonSerializer.Serialize(rawTime));}
            catch(JsonException){}
        }
        if(!ReportValues.IsTextIdentity(id))reason="No usable original report-row identity was returned.";
        if(observed is null){reason??="The original report timestamp is missing or invalid.";freshness="Unknown — missing or invalid report time";}
        else if(observed>collectedAt){reason??="The report time is later than collection; freshness cannot be established.";freshness="Unknown — report time is in the future";}
        else if(selectedChangeAt is not null)
        {
            if(observed<selectedChangeAt){reason??="The service observation predates the selected change/pilot reference.";freshness="Stale — before selected reference";}
            else freshness="At or after selected reference";
        }
        var meaning=status switch
        {
            "compliant"=>"Reported compliant","nonCompliant"=>"Reported non-compliant","error"=>"Reported error",
            "conflict"=>"Reported conflict","notApplicable"=>"Reported not applicable","notAssigned"=>"Reported not assigned",
            "remediated"=>"Reported remediated",_=>"Unknown"
        };
        if(reason is not null)meaning="Unknown";
        else if(meaning!="Unknown"&&selectedChangeAt is null)meaning+=" — freshness unknown";
        var detail=(reason??(meaning=="Unknown"?"The service status is missing, unknown or unsupported.":"This is the raw service's policy report observation."))
            +" It is not proof of effective targeting, device-specific verification or causation by a selected change.";
        return new(id,name,status,rawTime,meaning,freshness,detail);
    }
    private static string? Text(JsonObject row,string key,int maximum)
    {
        if(row[key] is null)return null;
        if(row[key] is not JsonValue value||!value.TryGetValue<string>(out var text)||text.Length>maximum)
            throw new ConfigurationException("A compliance report field has an unsupported type or exceeds its bound. No value is coerced or guessed.");
        return text;
    }
}
