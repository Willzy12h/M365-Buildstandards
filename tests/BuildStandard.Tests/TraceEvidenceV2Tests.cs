using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class TraceEvidenceV2Tests
{
    internal const string Frozen="2026-10-20T00:00:00.000Z",Start="2026-10-10T00:00:00.000Z",End=Frozen,Received="2026-10-15T12:00:00.0000000Z";
    internal static ExchangeReportEvidence Report(bool exact=true)
    {
        var report=InboxRuleEvidenceV2Tests.Report();report.ReportId="exo-message-trace";report.SourceCommands=["Get-MessageTraceV2"];
        report.Parameters=[new(){Name="StartDate",Type="Date",Value="2026-10-10"},new(){Name="EndDate",Type="Date",Value="2026-10-19"},new(){Name="Sender",Type="Text",Value=exact?"":"sender@synthetic.example"},new(){Name="Recipient",Type="Text",Value=""},new(){Name="InternetMessageId",Type="Text",Value=""},new(){Name="MessageTraceId",Type="Text",Value=exact?TestData.Office:""},new(){Name="IncludeSubject",Type="Boolean",Value="false"},new(){Name="MaximumRows",Type="Integer",Value="5000"}];
        var query=ExchangeTraceQuery.Prepare(report.Parameters,DateTimeOffset.Parse(Frozen,System.Globalization.CultureInfo.InvariantCulture));
        report.Sections[0].Id="interval-reads";report.Sections[0].Rows=[new JsonObject{
            ["id"]=CalendarPermissionEvidenceV2Tests.Hash(report.ReportId,Start,End,query.FilterDigest),["name"]=null,["readStatus"]="Collected",["error"]=null,
            ["windowIndex"]=0,["frozenAtUtc"]=Frozen,["startUtc"]=Start,["endUtc"]=End,["filterDigest"]=query.FilterDigest,["requestedResultSize"]=4999,["returnedEntries"]=1,["excludedEntries"]=0 }];
        report.Sections[1].Id="traces";report.Sections[1].Rows=[new JsonObject{
            ["id"]=ExchangeObservationKey.MessageTrace(TestData.Office,"recipient@synthetic.example",Received),["name"]=null,["readStatus"]="Collected",["error"]=null,
            ["windowIndex"]=0,["messageTraceId"]=TestData.Office,["recipientAddress"]="recipient@synthetic.example",["receivedUtc"]=Received,
            ["senderAddress"]=InboxRuleEvidenceV2Tests.Value("sender@synthetic.example"),["internetMessageId"]=InboxRuleEvidenceV2Tests.Value("<synthetic@message.invalid>"),
            ["status"]=InboxRuleEvidenceV2Tests.Value("Delivered"),["size"]=InboxRuleEvidenceV2Tests.Value("0"),["subject"]=new JsonObject{["state"]="NotAttempted",["value"]=null,["reason"]="Subject collection was not selected."} }];
        if(!exact){report.Status="Partial";foreach(var section in report.Sections){section.Status="Partial";section.Error="General query cannot establish exhaustive high-recipient coverage.";}}
        return report;
    }
    [Fact] public void Shared_trace_tables_lead_with_work_fields_and_do_not_infer_size_units()
    {
        var sheet=RegisteredReportDocuments.Sheets(InboxRuleEvidenceV2Tests.Read(Report())).Single(s=>s.Name=="traces");
        Assert.Equal(new[]{"Recipient address","Status","Received (UTC)"},sheet.Rows[0].Take(3));
        Assert.Contains("Reported size (raw; unit unverified)",sheet.Rows[0]);
        Assert.Equal("0 (reported)",sheet.Rows[1][Array.IndexOf(sheet.Rows[0],"Reported size (raw; unit unverified)")]);
    }
    [Fact] public void An_intentionally_omitted_subject_is_explained_as_unselected_rather_than_an_unknown_read()
    {
        var sheet=RegisteredReportDocuments.Sheets(InboxRuleEvidenceV2Tests.Read(Report())).Single(s=>s.Name=="traces");
        Assert.Contains("Not collected — subject collection was not selected.",sheet.Rows[1]);
    }
    [Fact] public void Frozen_review_cannot_be_after_the_recorded_collection_start()
    {
        var report=Report();report.Sections[0].Rows[0]["frozenAtUtc"]="2026-10-21T00:00:00.000Z";
        Assert.ThrowsAny<ToolkitException>(()=>InboxRuleEvidenceV2Tests.Read(report));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void Frozen_trace_identity_and_general_query_limit_survive_historical_admission(bool exact)
    {
        var report=InboxRuleEvidenceV2Tests.Read(Report(exact));Assert.Equal(exact?"Collected":"Partial",report.Status);
        Assert.Equal("NotAttempted",report.Sections[1].Rows[0]["subject"]!["state"]!.GetValue<string>());
        Assert.Equal(Frozen,report.Sections[0].Rows[0]["frozenAtUtc"]!.GetValue<string>());
    }
    [Theory][InlineData("key")][InlineData("clock")][InlineData("digest")][InlineData("missing-window")][InlineData("bounds")][InlineData("subject")][InlineData("null-subject")][InlineData("duplicate")][InlineData("saturation")][InlineData("failed-zero")][InlineData("exact-trace")]
    public void Resealed_interval_identity_subject_and_completeness_forgeries_are_refused(string fault)
    {
        var report=Report();var interval=report.Sections[0].Rows[0];var row=report.Sections[1].Rows[0];
        switch(fault){case "key":row["id"]=TestData.Office;break;case "clock":interval["frozenAtUtc"]="2026-10-19T00:00:00.000Z";break;case "digest":interval["filterDigest"]=new string('a',64);break;
            case "missing-window":report.Sections[0].Rows.Clear();break;case "bounds":row["receivedUtc"]="2026-10-01T00:00:00.0000000Z";row["id"]=ExchangeObservationKey.MessageTrace(TestData.Office,"recipient@synthetic.example","2026-10-01T00:00:00.0000000Z");break;
            case "subject":row["subject"]=InboxRuleEvidenceV2Tests.Value("not opted in");break;case "null-subject":row["subject"]=null;break;case "duplicate":report.Sections[1].Rows.Add((JsonObject)row.DeepClone());break;
            case "saturation":interval["requestedResultSize"]=1;break;case "failed-zero":interval["readStatus"]="Failed";interval["error"]="Synthetic failure";break;
            case "exact-trace":row["messageTraceId"]=TestData.TenantB;row["id"]=ExchangeObservationKey.MessageTrace(TestData.TenantB,"recipient@synthetic.example",Received);break;}
        Assert.ThrowsAny<ToolkitException>(()=>InboxRuleEvidenceV2Tests.Read(report));
    }
}
