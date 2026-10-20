using System.Text.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;

public static partial class ExchangeReportEvidenceSchema
{
    private static bool SupportedGroup(string? kind)=>kind is "MailUniversalDistributionGroup" or "MailUniversalSecurityGroup" or "RoomList";
    private static string GroupKey(ExchangeGroupMemberRow row)
    {
        if(row.SourceIdentity is null)throw Bad("A direct member needs its original source namespace/value.");
        return ExchangeObservationKey.GroupMember(row.GroupGuid,row.SourceIdentity.Namespace,row.SourceIdentity.Value);
    }
    private static bool GroupReference(ExchangeReferenceValue reference)
    {
        if(reference is null)throw Bad("An original group reference is missing.");
        if(reference.Namespace=="ExchangeRecipientGuid")
        {
            CanonicalGuid(reference.Value);
            if(reference.ExternalDirectoryObjectId is not null)CanonicalGuid(reference.ExternalDirectoryObjectId);
            return true;
        }
        if(reference.Namespace=="SpecialPrincipal")throw Bad("Calendar special principals are not group-member identities.");
        return FamilyReferences([reference]);
    }
    private static bool GroupText(ExchangeObserved<string> value)=>FamilyValue(value,text=>{FamilyIdentity(text);return true;});
    private static void ValidateGroupRow(ExchangeReportRow row)
    {
        switch(row)
        {
            case ExchangeGroupTargetRow target:
                CanonicalGuid(target.RequestedGroupGuid);State(target.LookupReadStatus);State(target.MembershipReadStatus);
                if(target.Id!=ExchangeObservationKey.TargetRead("exo-distribution-groups",target.RequestedGroupGuid))throw Bad("The group target key differs from its selected service GUID.");
                if(target.ExternalDirectoryObjectId is not null)CanonicalGuid(target.ExternalDirectoryObjectId);
                if(target.PrimarySmtpAddress is not null)FamilyIdentity(target.PrimarySmtpAddress);
                var known=GroupText(target.RecipientTypeDetails);
                known&=FamilyValue(target.ManagedBy,owners=>{if(owners.Count>100)throw Bad("Group owners exceed the100-value bound.");var all=true;foreach(var owner in owners)all&=GroupReference(owner);return all;});
                known&=GroupText(target.MemberJoinRestriction);known&=GroupText(target.MemberDepartRestriction);
                known&=FamilyValue(target.RequireSenderAuthenticationEnabled);known&=FamilyValue(target.HiddenFromAddressListsEnabled);
                if(target.LookupReadStatus==ReportReadState.Collected)
                {if(target.ObservedGroupGuid!=target.RequestedGroupGuid)throw Bad("Membership reads require the exact verified group GUID.");}
                else if(target.ObservedGroupGuid is not null||target.ExternalDirectoryObjectId is not null||target.PrimarySmtpAddress is not null||target.MembershipReadStatus!=ReportReadState.NotAttempted
                    ||target.RecipientTypeDetails.State=="Value"||target.ManagedBy.State=="Value"||target.MemberJoinRestriction.State=="Value"||target.MemberDepartRestriction.State=="Value"
                    ||target.RequireSenderAuthenticationEnabled.State=="Value"||target.HiddenFromAddressListsEnabled.State=="Value")
                    throw Bad("Unverified group lookup cannot claim original configuration or members.");
                if(target.MembershipReadStatus!=ReportReadState.NotAttempted&&(target.LookupReadStatus!=ReportReadState.Collected||target.RecipientTypeDetails.State!="Value"||!SupportedGroup(target.RecipientTypeDetails.Value)))
                    throw Bad("Unknown, dynamic or Microsoft365 group types cannot admit this direct-member adapter.");
                if(target.MembershipReadStatus is ReportReadState.Collected or ReportReadState.Partial)
                {
                    if(target.ReturnedEntries is null or <0 or >5000||target.ExcludedEntries is null or <0||target.ExcludedEntries>target.ReturnedEntries)
                        throw Bad("Membership reads require bounded original/excluded counts.");
                }
                else if(target.ReturnedEntries is not null||target.ExcludedEntries is not null)throw Bad("Unknown member counts cannot become known zero.");
                if(target.ReadStatus==ReportReadState.Collected&&(!known||target.LookupReadStatus!=ReportReadState.Collected||target.MembershipReadStatus!=ReportReadState.Collected||target.ExcludedEntries!=0))
                    throw Bad("A successful group target cannot conceal unavailable configuration or membership.");
                break;
            case ExchangeGroupMemberRow member:
                CanonicalGuid(member.GroupGuid);
                if(member.Id!=GroupKey(member))throw Bad("The direct-member key differs from its original group/source tuple.");
                var all=GroupReference(member.SourceIdentity);all&=GroupText(member.RecipientTypeDetails);
                if(member.PrimarySmtpAddress is not null)FamilyIdentity(member.PrimarySmtpAddress);
                if(member.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)||(!all&&member.ReadStatus==ReportReadState.Collected))
                    throw Bad("Unresolved members/unknown source values require partial status, not effective delivery.");
                break;
            default:throw Bad("Unregistered distribution-group row type.");
        }
    }
    private static void ValidateGroupReport(ExchangeReportEvidence evidence)
    {
        var selected=ExchangeTargetSelection.Parse(evidence.Parameters.Single(parameter=>parameter.Name=="GroupGuids").Value);
        var targetSection=evidence.Sections.Single(section=>section.Id=="target-reads");var memberSection=evidence.Sections.Single(section=>section.Id=="members");
        var targets=targetSection.Rows.Select(node=>JsonSerializer.Deserialize<ExchangeGroupTargetRow>(node.ToJsonString(),Strict)!).ToArray();
        var members=memberSection.Rows.Select(node=>JsonSerializer.Deserialize<ExchangeGroupMemberRow>(node.ToJsonString(),Strict)!).ToArray();
        if(targets.Length!=selected.Count||!selected.ToHashSet(StringComparer.Ordinal).SetEquals(targets.Select(target=>target.RequestedGroupGuid)))
            throw Bad("Every selected group needs a read diagnostic, including failures.");
        if(members.Any(member=>!selected.Contains(member.GroupGuid,StringComparer.Ordinal)))throw Bad("Member belongs to an unselected group.");
        if(targets.Length+targets.Sum(target=>(target.ReturnedEntries??0)+(target.ManagedBy.Value?.Count??0))>5000)
            throw Bad("Group diagnostics, owner references and direct-member observations exceed the shared5,000-row budget.");
        foreach(var target in targets)
        {
            var children=members.Where(member=>member.GroupGuid==target.RequestedGroupGuid).ToArray();
            if(target.MembershipReadStatus is ReportReadState.Collected or ReportReadState.Partial)
            {
                if(target.ReturnedEntries!=children.Length+target.ExcludedEntries)throw Bad("Direct-member counts must account exactly for retained/excluded entries.");
                if(target.MembershipReadStatus==ReportReadState.Collected&&(target.ExcludedEntries!=0||children.Any(member=>member.ReadStatus!=ReportReadState.Collected)))
                    throw Bad("A successful member read cannot conceal unresolved/excluded observations.");
            }
            else if(children.Length!=0)throw Bad("Failed/unattempted group reads cannot claim members.");
        }
        if((targetSection.Status==ReportReadState.Collected||memberSection.Status==ReportReadState.Collected)&&targets.Any(target=>target.ReadStatus!=ReportReadState.Collected))
            throw Bad("Successful empty membership cannot conceal unsuccessful group lookup/configuration.");
        if(memberSection.Status==ReportReadState.NotAttempted&&targets.Any(target=>target.MembershipReadStatus!=ReportReadState.NotAttempted))
            throw Bad("Attempted membership reads cannot be relabelled unattempted.");
        if(targetSection.Status==ReportReadState.Failed&&targets.Any(target=>target.ReadStatus!=ReportReadState.Failed))throw Bad("Failed target section conceals retained group outcomes.");
    }
}
