using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;

namespace BuildStandard.Engine.Reports;

/// <summary>Strict offline admission over the existing immutable stores; no authentication, collection or writes.</summary>
public sealed class MailboxCapacityEvidenceReader(EvidenceStore evidence)
{
    public enum Family { Mailboxes, Licences }
    public const int MaximumEntries = 200;
    public const long MaximumInspectedBytes = 64L * 1024 * 1024;
    public sealed record Entry(string Id, string Label, string Problem) { public bool CanOpen => Problem.Length == 0; }
    public sealed record Listing(IReadOnlyList<Entry> Entries, bool Complete, string Detail);
    internal Func<string, IEnumerable<string>> EnumerateEntries { get; init; } = Directory.EnumerateFileSystemEntries;
    private string Folder(string tenant, Family family) => Path.Combine(evidence.TenantDirectory(tenant),
        family == Family.Mailboxes ? EvidenceStore.ExchangeReportEvidenceFolder : "report-evidence");
    private static void CanonicalId(string id)
    {
        if (!Guid.TryParseExact(id, "D", out var parsed) || parsed == Guid.Empty || id != parsed.ToString("D"))
            throw new ConfigurationException("Choose a canonical evidence ID from this tenant's listing.");
    }
    public string StoredFile(string tenant, Family family, string id)
    { CanonicalId(id); return Path.Combine(Folder(tenant, family), id + ".json"); }
    public ExchangeReportEvidence OpenMailboxes(string file, string tenant)
    {
        ReportEnvelopeDispatcher.RefuseLinks(file);
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ExchangeReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Mailbox evidence exceeds its 32 MiB reader limit.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ExchangeReportEvidenceSchema.MaximumBytes + 1);
        if (bytes.Length > ExchangeReportEvidenceSchema.MaximumBytes) throw new ConfigurationException("Mailbox evidence exceeds its 32 MiB reader limit.");
        string json;
        try { json = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new ConfigurationException("Mailbox evidence is not valid UTF-8."); }
        var report = ExchangeReportEvidenceSchema.Read(json, tenant);
        if (report.ReportId != MailboxCapacityInventory.ReportId) throw new ConfigurationException("Select exo-mailbox-inventory evidence for mailbox capacity.");
        return report;
    }
    public ReportEvidence OpenLicences(string file, string tenant)
    {
        var report = ReportEnvelopeDispatcher.ReadFile(file, tenant).Schema1;
        if (report?.ReportId != "users-licences") throw new ConfigurationException("Select schema 1 users-licences evidence for mailbox capacity.");
        return report;
    }
    public ExchangeReportEvidence OpenStoredMailboxes(string tenant, string id)
    {
        var result = OpenMailboxes(StoredFile(tenant, Family.Mailboxes, id), tenant);
        if (result.Id != id) throw new IntegrityException("The mailbox file contains a different record ID.");
        return result;
    }
    public ReportEvidence OpenStoredLicences(string tenant, string id)
    {
        var result = OpenLicences(StoredFile(tenant, Family.Licences, id), tenant);
        if (result.Id != id) throw new IntegrityException("The licence file contains a different record ID.");
        return result;
    }
    public Listing List(string tenant, Family family)
    {
        var entries = new List<Entry>(); long inspected = 0; var admitted = false;
        try
        {
            var folder = Folder(tenant, family); ReportEnvelopeDispatcher.RefuseLinks(folder);
            var attributes = File.GetAttributes(folder);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ConfigurationException("Evidence directory links are refused.");
            if ((attributes & FileAttributes.Directory) == 0) throw new IOException("Evidence path is not a directory.");
            admitted = true;
            foreach (var file in EnumerateEntries(folder))
            {
                if (entries.Count == MaximumEntries) return new(entries, false, "Listing incomplete: stopped at 200 entries; further records were not inspected.");
                var name = Path.GetFileName(file); var id = Path.GetFileNameWithoutExtension(name);
                try
                {
                    CanonicalId(id);
                    if (name != id + ".json") throw new ConfigurationException("Only canonical GUID.json evidence records are opened.");
                    ReportEnvelopeDispatcher.RefuseLinks(file);
                    var length = new FileInfo(file).Length;
                    if (length <= ReportEvidenceSchema.MaximumBytes && length > MaximumInspectedBytes - inspected)
                        return new(entries, false, "Listing incomplete: stopped at the 64 MiB inspection budget.");
                    if (length <= ReportEvidenceSchema.MaximumBytes) inspected += length;
                    string label;
                    if (family == Family.Mailboxes)
                    { var r = OpenStoredMailboxes(tenant, id); label = "Mailbox inventory · " + r.EndedAt + " · " + r.Status; }
                    else
                    { var r = OpenStoredLicences(tenant, id); label = "Users and licences · " + r.EndedAt + " · " + r.Status; }
                    entries.Add(new(id, label, ""));
                }
                catch (Exception ex) when (ReadFailure(ex)) { entries.Add(new(id, name, SensitiveDataScrubber.Scrub(ex.Message))); }
            }
            return new(entries, true, entries.Count == 0 ? "No saved records in this source folder. No tenant absence is inferred." : "Offline listing complete; damaged or unrelated entries retain their own refusal reasons.");
        }
        catch (DirectoryNotFoundException) when (!admitted) { return new(entries, true, "No saved source folder exists for this tenant. No tenant absence is inferred."); }
        catch (FileNotFoundException) when (!admitted) { return new(entries, true, "No saved source folder exists for this tenant. No tenant absence is inferred."); }
        catch (Exception ex) when (ReadFailure(ex)) { return new(entries, false, "Listing incomplete: " + SensitiveDataScrubber.Scrub(ex.Message)); }
    }
    private static bool ReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or ToolkitException or DecoderFallbackException;
}
