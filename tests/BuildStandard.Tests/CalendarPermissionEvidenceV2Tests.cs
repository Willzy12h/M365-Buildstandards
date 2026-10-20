using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using Xunit;
namespace BuildStandard.Tests;

public sealed class CalendarPermissionEvidenceV2Tests
{
    internal const string FolderPath="/Kalender",FolderId="returned-store-folder-id";
    internal static string NativeIdentity=>TestData.Office+":\\Kalender";
    internal static string Hash(params object?[] values)=>"sha256:"+Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
    internal static ExchangeReportEvidence Report()
    {
        var report=InboxRuleEvidenceV2Tests.Report();report.ReportId="exo-calendar-permissions";
        report.SourceCommands=["Get-EXOMailbox","Get-EXOMailboxFolderStatistics","Get-EXOMailboxFolderPermission"];
        report.Parameters=[new(){Name="MailboxGuids",Type="Text",Value=TestData.Office}];
        report.Sections[0].Rows=[new JsonObject {
            ["id"]=Hash(report.ReportId,TestData.Office),["name"]="Synthetic mailbox",["readStatus"]="Collected",["error"]=null,
            ["requestedExchangeGuid"]=TestData.Office,["observedExchangeGuid"]=TestData.Office,["externalDirectoryObjectId"]=null,["primarySmtpAddress"]=null,
            ["lookupReadStatus"]="Collected",["folderReadStatus"]="Collected",["returnedFolders"]=1,["defaultFolderMatches"]=1,
            ["folderId"]=FolderId,["folderPath"]=FolderPath,["nativeFolderIdentity"]=NativeIdentity,
            ["permissionReadStatus"]="Collected",["returnedEntries"]=1,["excludedEntries"]=0 }];
        report.Sections[1].Id="permissions";report.Sections[1].Rows=[new JsonObject {
            ["id"]=ExchangeObservationKey.CalendarPermission(TestData.Office,NativeIdentity,"SpecialPrincipal","Default"),
            ["name"]=null,["readStatus"]="Collected",["error"]=null,["mailboxExchangeGuid"]=TestData.Office,
            ["nativeFolderIdentity"]=NativeIdentity,["principal"]=new JsonObject{["namespace"]="SpecialPrincipal",["value"]="Default",["externalDirectoryObjectId"]=null},
            ["rights"]=InboxRuleEvidenceV2Tests.Value(new JsonArray("AvailabilityOnly")),["sharingPermissionFlags"]=InboxRuleEvidenceV2Tests.Value(new JsonArray()) }];
        return report;
    }
    [Fact] public void Localised_calendar_and_special_principal_roundtrip_without_English_name_or_effective_access_inference()
    {
        var report=InboxRuleEvidenceV2Tests.Read(Report());
        Assert.Equal(FolderPath,report.Sections[0].Rows[0]["folderPath"]!.GetValue<string>());
        Assert.Equal("Default",report.Sections[1].Rows[0]["principal"]!["value"]!.GetValue<string>());
        Assert.Empty(report.Sections[1].Rows[0]["sharingPermissionFlags"]!["value"]!.AsArray());
    }
    [Fact] public void Failed_folder_discovery_is_not_successful_empty_permissions()
    {
        var report=Report();var target=report.Sections[0].Rows[0];
        target["folderReadStatus"]="Failed";target["returnedFolders"]=null;target["defaultFolderMatches"]=null;
        target["folderId"]=null;target["folderPath"]=null;target["nativeFolderIdentity"]=null;
        target["permissionReadStatus"]="NotAttempted";target["returnedEntries"]=null;target["excludedEntries"]=null;
        target["readStatus"]="Partial";target["error"]="Synthetic failed folder discovery";
        report.Sections[1].Rows.Clear();foreach(var section in report.Sections){section.Status="Partial";section.Error="Synthetic failed folder discovery";}report.Status="Partial";
        var admitted=InboxRuleEvidenceV2Tests.Read(report);Assert.Empty(admitted.Sections[1].Rows);Assert.Equal("Partial",admitted.Status);
    }
    [Theory][InlineData("ambiguous")][InlineData("English-guess")][InlineData("path-traversal")][InlineData("other-mailbox")][InlineData("failed-parent")][InlineData("duplicate")][InlineData("unknown-with-value")][InlineData("foreign-principal")][InlineData("missing-target")][InlineData("counts")]
    public void Resealed_ambiguous_folder_identity_scope_and_unknown_claims_are_refused(string fault)
    {
        var report=Report();var target=report.Sections[0].Rows[0];var edge=report.Sections[1].Rows[0];
        switch(fault)
        {
            case "ambiguous":target["defaultFolderMatches"]=2;target["returnedFolders"]=2;break;
            case "English-guess":target["nativeFolderIdentity"]=TestData.Office+":\\Calendar";break;
            case "path-traversal":target["folderPath"]="/../Kalender";break;
            case "other-mailbox":edge["mailboxExchangeGuid"]=TestData.TenantB;break;
            case "failed-parent":target["lookupReadStatus"]="Failed";break;
            case "duplicate":report.Sections[1].Rows.Add((JsonObject)edge.DeepClone());break;
            case "unknown-with-value":edge["rights"]!["state"]="Missing";edge["rights"]!["reason"]="Synthetic unknown";break;
            case "foreign-principal":edge["principal"]!["namespace"]="ResolvedByDisplayName";break;
            case "missing-target":report.Sections[0].Rows.Clear();break;
            case "counts":target["returnedEntries"]=0;break;
        }
        Assert.ThrowsAny<ToolkitException>(()=>InboxRuleEvidenceV2Tests.Read(report));
    }
}
