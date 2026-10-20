using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public static partial class ExchangeReportEvidenceSchema
{
    private static readonly string[] PermissionKinds = ["FullAccess", "SendAs", "SendOnBehalf"];
    private static string PermissionKey(MailboxPermissionObservationRow row)
    {
        if(row.Principal is null) throw Bad("A permission observation needs its original principal namespace/value.");
        return ExchangeObservationKey.MailboxPermission(row.MailboxExchangeGuid,row.PermissionKind,row.Principal.Namespace,
            row.Principal.Value,row.Rights?.Value,row.IsInherited?.Value,row.Deny?.Value);
    }
    private static void ValidatePermissionRow(ExchangeReportRow row)
    {
        switch(row)
        {
            case ExchangePermissionTargetRow target:
                CanonicalGuid(target.RequestedExchangeGuid);State(target.LookupReadStatus);
                if(target.Id!=ExchangeObservationKey.TargetRead("exo-mailbox-permissions",target.RequestedExchangeGuid))throw Bad("The permission target key differs from its exact mailbox GUID.");
                if(target.ExternalDirectoryObjectId is not null)CanonicalGuid(target.ExternalDirectoryObjectId);
                if(target.PrimarySmtpAddress is not null)FamilyIdentity(target.PrimarySmtpAddress);
                if(target.LookupReadStatus==ReportReadState.Collected)
                {if(target.ObservedExchangeGuid!=target.RequestedExchangeGuid)throw Bad("Child permissions need the exact verified mailbox GUID.");}
                else if(target.ObservedExchangeGuid is not null||target.ExternalDirectoryObjectId is not null||target.PrimarySmtpAddress is not null)
                    throw Bad("An unsuccessful lookup cannot claim returned mailbox identity.");
                if(target.PermissionReads is null||target.PermissionReads.Count!=3||target.PermissionReads.Any(read=>read is null)
                    ||!target.PermissionReads.Select(read=>read.Kind).SequenceEqual(PermissionKinds,StringComparer.Ordinal))
                    throw Bad("Each mailbox needs separate FullAccess, SendAs and SendOnBehalf diagnostics in registered order.");
                foreach(var read in target.PermissionReads)
                {
                    State(read.Status);
                    if(read.Status==ReportReadState.Collected)
                    {if(read.Error is not null)throw Bad("A successful permission source cannot hide an error.");}
                    else {if(string.IsNullOrWhiteSpace(read.Error))throw Bad("An unattempted, failed or partial permission source needs a reason.");Text(read.Error);}
                    if(read.Status is ReportReadState.Collected or ReportReadState.Partial)
                    {
                        if(!read.Selected||target.LookupReadStatus!=ReportReadState.Collected||read.ReturnedEntries is null or <0 or >5000
                            ||read.ExcludedEntries is null or <0||read.ExcludedEntries>read.ReturnedEntries)
                            throw Bad("A permission source needs verified scope and bounded original/excluded counts.");
                    }
                    else if(read.ReturnedEntries is not null||read.ExcludedEntries is not null)
                        throw Bad("Unsuccessful permission counts are unknown, never a known zero.");
                    if((!read.Selected||target.LookupReadStatus!=ReportReadState.Collected)&&read.Status!=ReportReadState.NotAttempted)
                        throw Bad("Unselected/unverified sources cannot claim a permission read.");
                }
                if(target.ReadStatus==ReportReadState.Collected&&(target.LookupReadStatus!=ReportReadState.Collected
                    ||target.PermissionReads.Where(read=>read.Selected).Any(read=>read.Status!=ReportReadState.Collected||read.ExcludedEntries!=0)))
                    throw Bad("A successful permission target cannot conceal unknown or excluded source observations.");
                break;
            case MailboxPermissionObservationRow edge:
                CanonicalGuid(edge.MailboxExchangeGuid);
                if(edge.Id!=PermissionKey(edge))throw Bad("The permission key differs from its exact principal, rights and flag tuple.");
                var all=FamilyReferences([edge.Principal]);
                all&=FamilyValue(edge.Rights,values=>{if(values.Count>100)throw Bad("Permission rights exceed their100-value bound.");foreach(var value in values)FamilyIdentity(value);return true;});
                all&=FamilyValue(edge.IsInherited);all&=FamilyValue(edge.Deny);
                if(edge.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)||(!all&&edge.ReadStatus==ReportReadState.Collected))
                    throw Bad("Unknown permission flags/rights or unresolved principals require a partial row, never an effective-access claim.");
                break;
            default:throw Bad("Unregistered permission row type.");
        }
    }
    private static void ValidatePermissionReport(ExchangeReportEvidence evidence)
    {
        var selected=ExchangeTargetSelection.Parse(evidence.Parameters.Single(parameter=>parameter.Name=="MailboxGuids").Value);
        var kinds=evidence.Parameters.Where(parameter=>PermissionKinds.Contains(parameter.Name,StringComparer.Ordinal))
            .ToDictionary(parameter=>parameter.Name,parameter=>parameter.Value=="true",StringComparer.Ordinal);
        if(!kinds.Values.Any(value=>value))throw Bad("Select at least one permission source; an unrequested inventory is not an empty successful result.");
        var targetSection=evidence.Sections.Single(section=>section.Id=="target-reads");
        var permissionSection=evidence.Sections.Single(section=>section.Id=="permissions");
        var targets=targetSection.Rows.Select(node=>JsonSerializer.Deserialize<ExchangePermissionTargetRow>(node.ToJsonString(),Strict)!).ToArray();
        var edges=permissionSection.Rows.Select(node=>JsonSerializer.Deserialize<MailboxPermissionObservationRow>(node.ToJsonString(),Strict)!).ToArray();
        if(targets.Length!=selected.Count||!selected.ToHashSet(StringComparer.Ordinal).SetEquals(targets.Select(target=>target.RequestedExchangeGuid)))
            throw Bad("Every selected mailbox needs exactly one permission-target diagnostic.");
        if(edges.Any(edge=>!selected.Contains(edge.MailboxExchangeGuid,StringComparer.Ordinal)||!kinds[edge.PermissionKind]))
            throw Bad("A permission edge belongs to an unselected mailbox or permission source.");
        if(targets.Length+targets.SelectMany(target=>target.PermissionReads).Sum(read=>read.ReturnedEntries??0)>5000)
            throw Bad("Permission observations and target diagnostics exceed the total5,000-row budget.");
        foreach(var target in targets)
        foreach(var read in target.PermissionReads)
        {
            if(read.Selected!=kinds[read.Kind])throw Bad("A source diagnostic differs from the exact reviewed permission selection.");
            var children=edges.Where(edge=>edge.MailboxExchangeGuid==target.RequestedExchangeGuid&&edge.PermissionKind==read.Kind).ToArray();
            if(read.Status is ReportReadState.Collected or ReportReadState.Partial)
            {
                if(read.ReturnedEntries!=children.Length+read.ExcludedEntries)throw Bad("Each permission source accounts exactly for retained/excluded observations.");
                if(read.Status==ReportReadState.Collected&&(read.ExcludedEntries!=0||children.Any(edge=>edge.ReadStatus!=ReportReadState.Collected)))
                    throw Bad("A complete permission source cannot hide excluded or unknown edges.");
            }
            else if(children.Length!=0)throw Bad("An unsuccessful/unattempted permission source cannot claim child observations.");
        }
        if((targetSection.Status==ReportReadState.Collected||permissionSection.Status==ReportReadState.Collected)
            &&targets.Any(target=>target.ReadStatus!=ReportReadState.Collected))
            throw Bad("A successful permission section cannot conceal failed or partial selected targets.");
        if(permissionSection.Status==ReportReadState.NotAttempted&&targets.SelectMany(target=>target.PermissionReads).Any(read=>read.Status!=ReportReadState.NotAttempted))
            throw Bad("Attempted permission reads cannot be relabelled unattempted.");
        if(targetSection.Status==ReportReadState.Failed&&targets.Any(target=>target.ReadStatus!=ReportReadState.Failed))
            throw Bad("A failed target section cannot conceal retained successful/partial targets.");
    }
}
