using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class TransportRuleEvidenceV2Tests
{
    internal static ExchangeReportEvidence Report(bool inventory=false)
    {
        var report=InboxRuleEvidenceV2Tests.Report();report.ReportId="exo-transport-rules";report.SourceCommands=["Get-TransportRule"];
        report.Parameters=[new(){Name="RuleGuids",Type="Text",Value=inventory?"":TestData.Office},new(){Name="OrganisationInventory",Type="Boolean",Value=inventory?"true":"false"}];
        var scope=inventory?"OrganisationInventory":"SelectedRule";string? guid=inventory?null:TestData.Office;
        report.Sections[0].Rows=[new JsonObject {
            ["id"]=CalendarPermissionEvidenceV2Tests.Hash(report.ReportId,scope,guid),["name"]=null,["readStatus"]="Collected",["error"]=null,
            ["scope"]=scope,["requestedRuleGuid"]=guid,["requestedResultSize"]=inventory?4999:2,["returnedEntries"]=1,["excludedEntries"]=0 }];
        report.Sections[1].Id="policies";report.Sections[1].Rows=[new JsonObject {
            ["id"]=TestData.Office,["name"]="Synthetic rule",["readStatus"]="Collected",["error"]=null,["ruleGuid"]=TestData.Office,
            ["priority"]=InboxRuleEvidenceV2Tests.Value(0),["state"]=InboxRuleEvidenceV2Tests.Value("Disabled"),["mode"]=InboxRuleEvidenceV2Tests.Value("Audit"),
            ["activationDate"]=InboxRuleEvidenceV2Tests.Value(""),["expiryDate"]=InboxRuleEvidenceV2Tests.Value("") }];return report;
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void Original_rule_guid_and_explicit_scope_roundtrip_without_fabricated_service_ids(bool inventory)
    {
        var report=InboxRuleEvidenceV2Tests.Read(Report(inventory));Assert.Equal(TestData.Office,report.Sections[1].Rows[0]["id"]!.GetValue<string>());
        Assert.Equal(inventory?"OrganisationInventory":"SelectedRule",report.Sections[0].Rows[0]["scope"]!.GetValue<string>());
    }
    [Fact] public void Formatted_policy_ids_are_labelled_as_original_service_guids_and_empty_dates_are_explicit()
    {
        var sheets=RegisteredReportDocuments.Sheets(InboxRuleEvidenceV2Tests.Read(Report()));var policies=sheets.Single(sheet=>sheet.Name=="policies");
        Assert.Contains("Exchange transport-rule GUID",policies.Rows[0]);Assert.DoesNotContain("Local observation key (not a service ID)",policies.Rows[0]);
        Assert.Contains("Empty text (reported)",policies.Rows[1]);
    }
    [Theory][InlineData("scope")][InlineData("service-id")][InlineData("missing-diagnostic")][InlineData("unsuccessful-zero")][InlineData("saturated")][InlineData("duplicate")][InlineData("unknown-value")][InlineData("content")][InlineData("foreign-rule")]
    public void Resealed_false_scope_completeness_unknowns_and_content_are_refused(string fault)
    {
        var report=Report();var target=report.Sections[0].Rows[0];var policy=report.Sections[1].Rows[0];
        switch(fault){case "scope":target["scope"]="OrganisationInventory";break;case "service-id":policy["id"]=CalendarPermissionEvidenceV2Tests.Hash(TestData.Office);break;
            case "missing-diagnostic":report.Sections[0].Rows.Clear();break;case "unsuccessful-zero":target["readStatus"]="Failed";target["error"]="Synthetic refusal";break;
            case "saturated":target["requestedResultSize"]=1;break;case "duplicate":report.Sections[1].Rows.Add((JsonObject)policy.DeepClone());break;
            case "unknown-value":policy["state"]!["state"]="Missing";break;case "content":policy["actions"]=new JsonArray("unnecessary content");break;case "foreign-rule":policy["ruleGuid"]=TestData.TenantB;policy["id"]=TestData.TenantB;break;}
        Assert.ThrowsAny<ToolkitException>(()=>InboxRuleEvidenceV2Tests.Read(report));
    }
}
