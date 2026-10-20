using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OperationalContextSchemaTests
{
    internal static OperationalContextReport Health()=>new()
    {
        ReportId="service-health",AccessIntent="service-health",TenantId=TestData.TenantA,AsOfUtc="2026-10-20T12:00:00Z",StartedAt="2026-10-20T12:00:00Z",EndedAt="2026-10-20T12:00:00Z",ToolkitVersion="synthetic",Status="Collected",
        Sources=[new(){Api="v1.0",RegisteredRoute="/admin/serviceAnnouncement/healthOverviews",Reference="https://learn.microsoft.com/en-us/graph/api/serviceannouncement-list-healthoverviews?view=graph-rest-1.0"},new(){Api="v1.0",RegisteredRoute="/admin/serviceAnnouncement/issues",Reference="https://learn.microsoft.com/en-us/graph/api/serviceannouncement-list-issues?view=graph-rest-1.0"}],
        Sections=[new(){Id="health-overviews",Status="Collected"},new(){Id="health-issues",Status="Collected"}]
    };
    [Fact] public void Genuine_empty_health_roundtrip_is_separate_from_failure_or_current_effective_access()
    {
        var report=Health();OperationalContextSchema.Seal(report);var read=OperationalContextSchema.Read(OperationalContextSchema.Serialise(report),TestData.TenantA);
        Assert.Equal("Collected",read.Status);Assert.All(read.Sections,section=>Assert.Empty(section.Rows));Assert.Null(read.CustomerTenantId);Assert.Null(read.PartnerCoverage);
    }
    [Fact] public void Failure_roundtrips_with_no_invented_rows_and_explicit_partial_overall_status()
    {
        var report=Health();report.Sections[1].Status="Failed";report.Sections[1].Error="Synthetic denied read.";report.Status="Partial";
        OperationalContextSchema.Seal(report);var read=OperationalContextSchema.Read(OperationalContextSchema.Serialise(report),TestData.TenantA);
        Assert.Equal("Failed",read.Sections[1].Status);Assert.Empty(read.Sections[1].Rows);
    }
    [Theory]
    [InlineData("identity")]
    [InlineData("failed-row")]
    [InlineData("unattempted-row")]
    public void Returned_health_objects_cannot_hide_missing_identity_or_claim_unattempted_status(string fault)
    {
        var report = Health();
        var row = new ServiceHealthOverviewRow { Id = "Exchange", Name = "Exchange Online", Status = "serviceOperational", ReadStatus = ReportReadState.Collected };
        if (fault == "identity") row.Id = "   ";
        else
        {
            row.ReadStatus = fault == "failed-row" ? ReportReadState.Failed : ReportReadState.NotAttempted;
            row.Error = "Synthetic incomplete observation.";
            report.Sections[0].Status = ReportReadState.Partial;
            report.Sections[0].Error = "A returned object is incomplete.";
            report.Status = ReportReadState.Partial;
        }
        report.Sections[0].Rows.Add(JsonSerializer.SerializeToNode(row, RegisteredReportJson.Strict)!.AsObject());
        Assert.Throws<ConfigurationException>(() => OperationalContextSchema.Seal(report));
    }
    [Theory][InlineData("tenant")][InlineData("kind")][InlineData("schema")][InlineData("report")][InlineData("clock")][InlineData("customer")][InlineData("overall")][InlineData("sections")][InlineData("live-account")][InlineData("source")]
    public void Resealed_unsupported_or_wrong_identity_claims_are_refused(string fault)
    {
        var report=Health();switch(fault)
        {
            case "tenant":report.TenantId=TestData.TenantB;break;case "kind":report.Kind="reportEvidence";break;case "schema":report.SchemaVersion=2;break;
            case "report":report.ReportId="arbitrary";break;case "clock":report.EndedAt="2026-10-19T12:00:00Z";break;case "customer":report.CustomerTenantId=TestData.TenantB;break;
            case "overall":report.Status="Partial";break;case "sections":report.Sections.Reverse();break;case "live-account":report.SourceMode="live";break;
            case "source":report.Sources[0].RegisteredRoute="/users";break;
        }
        report.IntegrityDigest=EvidenceIntegrity.Compute(report);Assert.ThrowsAny<ToolkitException>(()=>OperationalContextSchema.Read(JsonSerializer.Serialize(report,RegisteredReportJson.Strict),TestData.TenantA));
    }
}
