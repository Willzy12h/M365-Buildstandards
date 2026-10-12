using System.IO.Compression;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Assessment;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ApplicationReportExportTests
{
    [Fact]
    public async Task Secret_bearing_input_never_reaches_registered_metadata_store_or_any_export()
    {
        using var f = new ApplicationReportFixture(); const string secret = "SYNTHETIC_SECRET_BYTES_NEVER_RETAIN";
        var credential = ApplicationReportFixture.Credential("2026-09-20T00:00:00Z");
        foreach (var name in new[] { "secretText", "hint", "password", "key", "customKeyIdentifier", "accessToken" }) credential[name] = secret;
        var item = ApplicationReportFixture.Object(name: "=SUM(1,2)\nΚαλημέρα <script>synthetic</script>", passwords: [credential]); item["rawGraph"] = secret;
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications" ? [item] : []);
        var report = await f.Expiry(); var before = ApplicationReportEvidenceSchema.Serialise(report);
        Assert.DoesNotContain(secret, string.Join("\n", f.Log.Messages));
        Assert.DoesNotContain(secret, before); Assert.DoesNotContain("secretText", before); Assert.DoesNotContain("customKeyIdentifier", before);
        var store = new EvidenceStore(f.Root.Paths, NullLog.Instance); var stored = store.SaveApplicationReport(report);
        var storedBytes = File.ReadAllBytes(stored); Assert.DoesNotContain(secret, File.ReadAllText(stored));
        var exporter = new ReportExporter(f.Root.Paths, "Synthetic <company>");
        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Json, ExportFormat.Csv, ExportFormat.Xlsx })
        {
            var path = exporter.ExportApplicationReport(report, format, suppliedFile: true);
            var text = format is ExportFormat.Csv or ExportFormat.Xlsx ? ZipText(path) : File.ReadAllText(path);
            Assert.DoesNotContain(secret, text);
            if (format == ExportFormat.Json) Assert.Equal(before, ApplicationReportEvidenceSchema.Serialise(ApplicationReportEvidenceSchema.Read(text, TestData.TenantA)));
            else Assert.Contains("Offline supplied file", text);
            if (format == ExportFormat.Html) { Assert.DoesNotContain("<script>", text); Assert.Contains("&lt;script&gt;", text); Assert.Contains("default-src 'none'", text); }
            if (format == ExportFormat.Csv) Assert.Contains("'=SUM(1,2)", text);
            if (format == ExportFormat.Xlsx) { Assert.Contains("Καλημέρα", text); Assert.DoesNotContain("<f>", text); }
        }
        Assert.Equal(before, ApplicationReportEvidenceSchema.Serialise(report)); Assert.Equal(storedBytes, File.ReadAllBytes(stored));
        Assert.Throws<ConfigurationException>(() => AssessmentContext.RefuseEnvelope(before));
        Assert.Throws<ConfigurationException>(() => store.ReadJson<TenantSnapshot>(stored));
    }
    [Fact]
    public async Task Secret_or_unknown_row_fields_refuse_import_and_export_even_with_a_valid_digest()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications" ? [ApplicationReportFixture.Object()] : []);
        var report = await f.Expiry(); report.Sections[0].Rows[0]["secretText"] = "SYNTHETIC_SECRET";
        report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
        Assert.Throws<ConfigurationException>(() => ApplicationReportEvidenceSchema.Read(ApplicationReportEvidenceSchema.Serialise(report), TestData.TenantA));
        var exporter = new ReportExporter(f.Root.Paths, "Synthetic");
        Assert.Throws<ConfigurationException>(() => exporter.ExportApplicationReport(report, ExportFormat.Html));
        Assert.Empty(Directory.EnumerateFiles(f.Root.Paths.ReportsDirectory, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task Child_metadata_is_exported_as_separate_full_rows_and_partial_status_is_visible()
    {
        using var f = new ApplicationReportFixture(); f.ConsentData(); f.Session.Scopes.Remove("Directory.Read.All");
        var report = await f.Consent(); var sheets = ApplicationReportDocuments.Sheets(report, true);
        Assert.Contains(sheets, s => s.Name == "Role definitions" && s.Rows.Count == 2);
        Assert.Contains(sheets.Single(s => s.Name == "Report provenance").Rows, r => r.Any(c => c.Contains("NotAttempted", StringComparison.Ordinal)));
        var html = ApplicationReportDocuments.Html(report, "Synthetic", true); Assert.Contains("not an empty successful check", html);
        Assert.Contains("replication delays", html); Assert.Contains("Directory.Read.All", html);
        Assert.Single(sheets.Single(s => s.Name == "app-role-assignments").Rows.Skip(1));
    }
    private static string ZipText(string path)
    {
        using var stream = File.OpenRead(path); using var zip = new ZipArchive(stream); return string.Join("\n", zip.Entries.Where(e => e.Length > 0).Select(e =>
        { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
    }
}
