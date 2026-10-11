namespace BDIT.TenantToolkit.Engine.Reports;

public sealed partial class ReportExporter
{
    /// <summary>Exports every derived row and original source provenance; never persists or reseals report evidence.</summary>
    public string ExportMailboxCapacity(MailboxCapacityReviewDocuments.Document review, ExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(review);
        // Document is created only by strict paired-source admission; validate frozen originals again before output.
        MailboxCapacityReviewDocuments.Create(
            ExchangeReportEvidenceSchema.Read(review.MailboxJson, review.Review.MailboxSource.TenantId),
            ReportEvidenceSchema.Read(review.LicenceJson, review.Review.MailboxSource.TenantId), review.Review.MailboxSource.TenantId);
        var kind = "mailbox-capacity-review-" + Guid.NewGuid().ToString("N");
        var tenant = review.Review.MailboxSource.TenantId; var time = review.Review.MailboxSource.EndedAt;
        return format switch
        {
            ExportFormat.Html => WriteText(Target(kind, tenant, time, "html"), MailboxCapacityReviewDocuments.Html(review)),
            ExportFormat.Json => WriteText(Target(kind, tenant, time, "json"), MailboxCapacityReviewDocuments.Json(review)),
            ExportFormat.Csv => WriteBytes(Target(kind, tenant, time, "csv.zip"), CsvWriter.ZipSheets(MailboxCapacityReviewDocuments.Sheets(review))),
            ExportFormat.Xlsx => WriteBytes(Target(kind, tenant, time, "xlsx"), XlsxWriter.Write(MailboxCapacityReviewDocuments.Sheets(review))),
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Derived mailbox reviews export as HTML, JSON, CSV ZIP or XLSX.")
        };
    }
}
