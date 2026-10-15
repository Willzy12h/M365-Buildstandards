using BuildStandard.Core.Models;
using BuildStandard.Core;
using System.IO.Compression;
using System.Text;

namespace BuildStandard.Engine.Reports;

public sealed partial class ReportExporter
{
    public string ExportApplicationReport(ApplicationReportEvidence report, ExportFormat format, bool suppliedFile = false)
        => ExportApplicationCore(report, format, suppliedFile, null);

    public string ExportHistoricalApplicationReport(ApplicationReportEvidence report, byte[] originalBytes, ExportFormat format, bool suppliedFile = false)
    {
        if (originalBytes.Length > ApplicationReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Historical evidence exceeds the 32 MiB limit.");
        string originalJson;
        try { originalJson = new UTF8Encoding(false, true).GetString(originalBytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new ConfigurationException("Historical evidence is not valid UTF-8."); }
        var original = ApplicationReportEvidenceSchema.ReadHistorical(originalJson, report.TenantId);
        if (ApplicationReportEvidenceSchema.Serialise(original) != ApplicationReportEvidenceSchema.Serialise(report))
            throw new ConfigurationException("Historical exports must retain the original admitted record.");
        return ExportApplicationCore(report, format, suppliedFile, originalBytes);
    }
    private string ExportApplicationCore(ApplicationReportEvidence report, ExportFormat format, bool suppliedFile, byte[]? originalBytes)
    {
        var historical = originalBytes is not null;
        if (historical) ApplicationReportEvidenceSchema.ValidateHistorical(report, report.TenantId);
        else ApplicationReportEvidenceSchema.Validate(report, report.TenantId);
        var kind = (historical ? "historical-owner-unverified-" : "tenant-report-") + report.ReportId + "-" + report.Id + "-" + Guid.NewGuid().ToString("N");
        return format switch
        {
            ExportFormat.Html => WriteText(Target(kind, report.TenantId, report.EndedAt, "html"), ApplicationReportDocuments.Html(report, _companyName, suppliedFile, historical)),
            ExportFormat.Json when historical => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "json.zip"), HistoricalJsonBundle(originalBytes!)),
            ExportFormat.Json => WriteText(Target(kind, report.TenantId, report.EndedAt, "json"), ApplicationReportEvidenceSchema.Serialise(report)),
            ExportFormat.Csv => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "csv.zip"), CsvWriter.ZipSheets(ApplicationReportDocuments.Sheets(report, suppliedFile, historical))),
            ExportFormat.Xlsx => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "xlsx"), XlsxWriter.Write(ApplicationReportDocuments.Sheets(report, suppliedFile, historical))),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Application metadata reports export as HTML, JSON, CSV or XLSX.")
        };
    }
    private static byte[] HistoricalJsonBundle(byte[] originalBytes)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var record = zip.CreateEntry("report.json").Open()) record.Write(originalBytes);
            using var notice = new StreamWriter(zip.CreateEntry("READ-ME.txt").Open(), new UTF8Encoding(false));
            notice.Write(ApplicationReportEvidenceSchema.HistoricalOwnerNotice + ". Original revision-1 bytes and digest retained; this is limited historical inspection, not current owner validation or deployment before-evidence.");
        }
        return output.ToArray();
    }
}
