using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;

namespace BuildStandard.Engine.Reports;

/// <summary>Derived offline review only. Frozen validated sources are retained without resealing or evidence writes.</summary>
public static class MailboxCapacityReviewDocuments
{
    public const string Notice = "Derived historical mailbox capacity review — not report evidence or deployment authority. No live reads, quota or licence changes. Source and account claims are unverified; digests detect modification, not authenticity. Display filters never limit exports.";
    public sealed class Document
    {
        internal Document(string mailboxJson, string licenceJson, MailboxCapacityInventory.Review review)
        { MailboxJson = mailboxJson; LicenceJson = licenceJson; Review = review; }
        public string MailboxJson { get; }
        public string LicenceJson { get; }
        public MailboxCapacityInventory.Review Review { get; }
    }
    public static Document Create(ExchangeReportEvidence mailboxes, ReportEvidence licences, string expectedTenant)
    {
        var mailboxJson = ExchangeReportEvidenceSchema.Serialize(mailboxes);
        var licenceJson = ReportEvidenceSchema.Serialize(licences);
        var mailboxCopy = ExchangeReportEvidenceSchema.Read(mailboxJson, expectedTenant);
        var licenceCopy = ReportEvidenceSchema.Read(licenceJson, expectedTenant);
        return new(mailboxJson, licenceJson, MailboxCapacityInventory.Evaluate(mailboxCopy, licenceCopy));
    }
    public static string Summary(Document document)
    {
        var r = document.Review; var source = r.MailboxSource.Section;
        return "Mailbox source " + r.MailboxSource.Status + " / section " + source.Status + " · " + r.Mailboxes.Count + " recorded mailboxes. "
            + (r.Mailboxes.Count == 0 ? source.Status == ReportReadState.Collected ? "Checked source returned zero mailboxes; historical result only." : "No complete mailbox list is available; this is not an empty successful check." : "Returned rows retain their own unknown and failed reads.")
            + " Licence source " + r.LicenceSource.Status + " / users " + r.LicenceSource.Section.Status + ".";
    }
    public static IReadOnlyList<Sheet> Sheets(Document document)
    {
        var r = document.Review;
        var provenance = new Sheet("Review provenance", ["Field", "Value"]); provenance.Add("Review notice", Notice); provenance.Add("Summary", Summary(document));
        foreach (var (label, s) in new[] { ("Mailbox", r.MailboxSource), ("Licence", r.LicenceSource) })
        {
            provenance.Add(label + " tenant ID", s.TenantId); provenance.Add(label + " record ID", s.EvidenceId);
            provenance.Add(label + " report ID", s.ReportId); provenance.Add(label + " original source mode", s.SourceMode);
            provenance.Add(label + " started at", s.StartedAt); provenance.Add(label + " ended at", s.EndedAt);
            provenance.Add(label + " digest", s.IntegrityDigest); provenance.Add(label + " status", s.Status);
            provenance.Add(label + " section", s.Section.Id + " · " + s.Section.Status + " · " + (s.Section.Error ?? "No recorded error"));
            foreach (var limit in s.Limitations.Concat(s.Section.Limitations)) provenance.Add(label + " limitation", limit);
        }
        var mailboxSource = ExchangeReportEvidenceSchema.Read(document.MailboxJson, r.MailboxSource.TenantId);
        provenance.Add("Mailbox initiating account object ID", mailboxSource.InitiatingAccountObjectId ?? "Unknown historical account");
        provenance.Add("Mailbox observed account UPN", mailboxSource.ObservedAccountUpn ?? "Unknown historical account");
        provenance.Add("Mailbox run ID", mailboxSource.RunId); provenance.Add("Mailbox adapter version", mailboxSource.AdapterVersion);
        provenance.Add("Mailbox module / runtime", mailboxSource.ModuleVersion + " / " + mailboxSource.RuntimeVersion);
        provenance.Add("Mailbox script digest", mailboxSource.ScriptSha256); provenance.Add("Mailbox manifest digest", mailboxSource.ManifestSha256);
        provenance.Add("Mailbox runner digest", mailboxSource.RunnerTemplateSha256);
        foreach (var command in mailboxSource.SourceCommands) provenance.Add("Mailbox source command", command);
        foreach (var parameter in mailboxSource.Parameters) provenance.Add("Mailbox parameter", parameter.Name + " = " + parameter.Value);
        var licenceSource = ReportEvidenceSchema.Read(document.LicenceJson, r.LicenceSource.TenantId);
        provenance.Add("Licence account object ID", licenceSource.AccountObjectId ?? "Unknown historical account");
        var rows = new Sheet("Mailbox capacity", ["Name", "Primary SMTP", "Mailbox type", "Configured 100 GB", "Entitlement", "Finding", "Row read state", "Mailbox ID", "Entra object ID", "Eligibility reason", "Row error", "Has archive"]);
        var measures = new Sheet("Measurements", ["Mailbox ID", "Measurement", "Raw Exchange text", "Parsed bytes", "State", "Reason", "Recorded read state"]);
        foreach (var m in r.Mailboxes)
        {
            var c = m.Capacity; var o = c.Observed;
            rows.Add(o.Name ?? "Unknown / not returned", o.PrimarySmtpAddress ?? "Unknown / not returned", o.MailboxType,
                c.Configured100GB is null ? "Unknown" : c.Configured100GB.Value ? "Yes" : "No", c.Eligibility, c.Finding, m.RowReadStatus,
                o.MailboxId, o.ExternalDirectoryObjectId, c.EligibilityReason, m.RowError, m.HasArchive is null ? "Unknown" : m.HasArchive.Value ? "Yes" : "No");
            foreach (var (name, measure, state) in new[] {
                ("Primary size", c.PrimarySize, o.PrimarySizeReadStatus), ("Warning quota", c.WarningQuota, o.PrimaryReadStatus),
                ("Send quota", c.SendQuota, o.PrimaryReadStatus), ("Send/receive quota", c.SendReceiveQuota, o.PrimaryReadStatus),
                ("Archive size", c.ArchiveSize, o.ArchiveSizeReadStatus), ("Archive quota", c.ArchiveQuota, o.ArchiveReadStatus) })
                measures.Add(o.MailboxId, name, measure.Raw ?? "Unknown / not returned", measure.Bytes, measure.State, measure.Reason, state);
        }
        var originalColumns = ExchangeReportEvidenceSchema.Row(new MailboxReportRow()).Select(field => field.Key).ToArray();
        var originals = new Sheet("Mailbox original rows", originalColumns);
        foreach (var row in mailboxSource.Sections.Single(section => section.Id == "mailboxes").Rows)
            originals.Rows.Add(originalColumns.Select(column => row[column] is null ? "Unknown / not returned" : row[column] is JsonValue value && value.TryGetValue<string>(out var text) ? text : row[column]!.ToJsonString()).ToArray());
        var sheets = new List<Sheet> { provenance, rows, measures, originals };
        // Full assigned-licence tables remain available, including users not matched to a mailbox; no display filter is used.
        foreach (var source in RegisteredReportDocuments.Sheets(ReportEvidenceSchema.Read(document.LicenceJson, r.LicenceSource.TenantId), true))
        { var sheet = new Sheet("Licence " + source.Name, source.Rows[0]); sheet.Rows.AddRange(source.Rows.Skip(1)); sheets.Add(sheet); }
        return sheets;
    }
    public static string Json(Document document) => ToolkitJson.Serialize(new
    { kind = "mailboxCapacityDerivedReview", notice = Notice, review = document.Review,
        originalMailboxSource = JsonNode.Parse(document.MailboxJson), originalLicenceSource = JsonNode.Parse(document.LicenceJson) });
    public static string Html(Document document)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en-GB\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Mailbox capacity review</title><style>body{font:15px system-ui;margin:24px;color:#172c3e}table{border-collapse:collapse}td,th{border:1px solid #ccd6df;padding:8px;overflow-wrap:anywhere}.table{overflow:auto}</style></head><body><h1>Mailbox capacity review</h1><p>");
        html.Append(WebUtility.HtmlEncode(Notice)).Append("</p>");
        foreach (var sheet in Sheets(document))
        { html.Append("<h2>").Append(WebUtility.HtmlEncode(sheet.Name)).Append("</h2><div class=\"table\"><table>"); foreach (var row in sheet.Rows) { html.Append("<tr>"); foreach (var cell in row) html.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>"); html.Append("</tr>"); } html.Append("</table></div>"); }
        return html.Append("</body></html>").ToString();
    }
}
