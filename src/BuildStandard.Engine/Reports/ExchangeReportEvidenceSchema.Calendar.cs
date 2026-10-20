using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public static partial class ExchangeReportEvidenceSchema
{
    private static string CalendarKey(CalendarPermissionObservationRow row)
    {
        if(row.Principal is null)throw Bad("A calendar ACL needs the original principal namespace/value.");
        return ExchangeObservationKey.CalendarPermission(row.MailboxExchangeGuid,row.NativeFolderIdentity,row.Principal.Namespace,row.Principal.Value);
    }
    private static bool CalendarList(ExchangeObserved<List<string>> value)=>FamilyValue(value,values=>
    {if(values.Count>100)throw Bad("Calendar ACL values exceed their100-value bound.");foreach(var item in values)FamilyIdentity(item);return true;});
    private static void ValidateCalendarRow(ExchangeReportRow row)
    {
        switch(row)
        {
            case ExchangeCalendarTargetRow target:
                CanonicalGuid(target.RequestedExchangeGuid);State(target.LookupReadStatus);State(target.FolderReadStatus);State(target.PermissionReadStatus);
                if(target.Id!=ExchangeObservationKey.TargetRead("exo-calendar-permissions",target.RequestedExchangeGuid))throw Bad("The calendar target key differs from its selected mailbox.");
                if(target.ExternalDirectoryObjectId is not null)CanonicalGuid(target.ExternalDirectoryObjectId);
                if(target.PrimarySmtpAddress is not null)FamilyIdentity(target.PrimarySmtpAddress);
                if(target.LookupReadStatus==ReportReadState.Collected)
                {if(target.ObservedExchangeGuid!=target.RequestedExchangeGuid)throw Bad("Calendar reads require the exact verified mailbox GUID.");}
                else if(target.ObservedExchangeGuid is not null||target.ExternalDirectoryObjectId is not null||target.PrimarySmtpAddress is not null||target.FolderReadStatus!=ReportReadState.NotAttempted)
                    throw Bad("An unverified mailbox cannot admit folder reads or claim returned identity.");
                if(target.FolderReadStatus is ReportReadState.Collected or ReportReadState.Partial)
                {
                    if(target.LookupReadStatus!=ReportReadState.Collected||target.ReturnedFolders is null or <0 or >5000||target.DefaultFolderMatches is null or <0||target.DefaultFolderMatches>target.ReturnedFolders)
                        throw Bad("Folder discovery needs verified scope and explicit bounded match counts.");
                }
                else if(target.ReturnedFolders is not null||target.DefaultFolderMatches is not null)
                    throw Bad("Unsuccessful discovery counts are unknown, never zero.");
                if(target.FolderReadStatus==ReportReadState.Collected)
                {
                    if(target.DefaultFolderMatches!=1)throw Bad("Only a unique returned default Calendar is admissible.");
                    FamilyIdentity(target.FolderId);
                    if(target.FolderPath is null||target.NativeFolderIdentity!=ExchangeCalendarIdentity.FromReturnedPath(target.RequestedExchangeGuid,target.FolderPath))
                        throw Bad("The calendar argument differs from its returned path and verified mailbox.");
                }
                else if(target.FolderId is not null||target.FolderPath is not null||target.NativeFolderIdentity is not null||target.PermissionReadStatus!=ReportReadState.NotAttempted)
                    throw Bad("Failed/ambiguous discovery cannot choose a folder or claim its ACL.");
                if(target.PermissionReadStatus is ReportReadState.Collected or ReportReadState.Partial)
                {
                    if(target.FolderReadStatus!=ReportReadState.Collected||target.ReturnedEntries is null or <0 or >5000||target.ExcludedEntries is null or <0||target.ExcludedEntries>target.ReturnedEntries)
                        throw Bad("Calendar ACL reads need verified folder and bounded original/excluded counts.");
                }
                else if(target.ReturnedEntries is not null||target.ExcludedEntries is not null)throw Bad("Unknown ACL counts cannot become zero.");
                if(target.ReadStatus==ReportReadState.Collected&&(target.LookupReadStatus!=ReportReadState.Collected||target.FolderReadStatus!=ReportReadState.Collected||target.PermissionReadStatus!=ReportReadState.Collected||target.ExcludedEntries!=0))
                    throw Bad("A successful calendar target cannot conceal incomplete discovery or ACL reads.");
                break;
            case CalendarPermissionObservationRow edge:
                CanonicalGuid(edge.MailboxExchangeGuid);FamilyIdentity(edge.NativeFolderIdentity);
                if(edge.Id!=CalendarKey(edge))throw Bad("The calendar observation key differs from its original folder/principal tuple.");
                var all=FamilyReferences([edge.Principal]);all&=CalendarList(edge.Rights);all&=CalendarList(edge.SharingPermissionFlags);
                if(edge.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)||(!all&&edge.ReadStatus==ReportReadState.Collected))
                    throw Bad("Unknown ACL values/unresolved principals require partial status, never an effective-access claim.");
                break;
            default:throw Bad("Unregistered calendar row type.");
        }
    }
    private static void ValidateCalendarReport(ExchangeReportEvidence evidence)
    {
        var selected=ExchangeTargetSelection.Parse(evidence.Parameters.Single(parameter=>parameter.Name=="MailboxGuids").Value);
        var targetSection=evidence.Sections.Single(section=>section.Id=="target-reads");var aclSection=evidence.Sections.Single(section=>section.Id=="permissions");
        var targets=targetSection.Rows.Select(node=>JsonSerializer.Deserialize<ExchangeCalendarTargetRow>(node.ToJsonString(),Strict)!).ToArray();
        var edges=aclSection.Rows.Select(node=>JsonSerializer.Deserialize<CalendarPermissionObservationRow>(node.ToJsonString(),Strict)!).ToArray();
        if(targets.Length!=selected.Count||!selected.ToHashSet(StringComparer.Ordinal).SetEquals(targets.Select(target=>target.RequestedExchangeGuid)))
            throw Bad("Every selected mailbox requires a calendar discovery diagnostic, including failures.");
        if(edges.Any(edge=>!selected.Contains(edge.MailboxExchangeGuid,StringComparer.Ordinal)))throw Bad("Calendar ACL belongs to an unselected mailbox.");
        if(targets.Length+targets.Sum(target=>(target.ReturnedFolders??0)+(target.ReturnedEntries??0))>5000)
            throw Bad("Calendar discovery and ACL observations exceed the shared5,000-row budget.");
        foreach(var target in targets)
        {
            var children=edges.Where(edge=>edge.MailboxExchangeGuid==target.RequestedExchangeGuid).ToArray();
            if(children.Any(edge=>edge.NativeFolderIdentity!=target.NativeFolderIdentity))throw Bad("An ACL belongs to a different/unverified folder.");
            if(target.PermissionReadStatus is ReportReadState.Collected or ReportReadState.Partial)
            {
                if(target.ReturnedEntries!=children.Length+target.ExcludedEntries)throw Bad("Calendar ACL counts must account exactly for retained/excluded entries.");
                if(target.PermissionReadStatus==ReportReadState.Collected&&(target.ExcludedEntries!=0||children.Any(edge=>edge.ReadStatus!=ReportReadState.Collected)))
                    throw Bad("A successful ACL read cannot conceal unknown/excluded rows.");
            }
            else if(children.Length!=0)throw Bad("Failed/unattempted calendar reads cannot claim ACL observations.");
        }
        if((targetSection.Status==ReportReadState.Collected||aclSection.Status==ReportReadState.Collected)&&targets.Any(target=>target.ReadStatus!=ReportReadState.Collected))
            throw Bad("A successful empty ACL section cannot conceal unsuccessful parent discovery.");
        if(aclSection.Status==ReportReadState.NotAttempted&&targets.Any(target=>target.PermissionReadStatus!=ReportReadState.NotAttempted))
            throw Bad("Attempted ACL reads cannot become unattempted.");
        if(targetSection.Status==ReportReadState.Failed&&targets.Any(target=>target.ReadStatus!=ReportReadState.Failed))throw Bad("Failed target section hides retained target outcomes.");
    }
}
