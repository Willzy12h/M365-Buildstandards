using System.IO;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Envelope = BuildStandard.Engine.Reports.ReportEnvelopeDispatcher.Envelope;

namespace BuildStandard.App.Services;

/// <summary>Offline, bounded discovery. Unreadable records and incomplete listings are visible findings.</summary>
public sealed class GraphReportEvidenceBrowser(EvidenceStore evidence)
{
    public const int MaximumFileBytes = ReportEvidenceSchema.MaximumBytes;
    public const int MaximumEntries = 200;
    public const long MaximumListedBytes = 64L * 1024 * 1024;
    public sealed record Entry(string Id, string Label, string Problem)
    {
        public bool CanOpen => Problem.Length == 0;
    }
    public sealed record Listing(IReadOnlyList<Entry> Entries, bool Complete, string Detail);

    internal Func<string, IEnumerable<string>> EnumerateEntries { get; init; } = Directory.EnumerateFileSystemEntries;

    public Listing List(string tenantId)
    {
        var entries = new List<Entry>();
        long inspectedBytes = 0;
        var folderAdmitted = false;
        var folder = Path.Combine(evidence.TenantDirectory(tenantId), "report-evidence");
        try
        {
            RejectLinks(folder);
            // GetAttributes distinguishes missing directories from denied access, unlike Directory.Exists.
            if ((File.GetAttributes(folder) & FileAttributes.Directory) == 0) throw new IOException("Report evidence path is not a directory.");
            folderAdmitted = true;
            foreach (var file in EnumerateEntries(folder))
            {
                if (entries.Count == MaximumEntries) return new(entries, false, "Listing stopped at 200 entries; further evidence was not inspected.");
                var name = Path.GetFileName(file);
                var id = Path.GetFileNameWithoutExtension(name);
                if (!Guid.TryParseExact(id, "D", out var guid) || guid == Guid.Empty || name != guid.ToString("D") + ".json")
                { entries.Add(new("", name, "Unexpected entry: only canonical GUID.json report records are opened.")); continue; }
                try
                {
                    RejectLinks(file);
                    var length = new FileInfo(file).Length;
                    if (length <= MaximumFileBytes && length > MaximumListedBytes - inspectedBytes)
                        return new(entries, false, "Listing stopped at its 64 MiB inspection budget; further evidence was not inspected.");
                    if (length <= MaximumFileBytes) inspectedBytes += length;
                    var report = OpenStored(tenantId, id);
                    entries.Add(new(id, GraphReportPresentation.For(report).Name + " · " + GraphReportPresentation.EndedAt(report) + " · " + GraphReportPresentation.Status(report), ""));
                }
                catch (Exception ex) when (IsReadFailure(ex)) { entries.Add(new(id, name, SensitiveDataScrubber.Scrub(ex.Message))); }
            }
            return new(entries, true, entries.Count == 0 ? "No saved report records in this tenant's report-evidence folder." : "Offline listing complete; select a record to open it.");
        }
        catch (DirectoryNotFoundException) when (!folderAdmitted) { return new(entries, true, "No report-evidence folder exists for the selected tenant."); }
        catch (FileNotFoundException) when (!folderAdmitted) { return new(entries, true, "No report-evidence folder exists for the selected tenant."); }
        catch (Exception ex) when (IsReadFailure(ex)) { return new(entries, false, "Listing incomplete: " + SensitiveDataScrubber.Scrub(ex.Message)); }
    }

    private static bool IsReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or ToolkitException or DecoderFallbackException;

    public Envelope OpenStored(string tenantId, string recordId)
    {
        if (!Guid.TryParseExact(recordId, "D", out var id) || id == Guid.Empty || recordId != id.ToString("D"))
            throw new ConfigurationException("Choose a canonical report record ID from this tenant's listing.");
        var file = Path.Combine(evidence.TenantDirectory(tenantId), "report-evidence", recordId + ".json");
        RejectLinks(file);
        var report = ReportEnvelopeDispatcher.ReadFile(file, tenantId);
        if (GraphReportPresentation.RecordId(report) != recordId) throw new IntegrityException("The report file contains a different record ID.");
        return report;
    }

    public Envelope OpenSupplied(string file, string expectedTenant)
    {
        RejectLinks(file);
        return ReportEnvelopeDispatcher.ReadFile(file, expectedTenant);
    }

    internal static void RejectLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ConfigurationException("Report evidence refuses links and reparse points.");
            }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
        }
    }
}
