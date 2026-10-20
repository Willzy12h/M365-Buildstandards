using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class InboxRuleEvidenceV2Tests
{
    private const string Mailbox=TestData.Office,Rule="184467440737095516150000";
    internal static JsonObject Value(JsonNode? value)=>new(){["state"]="Value",["value"]=value,["reason"]=null};
    internal static ExchangeReportEvidence Report()
    {
        var targetKey="sha256:"+Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]{"exo-inbox-rules",Mailbox})));
        var target=new JsonObject{["id"]=targetKey,["name"]="Synthetic mailbox",["readStatus"]="Collected",["error"]=null,["requestedExchangeGuid"]=Mailbox,["observedExchangeGuid"]=Mailbox,["externalDirectoryObjectId"]=TestData.Operator,["primarySmtpAddress"]="mailbox@synthetic.example",["lookupReadStatus"]="Collected",["childReadStatus"]="Collected",["returnedEntries"]=1,["excludedEntries"]=0};
        var row=new JsonObject{["id"]=ExchangeObservationKey.InboxRule(Mailbox,Rule),["name"]="Synthetic inbox rule",["readStatus"]="Collected",["error"]=null,["mailboxExchangeGuid"]=Mailbox,["ruleIdentity"]=Rule,["actualIdentityRaw"]="original opaque identity",["enabled"]=Value(false),["priority"]=Value(0),["forwardTo"]=Value(new JsonArray()),["forwardAsAttachmentTo"]=Value(new JsonArray()),["redirectTo"]=Value(new JsonArray()),["deleteMessage"]=Value(false),["markAsRead"]=Value(false),["stopProcessingRules"]=Value(true)};
        return new(){Id=Guid.NewGuid().ToString(),RunId=Guid.NewGuid().ToString(),ReportId="exo-inbox-rules",ReportSchemaVersion=2,AdapterVersion="registered-exchange-report/2",TenantId=TestData.TenantA,InitiatingAccountObjectId=TestData.Operator,ObservedAccountUpn="operator@synthetic.example",Resource="ExchangeOnline",SourceMode="live",StartedAt="2026-10-20T00:00:00Z",EndedAt="2026-10-20T00:01:00Z",ToolkitVersion="synthetic",ModuleVersion="99.0.0",RuntimeVersion="7.6.7",ScriptSha256=new string('a',64),ManifestSha256=new string('b',64),RunnerTemplateSha256=new string('c',64),SourceCommands=["Get-EXOMailbox","Get-InboxRule"],Parameters=[new(){Name="MailboxGuids",Type="Text",Value=Mailbox},new(){Name="IncludeHidden",Type="Boolean",Value="false"}],Status="Collected",Limitations=["Synthetic observations; hidden rules omitted, not an incident verdict."],Sections=[new(){Id="target-reads",Status="Collected",Rows=[target]},new(){Id="rules",Status="Collected",Rows=[row]}]};
    }
    internal static ExchangeReportEvidence Read(ExchangeReportEvidence e)
    {e.IntegrityDigest=EvidenceIntegrity.Compute(e);return ExchangeReportEvidenceSchema.Read(ExchangeReportEvidenceSchema.Serialize(e),TestData.TenantA);}
    [Fact] public void Exact_registration_roundtrips_opaque_rule_identity_and_composite_keys()
    {var e=Read(Report());Assert.Equal(2,e.ReportSchemaVersion);Assert.Equal(Rule,e.Sections[1].Rows[0]["ruleIdentity"]!.GetValue<string>());Assert.False(e.Sections[1].Rows[0]["enabled"]!["value"]!.GetValue<bool>());Assert.Empty(e.Sections[1].Rows[0]["forwardTo"]!["value"]!.AsArray());}
    [Theory][InlineData("adapter")][InlineData("revision")][InlineData("guid-key")][InlineData("changed-rule")][InlineData("foreign-mailbox")][InlineData("duplicate")][InlineData("missing-target")][InlineData("failed-parent")][InlineData("hidden-relabel")][InlineData("invented-value")][InlineData("content")]
    public void Resealed_unsupported_versions_identity_scope_and_value_claims_are_refused(string defect)
    {var e=Report();var row=e.Sections[1].Rows[0];switch(defect){case "adapter":e.AdapterVersion="registered-exchange-report/1";break;case "revision":e.ReportSchemaVersion=1;break;case "guid-key":row["id"]=TestData.TenantB;break;case "changed-rule":row["ruleIdentity"]="different opaque identity";break;case "foreign-mailbox":row["mailboxExchangeGuid"]=TestData.TenantB;break;case "duplicate":e.Sections[1].Rows.Add((JsonObject)row.DeepClone());break;case "missing-target":e.Sections[0].Rows.Clear();break;case "failed-parent":e.Sections[0].Rows[0]["lookupReadStatus"]="Failed";break;case "hidden-relabel":e.Parameters[1].Value="False";break;case "invented-value":row["enabled"]!["state"]="Missing";break;case "content":row["subjectContainsWords"]=new JsonArray("unnecessary content");break;}Assert.ThrowsAny<ToolkitException>(()=>Read(e));}
    private static void Partial(ExchangeReportEvidence e)
    {foreach(var section in e.Sections){section.Status="Partial";section.Error="Some original values could not be established";foreach(var row in section.Rows){row["readStatus"]="Partial";row["error"]="Some original values could not be established";}}e.Sections[0].Rows[0]["childReadStatus"]="Partial";e.Status="Partial";}
    [Theory][InlineData("ReturnedNull")][InlineData("Missing")][InlineData("Failed")][InlineData("Unsupported")]
    public void Unknown_flags_remain_explicit_and_cannot_be_promoted_to_known_false(string state)
    {var e=Report();Partial(e);var field=e.Sections[1].Rows[0]["enabled"]!.AsObject();field["state"]=state;field["value"]=null;field["reason"]="Synthetic original value unavailable";var read=Read(e);Assert.Equal(state,read.Sections[1].Rows[0]["enabled"]!["state"]!.GetValue<string>());Assert.Null(read.Sections[1].Rows[0]["enabled"]!["value"]);field["value"]=false;Assert.ThrowsAny<ToolkitException>(()=>Read(e));}
    [Fact] public void Unresolved_reference_labels_are_preserved_as_partial_without_a_directory_join()
    {var e=Report();Partial(e);e.Sections[1].Rows[0]["forwardTo"]=Value(new JsonArray(new JsonObject{["namespace"]="UnresolvedDisplayLabel",["value"]="same friendly label",["externalDirectoryObjectId"]=null}));Assert.Equal("Partial",Read(e).Status);e.Sections[1].Rows[0]["readStatus"]="Collected";e.Sections[1].Rows[0]["error"]=null;Assert.ThrowsAny<ToolkitException>(()=>Read(e));}
    [Fact] public void Projection_uses_local_key_label_and_explicit_hidden_rule_scope()
    {var e=Read(Report());var sheets=RegisteredReportDocuments.Sheets(e);Assert.Contains("Local observation key (not a service ID)",sheets[1].Rows[0]);Assert.Contains("Local observation key (not a service ID)",sheets[2].Rows[0]);Assert.DoesNotContain("Exchange mailbox GUID",sheets[2].Rows[0]);Assert.Contains(sheets[0].Rows,row=>row.Any(cell=>cell.Contains("Hidden rules omitted",StringComparison.Ordinal)));}
    [Fact] public void Unsuccessful_target_is_recorded_and_cannot_become_successful_empty_child_section()
    {var e=Report();var target=e.Sections[0].Rows[0];target["observedExchangeGuid"]=null;target["externalDirectoryObjectId"]=null;target["primarySmtpAddress"]=null;target["lookupReadStatus"]="Failed";target["childReadStatus"]="NotAttempted";target["readStatus"]="Failed";target["error"]="Synthetic lookup failed";target["returnedEntries"]=null;target["excludedEntries"]=null;e.Sections[0].Status="Failed";e.Sections[0].Error="Synthetic lookup failed";e.Sections[1].Rows.Clear();e.Sections[1].Status="NotAttempted";e.Sections[1].Error="Lookup failed; no rule read";e.Status=ReportEvidenceSchema.Overall(e.Sections.Select(s=>s.Status));Assert.NotEqual("Collected",Read(e).Status);e.Sections[1].Status="Collected";e.Sections[1].Error=null;e.Status=ReportEvidenceSchema.Overall(e.Sections.Select(s=>s.Status));Assert.ThrowsAny<ToolkitException>(()=>Read(e));}
}
