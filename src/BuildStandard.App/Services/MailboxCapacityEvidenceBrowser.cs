using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;

namespace BuildStandard.App.Services;

/// <summary>Presentation facade over strict offline readers; both original store kinds stay separate.</summary>
public sealed class MailboxCapacityEvidenceBrowser(EvidenceStore store)
{
    private readonly MailboxCapacityEvidenceReader _reader = new(store);
    public MailboxCapacityEvidenceReader.Listing ListMailboxes(string tenant) => _reader.List(tenant, MailboxCapacityEvidenceReader.Family.Mailboxes);
    public MailboxCapacityEvidenceReader.Listing ListLicences(string tenant) => _reader.List(tenant, MailboxCapacityEvidenceReader.Family.Licences);
    public ExchangeReportEvidence Mailboxes(string tenant, string? recordId, string suppliedFile) => recordId is null
        ? _reader.OpenMailboxes(suppliedFile, tenant) : _reader.OpenStoredMailboxes(tenant, recordId);
    public ReportEvidence Licences(string tenant, string? recordId, string suppliedFile) => recordId is null
        ? _reader.OpenLicences(suppliedFile, tenant) : _reader.OpenStoredLicences(tenant, recordId);
}
