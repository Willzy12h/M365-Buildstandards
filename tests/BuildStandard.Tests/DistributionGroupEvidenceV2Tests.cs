using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DistributionGroupEvidenceV2Tests
{
    internal static ExchangeReportEvidence Report()
    {
        var report=InboxRuleEvidenceV2Tests.Report();report.ReportId="exo-distribution-groups";
        report.SourceCommands=["Get-DistributionGroup","Get-DistributionGroupMember"];
        report.Parameters=[new(){Name="GroupGuids",Type="Text",Value=TestData.Office}];
        report.Sections[0].Rows=[new JsonObject {
            ["id"]=CalendarPermissionEvidenceV2Tests.Hash(report.ReportId,TestData.Office),["name"]="Synthetic group",["readStatus"]="Collected",["error"]=null,
            ["requestedGroupGuid"]=TestData.Office,["observedGroupGuid"]=TestData.Office,["externalDirectoryObjectId"]=null,["primarySmtpAddress"]="group@synthetic.example",
            ["lookupReadStatus"]="Collected",["membershipReadStatus"]="Collected",["returnedEntries"]=1,["excludedEntries"]=0,
            ["recipientTypeDetails"]=InboxRuleEvidenceV2Tests.Value("MailUniversalDistributionGroup"),
            ["managedBy"]=InboxRuleEvidenceV2Tests.Value(new JsonArray(new JsonObject{["namespace"]="RawIdentity",["value"]="original-owner",["externalDirectoryObjectId"]=null})),
            ["memberJoinRestriction"]=InboxRuleEvidenceV2Tests.Value("Closed"),["memberDepartRestriction"]=InboxRuleEvidenceV2Tests.Value("Open"),
            ["requireSenderAuthenticationEnabled"]=InboxRuleEvidenceV2Tests.Value(false),["hiddenFromAddressListsEnabled"]=InboxRuleEvidenceV2Tests.Value(false) }];
        report.Sections[1].Id="members";report.Sections[1].Rows=[new JsonObject {
            ["id"]=ExchangeObservationKey.GroupMember(TestData.Office,"ExchangeRecipientGuid",TestData.Operator),["name"]="Synthetic member",["readStatus"]="Collected",["error"]=null,
            ["groupGuid"]=TestData.Office,["sourceIdentity"]=new JsonObject{["namespace"]="ExchangeRecipientGuid",["value"]=TestData.Operator,["externalDirectoryObjectId"]=TestData.TenantB},
            ["primarySmtpAddress"]="member@synthetic.example",["recipientTypeDetails"]=InboxRuleEvidenceV2Tests.Value("UserMailbox") }];return report;
    }
    [Fact] public void Direct_members_and_owner_references_keep_distinct_namespaces_and_reported_false()
    {
        var report=InboxRuleEvidenceV2Tests.Read(Report());var member=Assert.Single(report.Sections[1].Rows);
        Assert.Equal(TestData.Operator,member["sourceIdentity"]!["value"]!.GetValue<string>());
        Assert.Equal(TestData.TenantB,member["sourceIdentity"]!["externalDirectoryObjectId"]!.GetValue<string>());
        Assert.False(report.Sections[0].Rows[0]["requireSenderAuthenticationEnabled"]!["value"]!.GetValue<bool>());
    }
    [Fact] public void Human_exports_show_original_member_namespace_and_separate_directory_identity()
    {
        var sheet=RegisteredReportDocuments.Sheets(InboxRuleEvidenceV2Tests.Read(Report())).Single(s=>s.Name=="members");
        var cells=Assert.Single(sheet.Rows.Skip(1));
        Assert.Contains($"Exchange recipient GUID: {TestData.Operator} · Returned Entra object ID: {TestData.TenantB}",cells);
    }
    [Fact] public void Failed_membership_remains_unknown_despite_a_successful_group_lookup()
    {
        var report=Report();var target=report.Sections[0].Rows[0];target["membershipReadStatus"]="Failed";target["returnedEntries"]=null;target["excludedEntries"]=null;
        target["readStatus"]="Partial";target["error"]="Synthetic member read failed";report.Sections[1].Rows.Clear();
        foreach(var section in report.Sections){section.Status="Partial";section.Error="Synthetic membership failure";}report.Status="Partial";
        var read=InboxRuleEvidenceV2Tests.Read(report);Assert.Empty(read.Sections[1].Rows);Assert.Equal("Partial",read.Status);
    }
    [Theory][InlineData("foreign-parent")][InlineData("unselected")][InlineData("directory-join")][InlineData("invented-false")][InlineData("failed-empty-success")][InlineData("duplicate")][InlineData("unsupported-kind")][InlineData("counts")]
    public void Resealed_scope_kind_identity_and_unknown_claims_are_refused(string fault)
    {
        var report=Report();var target=report.Sections[0].Rows[0];var member=report.Sections[1].Rows[0];
        switch(fault)
        {
            case "foreign-parent":member["groupGuid"]=TestData.TenantB;break;
            case "unselected":report.Parameters[0].Value=TestData.TenantB;break;
            case "directory-join":member["sourceIdentity"]!["namespace"]="DisplayNameJoin";break;
            case "invented-false":target["requireSenderAuthenticationEnabled"]!["state"]="Missing";target["requireSenderAuthenticationEnabled"]!["reason"]="Unavailable";break;
            case "failed-empty-success":target["membershipReadStatus"]="Failed";report.Sections[1].Rows.Clear();break;
            case "duplicate":report.Sections[1].Rows.Add((JsonObject)member.DeepClone());break;
            case "unsupported-kind":target["recipientTypeDetails"]!["value"]="GroupMailbox";break;
            case "counts":target["returnedEntries"]=0;break;
        }
        Assert.ThrowsAny<ToolkitException>(()=>InboxRuleEvidenceV2Tests.Read(report));
    }
}
