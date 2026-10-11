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

    /// <summary>One section of a source record exactly as it was read: its own state, error and limitations.</summary>
    public sealed record SectionSummary(string Id, string Status, string? Error, IReadOnlyList<string> Limitations);

    /// <summary>
    /// An immutable copy of one validated source record's identity and limitations, taken when it was checked, so the review
    /// keeps each record's digest, source mode, capture interval and read state and never merges the two reads.
    /// </summary>
    public sealed record SourceSummary(string Kind, string TenantId, string ReportId, string EvidenceId, string IntegrityDigest,
        string SourceMode, string StartedAt, string EndedAt, string Status, IReadOnlyList<string> Limitations, SectionSummary Section);

    /// <summary>
    /// The review of one inventory record against one licence record. A mailbox section that was not read, or was cancelled,
    /// lists no mailboxes; a Partial section keeps its own limitation, so a capped or incomplete list is never presented as
    /// every mailbox in the tenant. A failed licence read stays a failed licence read, not an empty user list.
    /// </summary>
    public sealed record Review(SourceSummary MailboxSource, SourceSummary LicenceSource, IReadOnlyList<Mailbox> Mailboxes);

    internal static SourceSummary Summarise(string kind, string tenantId, string reportId, string id, string digest, string sourceMode,
        string startedAt, string endedAt, string status, IEnumerable<string> limitations, ReportSection section) =>
        new(kind, tenantId, reportId, id, digest, sourceMode, startedAt, endedAt, status, Array.AsReadOnly(limitations.ToArray()),
            new(section.Id, section.Status, section.Error, Array.AsReadOnly(section.Limitations.ToArray())));

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
                // A mailbox read as having no archive has no archive quota or size to check. Otherwise the archive keeps the read
                // state it was given: a failed archive read stays Failed rather than becoming NotAttempted.
                var archive = row.HasArchive == false ? ReportReadState.NotAttempted : row.ArchiveReadStatus;
                var archiveSize = row.HasArchive == false ? ReportReadState.NotAttempted : row.ArchiveSizeReadStatus;
                var observation = new MailboxCapacity.Observation(mailboxes.TenantId, row.Id!, row.ExternalDirectoryObjectId,
                    row.Name, row.PrimarySmtpAddress, row.MailboxType, mailboxes.EndedAt,
                    row.QuotaReadStatus, row.PrimarySizeRaw, row.IssueWarningQuotaRaw, row.ProhibitSendQuotaRaw, row.ProhibitSendReceiveQuotaRaw,
                    archive, row.ArchiveSizeRaw, row.ArchiveQuotaRaw, row.PrimarySizeReadStatus, archiveSize);
                reviewed.Add(new(MailboxCapacity.Review(observation, checkedLicences), row.ReadStatus, row.Error, row.HasArchive));
            }
        }
        var mailboxSource = Summarise(mailboxes.Kind, mailboxes.TenantId, mailboxes.ReportId, mailboxes.Id, mailboxes.IntegrityDigest,
            mailboxes.SourceMode, mailboxes.StartedAt, mailboxes.EndedAt, mailboxes.Status, mailboxes.Limitations, section);
        return new(mailboxSource, checkedLicences.Source, reviewed.AsReadOnly());
    }
}
