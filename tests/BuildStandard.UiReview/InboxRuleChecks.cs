using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
internal static partial class Program
{
    private static void PopulateInboxNativeFixture(ExchangeReportEvidence evidence)
    {
        const string mailbox="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",rule="18446744073709551615";
        static ExchangeObserved<bool?> Flag(bool value)=>new(){State="Value",Value=value};
        static ExchangeObserved<List<ExchangeReferenceValue>> Empty()=>new(){State="Value",Value=[]};
        evidence.ReportSchemaVersion=2;evidence.AdapterVersion=ExchangeReportRegistry.FamilyAdapterVersion;
        evidence.Sections=
        [
            new(){Id="target-reads",Status="Partial",Error="Synthetic unknown rule field; inspect the value state.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangeTargetReadRow
            {
                Id=ExchangeObservationKey.TargetRead(evidence.ReportId,mailbox),Name="Synthetic selected mailbox",ReadStatus="Partial",Error="Synthetic incomplete rule fields",
                RequestedExchangeGuid=mailbox,ObservedExchangeGuid=mailbox,LookupReadStatus="Collected",ChildReadStatus="Partial",ReturnedEntries=1,ExcludedEntries=0
            })]},
            new(){Id="rules",Status="Partial",Error="Synthetic unknown rule field; not an empty-success result.",Rows=[ExchangeReportEvidenceSchema.Row(new InboxRuleObservationRow
            {
                Id=ExchangeObservationKey.InboxRule(mailbox,rule),Name="Synthetic disabled rule",ReadStatus="Partial",Error="Synthetic recipient field unavailable",
                MailboxExchangeGuid=mailbox,RuleIdentity=rule,Enabled=Flag(false),Priority=new(){State="Value",Value=0},ForwardTo=new(){State="Missing",Reason="Synthetic recipients not returned"},
                ForwardAsAttachmentTo=Empty(),RedirectTo=Empty(),DeleteMessage=Flag(true),MarkAsRead=Flag(false),StopProcessingRules=Flag(true)
            })]}
        ];
    }
}
