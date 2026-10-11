using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Reports;

public sealed partial class ReportExporter
{
    public string ExportApplicationReport(ApplicationReportEvidence report, ExportFormat format, bool suppliedFile = false)
    {
        ApplicationReportEvidenceSchema.Validate(report, report.TenantId);
        var kind = "tenant-report-" + report.ReportId + "-" + report.Id + "-" + Guid.NewGuid().ToString("N");
        return format switch
        {
            ExportFormat.Html => WriteText(Target(kind, report.TenantId, report.EndedAt, "html"), ApplicationReportDocuments.Html(report, _companyName, suppliedFile)),
            ExportFormat.Json => WriteText(Target(kind, report.TenantId, report.EndedAt, "json"), ApplicationReportEvidenceSchema.Serialise(report)),
            ExportFormat.Csv => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "csv.zip"), CsvWriter.ZipSheets(ApplicationReportDocuments.Sheets(report, suppliedFile))),
            ExportFormat.Xlsx => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "xlsx"), XlsxWriter.Write(ApplicationReportDocuments.Sheets(report, suppliedFile))),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Application metadata reports export as HTML, JSON, CSV or XLSX.")
        };
    }
}
