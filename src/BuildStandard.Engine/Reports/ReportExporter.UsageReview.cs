using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Reports;
public sealed partial class ReportExporter
{
    public string ExportUsageReview(UsageReviewReport report,ExportFormat format,bool suppliedFile=false,byte[]? originalBytes=null)
    {
        _=UsageReviewSchema.Project(report);
        if(originalBytes is not null)
        {
            if(originalBytes.Length>UsageReviewSchema.MaximumBytes)throw new ConfigurationException("Original usage pack exceeds40MiB.");
            string json;
            try{json=new UTF8Encoding(false,true).GetString(originalBytes).TrimStart('\uFEFF');}
            catch(DecoderFallbackException){throw new ConfigurationException("Original usage pack is not strict UTF-8.");}
            if(UsageReviewSchema.Serialise(UsageReviewSchema.Read(json,report.TenantId))!=UsageReviewSchema.Serialise(report))throw new ConfigurationException("Usage export no longer matches its admitted original pack.");
        }
        var kind="usage-review-"+report.Id+"-"+Guid.NewGuid().ToString("N");
        return format switch
        {
            ExportFormat.Html=>WriteText(Target(kind,report.TenantId,report.EndedAt,"html"),UsageReviewDocuments.Html(report,_companyName,suppliedFile)),
            ExportFormat.Json when originalBytes is not null=>WriteBytes(Target(kind,report.TenantId,report.EndedAt,"json"),originalBytes),
            ExportFormat.Json=>WriteText(Target(kind,report.TenantId,report.EndedAt,"json"),UsageReviewSchema.Serialise(report)),
            ExportFormat.Csv=>WriteBytes(Target(kind,report.TenantId,report.EndedAt,"csv.zip"),CsvWriter.ZipSheets(UsageReviewDocuments.Sheets(report,suppliedFile))),
            ExportFormat.Xlsx=>WriteBytes(Target(kind,report.TenantId,report.EndedAt,"xlsx"),XlsxWriter.Write(UsageReviewDocuments.Sheets(report,suppliedFile))),
            _=>throw new ConfigurationException("Usage reports export as html, json, csv or xlsx.")
        };
    }
}
