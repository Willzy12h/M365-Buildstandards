using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts.Runner;
internal static partial class Program
{
    private static void PopulateTraceNativeFixture(ExchangeReportEvidence evidence,ReadRunContext context)
    {
        var query=context.TracePreparation?.Query??throw new InvalidOperationException("Native trace run omitted its exact frozen review.");
        evidence.ReportSchemaVersion=2;evidence.AdapterVersion=ExchangeReportRegistry.FamilyAdapterVersion;
        evidence.StartedAt=Timestamps.Format(query.FrozenNow);
        var windows=query.Windows.Select((window,index)=>ExchangeReportEvidenceSchema.Row(new ExchangeTraceIntervalRow
        {
            Id=ExchangeObservationKey.TraceInterval(Timestamps.Format(window.Start),Timestamps.Format(window.End),query.FilterDigest),
            WindowIndex=index,FrozenAtUtc=Timestamps.Format(query.FrozenNow),StartUtc=Timestamps.Format(window.Start),EndUtc=Timestamps.Format(window.End),FilterDigest=query.FilterDigest,
            RequestedResultSize=query.MaximumRows-query.Windows.Count,ReturnedEntries=index==0?1:0,ExcludedEntries=0,
            ReadStatus=index==0?"Partial":"Collected",Error=index==0?"Synthetic size was not returned.":null
        })).ToList();
        var received=query.Windows[0].Start.AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",System.Globalization.CultureInfo.InvariantCulture);
        const string recipient="recipient@synthetic.example";
        evidence.Sections=
        [
            new(){Id="interval-reads",Status="Partial",Error="Synthetic missing trace metadata.",Rows=windows},
            new(){Id="traces",Status="Partial",Error="Synthetic size was not returned; no capacity inference.",Rows=[ExchangeReportEvidenceSchema.Row(new ExchangeTraceRecipientRow
            {
                Id=ExchangeObservationKey.MessageTrace(query.MessageTraceId!,recipient,received),WindowIndex=0,MessageTraceId=query.MessageTraceId!,RecipientAddress=recipient,ReceivedUtc=received,
                ReadStatus="Partial",Error="Synthetic size was not returned.",SenderAddress=new(){State="Value",Value="sender@synthetic.example"},
                InternetMessageId=new(){State="Value",Value="<synthetic@message.invalid>"},Status=new(){State="Value",Value="Delivered"},
                Size=new(){State="Missing",Reason="Synthetic size was not returned."},Subject=new(){State="NotAttempted",Reason="Subject collection was not selected."}
            })]}
        ];
    }
}
