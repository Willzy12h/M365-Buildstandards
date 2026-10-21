using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BuildStandard.Core;
using BuildStandard.Engine.Evidence;
namespace BuildStandard.Engine.Reports;
public sealed partial class ReportExporter
{
    public string ExportLifecycleRecord(LifecycleHistoricalRecord source,string expectedTenantId,ExportFormat format,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if(!Guid.TryParseExact(expectedTenantId,"D",out var tenant)||tenant==Guid.Empty||!string.Equals(source.TenantId,expectedTenantId,StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Lifecycle record export belongs to a different client.");
        var document=source.GetDocument();
        var (extension,bytes)=format switch
        {
            ExportFormat.Json=>("json",source.GetOriginalBytes()),
            ExportFormat.Html=>("html",Encoding.UTF8.GetBytes("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Historical lifecycle evidence</title><style>body{font:15px system-ui;margin:24px;color:#172c3e}pre{white-space:pre-wrap;overflow-wrap:anywhere;padding:16px;background:#eef5f5}.notice{padding:12px;background:#fff0cd}</style></head><body><p>"+E(_companyName)+"</p><h1>Historical lifecycle evidence</h1><p class=\"notice\">Historical local evidence only: no execution authority. Byte integrity is not source authenticity or permission to replay a write. An observed action does not complete a joiner, mover or leaver workflow; manual preservation, Exchange, application and device work remain separate.</p><p>Recorded tenant: "+E(source.TenantId)+"<br>Kind: "+E(source.Kind)+"<br>Record ID: "+E(source.Id)+"<br>Original bytes SHA-256: "+source.OriginalSha256+"</p><h2>Exact recorded fields</h2><p>Statuses, missing values and partial/unknown evidence retain their original meaning. JSON export preserves the original bytes.</p><pre>"+E(document.ToJsonString(new JsonSerializerOptions {WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping}))+"</pre></body></html>")),
            _=>throw new ConfigurationException("Lifecycle evidence exports as original JSON or inert HTML; no spreadsheet truncation or implied completeness is offered.")
        };
        var time=document["endedAt"]?.GetValue<string>()??document["createdAt"]?.GetValue<string>()??document["startedAt"]?.GetValue<string>()??"";
        var file=Target(source.Kind+"-"+Guid.NewGuid().ToString("N"),source.TenantId,time,extension);
        return WriteExclusiveReportOutput(file,bytes,ct);
    }
    private static string E(string value)=>WebUtility.HtmlEncode(value);
}
