using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
internal static partial class Program
{
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
