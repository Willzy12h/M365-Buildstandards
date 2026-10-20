using System.IO.Compression;
using System.Text;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Reports;

public sealed partial class ReportExporter
{
    /// <summary>Exports admitted supplied bytes without rewriting their source claims or upgrading their assurance.</summary>
    public string ExportSuppliedExchangeReport(ExchangeReportImport original, ExportFormat format)
    {
        var report = original.ReadEvidence();
        var kind = "supplied-exchange-report-" + SafeName(report.ReportId) + "-" + report.Id + "-" + Guid.NewGuid().ToString("N");
        var sheets = RegisteredReportDocuments.Sheets(report, suppliedFile: true);
        sheets[0].Add("Original file SHA-256", original.OriginalSha256);
        return format switch
        {
            ExportFormat.Html => WriteText(Target(kind, report.TenantId, report.EndedAt, "html"), RegisteredReportDocuments.SuppliedExchangeHtml(original, _companyName)),
            ExportFormat.Json => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "json.zip"), SuppliedExchangeBundle(original)),
            ExportFormat.Csv => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "csv.zip"), CsvWriter.ZipSheets(sheets)),
            ExportFormat.Xlsx => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "xlsx"), XlsxWriter.Write(sheets)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Exchange reports export as HTML, original JSON bundle, CSV or XLSX.")
        };
    }

    private static byte[] SuppliedExchangeBundle(ExchangeReportImport original)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var record = zip.CreateEntry("report.json").Open()) record.Write(original.OriginalBytes());
            using var notice = new StreamWriter(zip.CreateEntry("README.txt").Open(), new UTF8Encoding(false));
            notice.Write(RegisteredReportDocuments.SuppliedFileNotice + "\nOriginal file SHA-256: " + original.OriginalSha256
                + "\nOriginal bytes retained, including BOM and whitespace. This is not deployment before-evidence or current tenant verification.");
        }
        return output.ToArray();
    }
}
