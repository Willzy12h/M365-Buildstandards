using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ComplianceCoverageObservationTests
{
    private static readonly DateTimeOffset Collected=new(2026,10,20,12,0,0,TimeSpan.Zero);
    private static JsonObject Row()=>new(){["id"]="opaque-report-row-7",["deviceDisplayName"]="Synthetic device",["status"]="compliant",["lastReportedDateTime"]="2026-10-20T11:30:00Z"};
    [Theory][InlineData("compliant","Reported compliant")][InlineData("nonCompliant","Reported non-compliant")][InlineData("error","Reported error")][InlineData("conflict","Reported conflict")][InlineData("notApplicable","Reported not applicable")][InlineData("notAssigned","Reported not assigned")][InlineData("remediated","Reported remediated")]
    public void Known_service_status_is_separate_from_subject_identity_and_verification(string raw,string expected)
    {
        var row=Row();row["status"]=raw;var r=ComplianceCoverageObservation.Read(row,Collected,Collected.AddHours(-1));Assert.Equal(expected,r.Observation);Assert.Equal(raw,r.RawStatus);Assert.Equal("At or after selected reference",r.Freshness);
        Assert.Equal("opaque-report-row-7",r.ReportRowId);Assert.Contains("no authoritative managed-device ID",r.IdentityMeaning);Assert.Contains("not proof",r.Detail);
    }
    [Theory][InlineData("unknown")][InlineData("futureStatus")][InlineData("")][InlineData(null)]
    public void Future_or_unreturned_status_is_never_a_success(string? raw)
    {var row=Row();row["status"]=raw;var r=ComplianceCoverageObservation.Read(row,Collected,Collected.AddHours(-1));Assert.Equal("Unknown",r.Observation);Assert.Equal(raw,r.RawStatus);}
    [Theory][InlineData("missing-time")][InlineData("invalid-time")][InlineData("future-time")][InlineData("stale")][InlineData("missing-id")]
    public void Incomplete_or_stale_rows_cannot_verify_a_selected_change(string defect)
    {
        var row=Row();switch(defect){case "missing-time":row.Remove("lastReportedDateTime");break;case "invalid-time":row["lastReportedDateTime"]="yesterday";break;case "future-time":row["lastReportedDateTime"]="2026-10-21T00:00:00Z";break;case "stale":row["lastReportedDateTime"]="2026-10-19T00:00:00Z";break;case "missing-id":row.Remove("id");break;}
        var r=ComplianceCoverageObservation.Read(row,Collected,Collected.AddHours(-1));Assert.Equal("Unknown",r.Observation);Assert.Equal("compliant",r.RawStatus);Assert.Contains("not proof",r.Detail);
    }
    [Fact] public void No_selected_reference_leaves_freshness_unknown_even_with_a_reported_compliant_value()
    {var r=ComplianceCoverageObservation.Read(Row(),Collected);Assert.Equal("Unknown — no selected reference time",r.Freshness);Assert.Contains("freshness unknown",r.Observation);Assert.Contains("not proof",r.Detail);}
    [Fact] public void Offsets_are_compared_as_instants_and_original_time_is_preserved()
    {var row=Row();row["lastReportedDateTime"]="2026-10-20T12:30:00+01:00";var r=ComplianceCoverageObservation.Read(row,Collected,Collected.AddHours(-1));Assert.Equal("At or after selected reference",r.Freshness);Assert.Equal("2026-10-20T12:30:00+01:00",r.RawReportedAt);}
    [Theory][InlineData("status")][InlineData("lastReportedDateTime")][InlineData("id")][InlineData("deviceDisplayName")]
    public void Wrong_wire_types_are_refused_instead_of_stringified(string property)
    {var row=Row();row[property]=false;Assert.Throws<ConfigurationException>(()=>ComplianceCoverageObservation.Read(row,Collected));}
    [Fact] public void A_reference_later_than_the_collection_is_an_invalid_request()
    {Assert.Throws<ConfigurationException>(()=>ComplianceCoverageObservation.Read(Row(),Collected,Collected.AddDays(1)));}
    [Fact] public void A_report_row_that_looks_like_a_known_device_ID_still_has_the_report_row_namespace()
    {var row=Row();row["id"]=TestData.Office;row["deviceDisplayName"]=TestData.Office;var r=ComplianceCoverageObservation.Read(row,Collected,Collected.AddHours(-1));Assert.Equal(TestData.Office,r.ReportRowId);Assert.Contains("No device/name/UPN join",r.IdentityMeaning);}
}
