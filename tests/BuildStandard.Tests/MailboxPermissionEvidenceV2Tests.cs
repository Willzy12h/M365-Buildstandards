using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;

public sealed class MailboxPermissionEvidenceV2Tests
{
    private const string Mailbox = TestData.Office, Principal = "S-1-5-21-123-456-789-1001";
    internal static BuildStandard.Core.Models.ExchangeReportEvidence Report()
    {
        var report=InboxRuleEvidenceV2Tests.Report();report.ReportId="exo-mailbox-permissions";
        report.SourceCommands=["Get-EXOMailbox","Get-EXOMailboxPermission","Get-EXORecipientPermission"];
        report.Parameters=[new(){Name="MailboxGuids",Type="Text",Value=Mailbox},new(){Name="FullAccess",Type="Boolean",Value="true"},new(){Name="SendAs",Type="Boolean",Value="false"},new(){Name="SendOnBehalf",Type="Boolean",Value="false"}];
        var key="sha256:"+Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]{report.ReportId,Mailbox})));
        JsonObject Read(string kind,bool selected)=>new(){["kind"]=kind,["selected"]=selected,["status"]=selected?"Collected":"NotAttempted",["error"]=selected?null:"Not selected",["returnedEntries"]=selected?1:null,["excludedEntries"]=selected?0:null};
        report.Sections[0].Rows=[new JsonObject
        {
            ["id"]=key,["name"]="Synthetic selected mailbox",["readStatus"]="Collected",["error"]=null,
            ["requestedExchangeGuid"]=Mailbox,["observedExchangeGuid"]=Mailbox,["externalDirectoryObjectId"]=null,["primarySmtpAddress"]=null,["lookupReadStatus"]="Collected",
            ["permissionReads"]=new JsonArray(Read("FullAccess",true),Read("SendAs",false),Read("SendOnBehalf",false))
        }];
        report.Sections[1].Id="permissions";
        report.Sections[1].Rows=[new JsonObject
        {
            ["id"]=ExchangeObservationKey.MailboxPermission(Mailbox,"FullAccess","RawIdentity",Principal,["FullAccess"],false,false),
            ["name"]=null,["readStatus"]="Collected",["error"]=null,["mailboxExchangeGuid"]=Mailbox,["permissionKind"]="FullAccess",
            ["principal"]=new JsonObject{["namespace"]="RawIdentity",["value"]=Principal,["externalDirectoryObjectId"]=null},
            ["rights"]=InboxRuleEvidenceV2Tests.Value(new JsonArray("FullAccess")),["isInherited"]=InboxRuleEvidenceV2Tests.Value(false),["deny"]=InboxRuleEvidenceV2Tests.Value(false)
        }];return report;
    }
    [Fact]
    public void Exact_permission_sources_and_unknown_flags_roundtrip_without_effective_access_or_name_joins()
    {
        var report=InboxRuleEvidenceV2Tests.Read(Report());var row=Assert.Single(report.Sections[1].Rows);
        Assert.Equal(Principal,row["principal"]!["value"]!.GetValue<string>());Assert.Null(row["principal"]!["externalDirectoryObjectId"]);
        Assert.False(row["isInherited"]!["value"]!.GetValue<bool>());Assert.False(row["deny"]!["value"]!.GetValue<bool>());
    }
    [Fact]
    public void Shared_tables_label_raw_principals_and_each_selected_or_unselected_source_without_raw_json()
    {
        var report=InboxRuleEvidenceV2Tests.Read(Report());
        var sheets=BuildStandard.Engine.Reports.RegisteredReportDocuments.Sheets(report);
        var edges=sheets.Single(sheet=>sheet.Name=="permissions");var targets=sheets.Single(sheet=>sheet.Name=="target-reads");
        Assert.Equal("Raw identity: "+Principal,edges.Rows[1][Array.IndexOf(edges.Rows[0],"Principal")]);
        var reads=targets.Rows[1][Array.IndexOf(targets.Rows[0],"Permission reads")];
        Assert.Contains("FullAccess: Collected",reads);Assert.Contains("SendAs: Not selected",reads);Assert.Contains("SendOnBehalf: Not selected",reads);
        Assert.DoesNotContain("{",reads);Assert.Equal("False (reported)",edges.Rows[1][Array.IndexOf(edges.Rows[0],"Deny")]);
    }
    [Theory][InlineData("Missing")][InlineData("ReturnedNull")]
    public void Unknown_inheritance_is_distinct_from_false_and_cannot_claim_complete_projection(string state)
    {
        var report=Report();var row=report.Sections[1].Rows[0];
        row["isInherited"]=new JsonObject{["state"]=state,["value"]=null,["reason"]="Synthetic unavailable inheritance"};
        row["id"]=ExchangeObservationKey.MailboxPermission(Mailbox,"FullAccess","RawIdentity",Principal,["FullAccess"],null,false);
        row["readStatus"]="Partial";row["error"]="Synthetic unknown inheritance";
        var target=report.Sections[0].Rows[0];target["readStatus"]="Partial";target["error"]="Synthetic unknown inheritance";
        target["permissionReads"]![0]!["status"]="Partial";target["permissionReads"]![0]!["error"]="Synthetic unknown inheritance";
        foreach(var section in report.Sections){section.Status="Partial";section.Error="Synthetic incomplete values";}report.Status="Partial";
        var admitted=InboxRuleEvidenceV2Tests.Read(report);Assert.Null(admitted.Sections[1].Rows[0]["isInherited"]!["value"]);
    }
    [Theory][InlineData("unselected")][InlineData("missing-target")][InlineData("no-kind-selected")][InlineData("unknown-principal")][InlineData("invented-false")][InlineData("duplicate")][InlineData("foreign-parent")][InlineData("missing-kind")]
    public void Resealed_scope_identity_and_missing_source_read_claims_are_refused(string fault)
    {
        var report=Report();var row=report.Sections[1].Rows[0];
        switch(fault)
        {
            case "unselected":report.Parameters[1].Value="false";report.Parameters[2].Value="true";break;
            case "missing-target":report.Sections[0].Rows.Clear();break;
            case "no-kind-selected":report.Parameters[1].Value="false";break;
            case "unknown-principal":row["principal"]!["namespace"]="DisplayNameResolvedToEntra";break;
            case "invented-false":row["deny"]!["state"]="Missing";row["deny"]!["reason"]="Unknown";break;
            case "duplicate":report.Sections[1].Rows.Add((JsonObject)row.DeepClone());break;
            case "foreign-parent":row["mailboxExchangeGuid"]=TestData.TenantB;break;
            case "missing-kind":report.Sections[0].Rows[0]["permissionReads"]!.AsArray().RemoveAt(2);break;
        }
        Assert.ThrowsAny<ToolkitException>(()=>InboxRuleEvidenceV2Tests.Read(report));
    }
}
