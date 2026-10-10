using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.Engine.Reports;

/// <summary>
/// INT-071 over INT-088 evidence: reviews every mailbox in a sealed <c>exo-mailbox-inventory</c> record against one sealed
/// assigned-licence report for the same tenant. Pure and offline: it reads nothing, changes no quota and does not combine the
/// two records into a new one. Each read keeps its own state, so a value that was not read stays unable to check.
/// </summary>
public static class MailboxCapacityInventory
{
    public const string ReportId = "exo-mailbox-inventory";

    /// <summary>One reviewed mailbox. <see cref="HasArchive"/> is null unless the archive state was read.</summary>
    public sealed record Mailbox(MailboxCapacity.Result Capacity, string RowReadStatus, string? RowError, bool? HasArchive);

    /// <summary>
    /// The review of one inventory record. A section that was not read, or was cancelled, lists no mailboxes; a Partial section
    /// keeps its own limitation, so a capped or incomplete list is never presented as every mailbox in the tenant.
    /// </summary>
    public sealed record Review(string MailboxEvidenceId, string MailboxesObservedAt, string SectionStatus, string? SectionError,
        string LicenceEvidenceId, string LicencesObservedAt, IReadOnlyList<Mailbox> Mailboxes);

    public static Review Evaluate(ExchangeReportEvidence mailboxes, ReportEvidence licences)
    {
        ExchangeReportEvidenceSchema.Validate(mailboxes, mailboxes.TenantId);
        if (mailboxes.ReportId != ReportId)
            throw new ConfigurationException("Mailbox capacity needs the registered Exchange mailbox inventory report.");
        // The licence report must belong to the mailbox record's tenant; it is validated and indexed once for every row.
        var checkedLicences = MailboxCapacity.CheckedLicences(licences, mailboxes.TenantId);
        var section = mailboxes.Sections.Single(s => s.Id == "mailboxes");
        var reviewed = new List<Mailbox>();
        if (section.Status is ReportReadState.Collected or ReportReadState.Partial)
        {
            foreach (var node in section.Rows)
            {
                var row = ToolkitJson.Deserialize<MailboxReportRow>(node.ToJsonString());
                // Archive quota and size apply only to an archive that was read as present.
                var archive = row.HasArchive == true ? row.ArchiveReadStatus : ReportReadState.NotAttempted;
                var archiveSize = row.HasArchive == true ? row.ArchiveSizeReadStatus : ReportReadState.NotAttempted;
                var observation = new MailboxCapacity.Observation(mailboxes.TenantId, row.Id!, row.ExternalDirectoryObjectId,
                    row.Name, row.PrimarySmtpAddress, row.MailboxType, mailboxes.EndedAt,
                    row.QuotaReadStatus, row.PrimarySizeRaw, row.IssueWarningQuotaRaw, row.ProhibitSendQuotaRaw, row.ProhibitSendReceiveQuotaRaw,
                    archive, row.ArchiveSizeRaw, row.ArchiveQuotaRaw, row.PrimarySizeReadStatus, archiveSize);
                reviewed.Add(new(MailboxCapacity.Review(observation, checkedLicences), row.ReadStatus, row.Error, row.HasArchive));
            }
        }
        return new(mailboxes.Id, mailboxes.EndedAt, section.Status, section.Error, licences.Id, licences.EndedAt, reviewed);
    }
}
