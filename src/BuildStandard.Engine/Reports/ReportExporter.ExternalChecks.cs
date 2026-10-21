using System.Security.Cryptography;
using System.Text;
using BuildStandard.Core;
namespace BuildStandard.Engine.Reports;
public sealed partial class ReportExporter
{
    public string ExportExternalCheckResults(ExternalCheckImport source, string expectedTenantId, ExportFormat format, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var admitted = ExternalCheckResultsSchema.Read(source.GetOriginalBytes(), expectedTenantId);
        var (extension, content) = format switch
        {
            ExportFormat.Html => ("html", Encoding.UTF8.GetBytes(ExternalCheckDocuments.Html(admitted, _companyName))),
            ExportFormat.Json => ("json", admitted.GetOriginalBytes()),
            ExportFormat.Csv => ("csv.zip", CsvWriter.ZipSheets(ExternalCheckDocuments.Sheets(admitted))),
            ExportFormat.Xlsx => ("xlsx", XlsxWriter.Write(ExternalCheckDocuments.Sheets(admitted))),
            _ => throw new ConfigurationException("External check claims export as HTML, original JSON, CSV or XLSX.")
        };
        ct.ThrowIfCancellationRequested();
        var file = Target("untrusted-external-results-" + Guid.NewGuid().ToString("N"), expectedTenantId, admitted.Evidence.PerformedAt, extension);
        return WriteExclusiveReportOutput(file, content, ct);
    }
    public string ExportExternalControlReferences(ExternalCheckImport source, IReadOnlyList<ExternalControlReference> references, string expectedTenantId)
    {
        var admitted = ExternalCheckResultsSchema.Read(source.GetOriginalBytes(), expectedTenantId);
        var content = ExternalControlReferenceDocument.Write(admitted, references);
        var file = Target("external-control-references-" + Guid.NewGuid().ToString("N"), expectedTenantId, admitted.Evidence.PerformedAt, "json");
        return WriteExclusiveReportOutput(file, content, CancellationToken.None);
    }
    public string ExportIncidentControlReferences(OperationalContextFile source, IReadOnlyList<IncidentControlReference> references, string expectedTenantId)
    {
        var admitted = OperationalContextFile.Read(source.CopyOriginal(), expectedTenantId);
        var content = IncidentControlReferenceDocument.Write(admitted, references);
        var file = Target("incident-control-references-" + Guid.NewGuid().ToString("N"), expectedTenantId, admitted.Report.EndedAt, "json");
        return WriteExclusiveReportOutput(file, content, CancellationToken.None);
    }
    private static string WriteExclusiveReportOutput(string file, byte[] content, CancellationToken ct)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        var created = false;
        try
        {
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            created = true; ct.ThrowIfCancellationRequested(); stream.Write(content); stream.Flush(flushToDisk: true);
            stream.Position = 0;
            if (!SHA256.HashData(stream).SequenceEqual(SHA256.HashData(content))) throw new IntegrityException("Saved local report output failed readback verification.");
            ct.ThrowIfCancellationRequested(); return file;
        }
        catch (Exception ex) when (created && ex is IOException or UnauthorizedAccessException or OperationCanceledException or IntegrityException)
        {
            // Preserve the path for review; never delete after releasing a handle another process could replace.
            throw new ConfigurationException("Export did not complete. A possibly incomplete output remains for review: " + file, ex);
        }
    }
}
