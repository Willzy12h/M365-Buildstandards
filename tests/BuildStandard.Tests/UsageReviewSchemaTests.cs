using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Json;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageReviewSchemaTests
{
    internal static UsageReviewReport Report()
    {
        var csv=Encoding.UTF8.GetBytes(string.Join(',',UsageReportRegistry.Find("email-activity").Columns)+"\r\n");
        return new(){Id=TestData.Office,TenantId=TestData.TenantA,AccountObjectId=null,StartedAt="2026-10-20T12:00:00Z",EndedAt="2026-10-20T12:00:00Z",ToolkitVersion="0.0.0-test",Status="Collected",
            Sources=[new(){SourceId="email-activity",Period="D30",Status="Collected",OriginalCsvBase64=Convert.ToBase64String(csv),CsvSha256=Convert.ToHexStringLower(SHA256.HashData(csv))}]};
    }
    [Fact] public void Original_csv_bytes_roundtrip_and_unknown_privacy_cannot_become_a_join()
    {
        var report=Report();UsageReviewSchema.Seal(report);
        var read=UsageReviewSchema.Read(UsageReviewSchema.Serialise(report),TestData.TenantA);
        Assert.Equal(report.Sources[0].OriginalCsvBase64,read.Sources[0].OriginalCsvBase64);
        var projected=Assert.Single(UsageReviewSchema.Project(read));Assert.Empty(projected.Rows);Assert.False(projected.CanJoinIdentities);
    }
    [Theory][InlineData("tenant")][InlineData("source")][InlineData("digest")][InlineData("privacy")][InlineData("duplicate")][InlineData("failed-data")][InlineData("success-no-data")][InlineData("url")][InlineData("overall")][InlineData("version")][InlineData("live-without-account")]
    public void Resealed_invalid_claims_are_refused(string fault)
    {
        var report=Report();var source=report.Sources[0];
        switch(fault)
        {
            case "tenant":report.TenantId=TestData.TenantB;break;case "source":source.SourceId="arbitrary";break;
            case "digest":source.CsvSha256=new string('a',64);break;case "privacy":source.PrivacyProvenance="Identifiable";break;
            case "duplicate":report.Sources.Add(source);break;case "failed-data":source.Status="Failed";source.Reason="Synthetic failure";report.Status="Failed";break;
            case "success-no-data":source.OriginalCsvBase64=null;source.CsvSha256=null;break;
            case "url":source.DownloadHost="https://reports.office.com/data/download/secret";break;
            case "overall":report.Status="Partial";break;case "version":report.SchemaVersion=2;break;case "live-without-account":report.SourceMode="live";break;
        }
        report.IntegrityDigest=EvidenceIntegrity.Compute(report);
        Assert.ThrowsAny<ToolkitException>(()=>UsageReviewSchema.Read(System.Text.Json.JsonSerializer.Serialize(report,new System.Text.Json.JsonSerializerOptions(ToolkitJson.Options){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.Never}),TestData.TenantA));
    }
    [Fact] public void Duplicate_and_unknown_json_properties_are_refused_even_when_values_look_safe()
    {
        var report=Report();UsageReviewSchema.Seal(report);var json=UsageReviewSchema.Serialise(report);
        Assert.ThrowsAny<ToolkitException>(()=>UsageReviewSchema.Read(json.Insert(json.IndexOf('{')+1,"\"schemaVersion\":1,"),TestData.TenantA));
        var node=JsonNode.Parse(json)!.AsObject();node["downloadUrl"]="https://reports.office.com/data/download/secret";
        Assert.ThrowsAny<ToolkitException>(()=>UsageReviewSchema.Read(node.ToJsonString(),TestData.TenantA));
    }
    [Fact] public void Failed_sources_remain_visible_without_invented_csv_or_known_zero()
    {
        var report=Report();report.Sources.Add(new(){SourceId="teams-activity",Status="Failed",Reason="Synthetic access refusal"});report.Status="Partial";
        UsageReviewSchema.Seal(report);var read=UsageReviewSchema.Read(UsageReviewSchema.Serialise(report),TestData.TenantA);
        Assert.Null(read.Sources[1].OriginalCsvBase64);Assert.Single(UsageReviewSchema.Project(read));Assert.Equal("Failed",read.Sources[1].Status);
    }
    [Fact] public void Encoded_pack_budget_refuses_four_individually_valid_sources_without_clipping_originals()
    {
        var report=Report();report.Sources.Clear();
        foreach(var definition in UsageReportRegistry.Definitions)
        {
            var row=string.Join(',',definition.Columns.Select(name=>name=="Report Refresh Date"?"2026-10-20":name=="Assigned Products"?new string('x',3000):""))+"\r\n";
            var csv=Encoding.UTF8.GetBytes(string.Join(',',definition.Columns)+"\r\n"+string.Concat(Enumerable.Repeat(row,3000)));
            Assert.True(csv.Length<UsageCsv.MaximumBytes);
            report.Sources.Add(new(){SourceId=definition.Id,Status="Collected",Period="D30",OriginalCsvBase64=Convert.ToBase64String(csv),CsvSha256=Convert.ToHexStringLower(SHA256.HashData(csv))});
        }
        var originals=report.Sources.Select(source=>source.OriginalCsvBase64).ToArray();
        var error=Assert.ThrowsAny<ToolkitException>(()=>UsageReviewSchema.Seal(report));Assert.Contains("40MiB",error.Message);
        Assert.Equal(originals,report.Sources.Select(source=>source.OriginalCsvBase64));
    }
}
