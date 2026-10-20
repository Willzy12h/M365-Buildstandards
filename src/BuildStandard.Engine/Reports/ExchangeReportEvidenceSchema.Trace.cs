using System.Globalization;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
namespace BuildStandard.Engine.Reports;
public static partial class ExchangeReportEvidenceSchema
{
    private static bool TraceTime(string value,string format,out DateTimeOffset time)=>DateTimeOffset.TryParseExact(value,format,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out time);
    private static bool TraceFrameTime(string value,out DateTimeOffset time)=>TraceTime(value,"yyyy-MM-dd'T'HH:mm:ss.fff'Z'",out time);
    private static void ValidateTraceRow(ExchangeReportRow row)
    {
        switch(row)
        {
            case ExchangeTraceIntervalRow interval:
                if(interval.WindowIndex is <0 or >8||!TraceFrameTime(interval.FrozenAtUtc,out _)||!TraceFrameTime(interval.StartUtc,out var first)||!TraceFrameTime(interval.EndUtc,out var last)||last<=first||last-first>TimeSpan.FromDays(10))throw Bad("Invalid frozen trace interval clock/order.");
                if(interval.Id!=ExchangeObservationKey.TraceInterval(interval.StartUtc,interval.EndUtc,interval.FilterDigest))throw Bad("Interval key differs from its exact original tuple.");
                if(interval.RequestedResultSize is not null and (<1 or >5000))throw Bad("Trace source cap is not bounded.");
                if(interval.ReadStatus is ReportReadState.Collected or ReportReadState.Partial)
                {
                    if(interval.RequestedResultSize is null||interval.ReturnedEntries is null or <0||interval.ExcludedEntries is null or <0||interval.ExcludedEntries>interval.ReturnedEntries||interval.ReturnedEntries>interval.RequestedResultSize)throw Bad("Trace reads require actual bounded/excluded counts.");
                    if(interval.ReadStatus==ReportReadState.Collected&&(interval.ExcludedEntries!=0||interval.ReturnedEntries==interval.RequestedResultSize))throw Bad("Saturated/excluded intervals cannot claim completeness.");
                }
                else if(interval.ReturnedEntries is not null||interval.ExcludedEntries is not null)throw Bad("Failed/unattempted trace counts are unknown, not zero.");
                if(interval.ReadStatus==ReportReadState.NotAttempted&&interval.RequestedResultSize is not null)throw Bad("An unattempted interval cannot claim a requested source cap.");
                break;
            case ExchangeTraceRecipientRow trace:
                if(trace.WindowIndex is <0 or >8||!TraceTime(trace.ReceivedUtc,"yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",out _))throw Bad("Trace identity needs its canonical observed UTC time.");
                CanonicalGuid(trace.MessageTraceId);FamilyIdentity(trace.RecipientAddress);
                if(trace.Id!=ExchangeObservationKey.MessageTrace(trace.MessageTraceId,trace.RecipientAddress,trace.ReceivedUtc))throw Bad("Trace key differs from its original recipient tuple.");
                var known=FamilyValue(trace.SenderAddress,value=>{FamilyIdentity(value);return true;});
                known&=FamilyValue(trace.InternetMessageId,value=>{Text(value);return true;});known&=FamilyValue(trace.Status,value=>{FamilyIdentity(value);return true;});known&=FamilyValue(trace.Size,value=>{Text(value);return true;});
                if(trace.Subject is null)throw Bad("A required subject scope state is missing.");
                if(trace.Subject.State!="NotAttempted")known&=FamilyValue(trace.Subject,value=>{Text(value);return true;});
                else if(trace.Subject.Value is not null||trace.Subject.Reason!="Subject collection was not selected.")throw Bad("Unselected subject needs its explicit omitted state.");
                if(trace.ReadStatus is not (ReportReadState.Collected or ReportReadState.Partial)||(!known&&trace.ReadStatus==ReportReadState.Collected))throw Bad("Unknown trace metadata cannot become a successful row.");
                break;
            default:throw Bad("Unregistered trace row type.");
        }
    }
    private static void ValidateTraceReport(ExchangeReportEvidence evidence)
    {
        var intervalSection=evidence.Sections.Single(s=>s.Id=="interval-reads");var traceSection=evidence.Sections.Single(s=>s.Id=="traces");
        var intervals=intervalSection.Rows.Select(node=>JsonSerializer.Deserialize<ExchangeTraceIntervalRow>(node.ToJsonString(),Strict)!).ToArray();
        var traces=traceSection.Rows.Select(node=>JsonSerializer.Deserialize<ExchangeTraceRecipientRow>(node.ToJsonString(),Strict)!).ToArray();
        if(intervals.Length is <1 or >9||!TraceFrameTime(intervals[0].FrozenAtUtc,out var frozen))throw Bad("Every trace needs its original frozen interval diagnostics.");
        var query=ExchangeTraceQuery.Prepare(evidence.Parameters,frozen);
        if(query.Windows.Count!=intervals.Length||query.MaximumRows<=intervals.Length||intervals.Length+intervals.Sum(i=>i.ReturnedEntries??0)>query.MaximumRows)throw Bad("Trace diagnostics/observations exceed their reviewed total row budget.");
        for(var index=0;index<intervals.Length;index++)
        {
            var interval=intervals[index];var window=query.Windows[index];
            if(interval.WindowIndex!=index||interval.FrozenAtUtc!=Timestamps.Format(frozen)||interval.StartUtc!=Timestamps.Format(window.Start)||interval.EndUtc!=Timestamps.Format(window.End)||interval.FilterDigest!=query.FilterDigest)
                throw Bad("Trace diagnostics differ from their frozen date/filter partition.");
            var children=traces.Where(t=>t.WindowIndex==index).ToArray();
            if(interval.ReadStatus is ReportReadState.Collected or ReportReadState.Partial)
            {
                if(interval.ReturnedEntries!=children.Length+interval.ExcludedEntries)throw Bad("Trace interval counts must account for retained/excluded recipients.");
                if(interval.ReadStatus==ReportReadState.Collected&&children.Any(t=>t.ReadStatus!=ReportReadState.Collected))throw Bad("A complete trace interval cannot hide unknown metadata.");
            }
            else if(children.Length!=0)throw Bad("Failed/unattempted intervals cannot claim trace recipients.");
            foreach(var trace in children)
            {
                _=TraceTime(trace.ReceivedUtc,"yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",out var received);
                if(received<window.Start||received>window.End||query.MessageTraceId is not null&&trace.MessageTraceId!=query.MessageTraceId)throw Bad("Trace recipient is outside the exact reviewed window/trace selection.");
                if(!query.IncludeSubject ? trace.Subject.State!="NotAttempted" : trace.Subject.State=="NotAttempted")throw Bad("Subject retention differs from the reviewed choice.");
            }
        }
        if(traces.Any(t=>t.WindowIndex>=intervals.Length))throw Bad("A trace references an unreviewed interval.");
        if((intervalSection.Status==ReportReadState.Collected||traceSection.Status==ReportReadState.Collected)&&intervals.Any(i=>i.ReadStatus!=ReportReadState.Collected))throw Bad("Successful sections cannot conceal unsuccessful trace intervals.");
        if((!query.IsExactTraceQuery||query.IsCurrentDayIncomplete)&&(evidence.Status==ReportReadState.Collected||intervalSection.Status==ReportReadState.Collected||traceSection.Status==ReportReadState.Collected))throw Bad("General high-recipient coverage/current-day queries remain explicitly incomplete.");
    }
}
