using BuildStandard.Core;
using BuildStandard.Core.Models;
namespace BuildStandard.Engine.Reports;
public sealed partial class ReportExporter
{
    public string ExportOperationalContext(OperationalContextReport report, ExportFormat format, bool suppliedFile = false, byte[]? originalBytes = null)
    {
        var serialised = OperationalContextSchema.Serialise(report);
        if (originalBytes is not null && OperationalContextSchema.Serialise(OperationalContextFile.Read(originalBytes, report.TenantId).Report) != serialised)
            throw new IntegrityException("The operational export no longer matches its admitted original.");
        var kind = "operational-context-" + report.Id + "-" + Guid.NewGuid().ToString("N");
        return format switch
        {
            ExportFormat.Html => WriteText(Target(kind, report.TenantId, report.EndedAt, "html"), RegisteredReportDocuments.OperationalHtml(report, _companyName, suppliedFile)),
            ExportFormat.Json when originalBytes is not null => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "json"), originalBytes),
            ExportFormat.Json => WriteText(Target(kind, report.TenantId, report.EndedAt, "json"), serialised),
            ExportFormat.Csv => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "csv.zip"), CsvWriter.ZipSheets(RegisteredReportDocuments.OperationalSheets(report, suppliedFile))),
            ExportFormat.Xlsx => WriteBytes(Target(kind, report.TenantId, report.EndedAt, "xlsx"), XlsxWriter.Write(RegisteredReportDocuments.OperationalSheets(report, suppliedFile))),
            _ => throw new ConfigurationException("Operational reports export as html, json, csv or xlsx.")
        };
    }
}
