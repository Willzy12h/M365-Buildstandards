using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
internal static partial class Program
{
    private static void PopulateTransportNativeFixture(ExchangeReportEvidence evidence)
    {
        const string rule="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa";
        evidence.ReportSchemaVersion=2;evidence.AdapterVersion=ExchangeReportRegistry.FamilyAdapterVersion;
        evidence.Sections=
        [
            new(){Id="target-reads",Status="Partial",Error="Synthetic missing metadata; no protection claim.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangeTransportReadRow
            {
                Id=ExchangeObservationKey.TransportRead("SelectedRule",rule),Name="Synthetic selected rule",ReadStatus="Partial",Error="Synthetic rule state not returned",
                Scope="SelectedRule",RequestedRuleGuid=rule,RequestedResultSize=2,ReturnedEntries=1,ExcludedEntries=0
            })]},
            new(){Id="policies",Status="Partial",Error="Synthetic missing metadata; no protection claim.",Rows=[ExchangeReportEvidenceSchema.Row(new TransportRuleObservationRow
            {
                Id=rule,RuleGuid=rule,Name="Synthetic transport rule",ReadStatus="Partial",Error="Synthetic rule state not returned",
                Priority=new(){State="Value",Value=0},State=new(){State="Missing",Reason="Synthetic rule state not returned"},Mode=new(){State="Value",Value="Audit"},
                ActivationDate=new(){State="Value",Value=""},ExpiryDate=new(){State="ReturnedNull",Reason="Synthetic expiry returned null"}
            })]}
        ];
    }
    private static void PopulateGroupNativeFixture(ExchangeReportEvidence evidence)
    {
        const string group="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",member="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        evidence.ReportSchemaVersion=2;evidence.AdapterVersion=ExchangeReportRegistry.FamilyAdapterVersion;
        evidence.Sections=
        [
            new(){Id="target-reads",Status="Partial",Error="Synthetic unresolved owner reference.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangeGroupTargetRow
            {
                Id=ExchangeObservationKey.TargetRead(evidence.ReportId,group),Name="Synthetic selected group",ReadStatus="Partial",Error="Synthetic owner is an unresolved label",
                RequestedGroupGuid=group,ObservedGroupGuid=group,LookupReadStatus="Collected",MembershipReadStatus="Collected",ReturnedEntries=1,ExcludedEntries=0,
                RecipientTypeDetails=new(){State="Value",Value="MailUniversalDistributionGroup"},ManagedBy=new(){State="Value",Value=[new(){Namespace="UnresolvedDisplayLabel",Value="Synthetic owner label"}]},
                MemberJoinRestriction=new(){State="Value",Value="Closed"},MemberDepartRestriction=new(){State="Value",Value="Open"},
                RequireSenderAuthenticationEnabled=new(){State="Value",Value=false},HiddenFromAddressListsEnabled=new(){State="Value",Value=false}
            })]},
            new(){Id="members",Status="Partial",Error="Synthetic owner context remains unresolved; direct member retained.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangeGroupMemberRow
            {
                Id=ExchangeObservationKey.GroupMember(group,"ExchangeRecipientGuid",member),Name="Synthetic direct member",ReadStatus="Collected",GroupGuid=group,
                SourceIdentity=new(){Namespace="ExchangeRecipientGuid",Value=member},PrimarySmtpAddress="member@synthetic.example",RecipientTypeDetails=new(){State="Value",Value="UserMailbox"}
            })]}
        ];
    }
    private static void PopulateCalendarNativeFixture(ExchangeReportEvidence evidence)
    {
        const string mailbox="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",path="/Kalender";
        var identity=ExchangeCalendarIdentity.FromReturnedPath(mailbox,path);
        evidence.ReportSchemaVersion=2;evidence.AdapterVersion=ExchangeReportRegistry.FamilyAdapterVersion;
        evidence.Sections=
        [
            new(){Id="target-reads",Status="Partial",Error="Synthetic unavailable ACL rights.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangeCalendarTargetRow
            {
                Id=ExchangeObservationKey.TargetRead(evidence.ReportId,mailbox),Name="Synthetic selected mailbox",ReadStatus="Partial",Error="Synthetic missing rights",
                RequestedExchangeGuid=mailbox,ObservedExchangeGuid=mailbox,LookupReadStatus="Collected",FolderReadStatus="Collected",ReturnedFolders=1,DefaultFolderMatches=1,
                FolderId="synthetic-store-folder-id",FolderPath=path,NativeFolderIdentity=identity,PermissionReadStatus="Partial",ReturnedEntries=1,ExcludedEntries=0
            })]},
            new(){Id="permissions",Status="Partial",Error="Synthetic unavailable rights; not effective access.",Rows=[ExchangeReportEvidenceSchema.Row(new CalendarPermissionObservationRow
            {
                Id=ExchangeObservationKey.CalendarPermission(mailbox,identity,"SpecialPrincipal","Default"),Name=null,ReadStatus="Partial",Error="Synthetic rights not returned",
                MailboxExchangeGuid=mailbox,NativeFolderIdentity=identity,Principal=new(){Namespace="SpecialPrincipal",Value="Default"},
                Rights=new(){State="Missing",Reason="Synthetic rights not returned"},SharingPermissionFlags=new(){State="Value",Value=[]}
            })]}
        ];
    }
    private static void PopulatePermissionNativeFixture(ExchangeReportEvidence evidence)
    {
        const string mailbox="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",principal="S-1-5-21-123-456-789-1001";
        evidence.ReportSchemaVersion=2;evidence.AdapterVersion=ExchangeReportRegistry.FamilyAdapterVersion;
        evidence.Sections=
        [
            new(){Id="target-reads",Status="Partial",Error="Synthetic unknown deny flag.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangePermissionTargetRow
            {
                Id=ExchangeObservationKey.TargetRead(evidence.ReportId,mailbox),Name="Synthetic selected mailbox",ReadStatus="Partial",Error="Synthetic incomplete ACL fields",
                RequestedExchangeGuid=mailbox,ObservedExchangeGuid=mailbox,LookupReadStatus="Collected",PermissionReads=
                [
                    new(){Kind="FullAccess",Selected=true,Status="Partial",Error="Synthetic unknown deny",ReturnedEntries=1,ExcludedEntries=0},
                    new(){Kind="SendAs",Selected=false,Status="NotAttempted",Error="Not selected"},
                    new(){Kind="SendOnBehalf",Selected=false,Status="NotAttempted",Error="Not selected"}
                ]
            })]},
            new(){Id="permissions",Status="Partial",Error="Synthetic unknown deny flag; not an effective access verdict.",Rows=[ExchangeReportEvidenceSchema.Row(new MailboxPermissionObservationRow
            {
                Id=ExchangeObservationKey.MailboxPermission(mailbox,"FullAccess","RawIdentity",principal,["FullAccess"],false,null),
                Name="Synthetic mailbox ACL observation",ReadStatus="Partial",Error="Synthetic deny flag not returned",MailboxExchangeGuid=mailbox,PermissionKind="FullAccess",
                Principal=new(){Namespace="RawIdentity",Value=principal},Rights=new(){State="Value",Value=["FullAccess"]},
                IsInherited=new(){State="Value",Value=false},Deny=new(){State="Missing",Reason="Synthetic deny flag was not returned"}
            })]}
        ];
    }
}
