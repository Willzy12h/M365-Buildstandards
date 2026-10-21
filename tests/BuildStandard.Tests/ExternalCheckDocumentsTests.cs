using System.IO.Compression;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExternalCheckDocumentsTests
{
    private static string Export(ReportExporter exporter, ExternalCheckImport source, ExportFormat format,
        string? tenant = null, CancellationToken ct = default)
        => exporter.ExportExternalCheckResults(source, tenant ?? TestData.TenantA, format, ct);
    [Theory][InlineData(ExportFormat.Html)][InlineData(ExportFormat.Csv)][InlineData(ExportFormat.Xlsx)][InlineData(ExportFormat.Json)]
    public void Every_export_retains_untrusted_partial_source_meaning_and_inert_text(ExportFormat format)
    {
        using var root = new TempRoot(); var fixture = ExternalCheckResultsTests.Fixture();
        fixture["rows"]![0]!["title"] = "=SUM(1,2)"; fixture["rows"]![0]!["reason"] = "<script>never()</script>";
        fixture["rows"]![0]!["reference"] = "javascript:never()"; fixture["rows"]![0]!["outcome"] = "FutureSuccess";
        var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(ExternalCheckResultsTests.Bytes(fixture)).ToArray();
        var source = ExternalCheckResultsSchema.Read(bytes, TestData.TenantA); var exporter = new ReportExporter(root.Paths, "Synthetic company");
        var file = Export(exporter, source, format); var first = File.ReadAllBytes(file);
        var second = Export(exporter, source, format); Assert.NotEqual(file, second); Assert.Equal(first, File.ReadAllBytes(file));
        Assert.Contains("untrusted-external-results", Path.GetFileName(file));
        if (format == ExportFormat.Json) { Assert.Equal(bytes, first); return; }
        string text;
        if (format == ExportFormat.Html)
        {
            text = File.ReadAllText(file); Assert.DoesNotContain("<script>", text); Assert.DoesNotContain("href=", text);
            Assert.Contains("&lt;script&gt;", text); Assert.Contains("default-src 'none'", text);
        }
        else
        {
            using var zip = ZipFile.OpenRead(file);
            text = string.Join("\n", zip.Entries.Where(e => e.FullName.EndsWith(format == ExportFormat.Csv ? ".csv" : ".xml", StringComparison.Ordinal))
                .Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
            if (format == ExportFormat.Csv) Assert.Contains("'=SUM", text);
            else { Assert.DoesNotContain("<f>", text); Assert.Contains("inlineStr", text); }
        }
        Assert.Contains("Untrusted", text); Assert.Contains("Partial", text); Assert.Contains("Unclassified", text);
        Assert.Contains(source.OriginalSha256, text); Assert.DoesNotContain("Compliant", text);
    }
    [Theory][InlineData("tenant")][InlineData("cancel")][InlineData("format")]
    public void Refused_export_creates_no_output(string fault)
    {
        using var root = new TempRoot(); var source = ExternalCheckResultsSchema.Read(ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture()), TestData.TenantA);
        var exporter = new ReportExporter(root.Paths, "Synthetic");
        if (fault == "cancel") Assert.ThrowsAny<OperationCanceledException>(() => Export(exporter, source, ExportFormat.Json, ct: new CancellationToken(true)));
        else Assert.ThrowsAny<ToolkitException>(() => Export(exporter, source, fault == "format" ? ExportFormat.Markdown : ExportFormat.Json,
            tenant: fault == "tenant" ? TestData.TenantB : TestData.TenantA));
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
    [Fact] public void Reference_export_keeps_exact_source_and_does_not_overwrite_previous_output()
    {
        using var root = new TempRoot();
        var source = ExternalCheckResultsSchema.Read(ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture()), TestData.TenantA);
        var exporter = new ReportExporter(root.Paths, "Synthetic");
        var method = typeof(ReportExporter).GetMethod("ExportExternalControlReferences"); Assert.NotNull(method);
        var links = new[] { new ExternalControlReference(source.OriginalSha256, "TEST-1", "2026.10.20", new string('a',64), "CA-001") };
        var first = (string)method.Invoke(exporter, new object[] { source, links, TestData.TenantA })!;
        var bytes = File.ReadAllBytes(first);
        Assert.Equal(links, ExternalControlReferenceDocument.Read(bytes, source));
        var second = (string)method.Invoke(exporter, new object[] { source, links, TestData.TenantA })!;
        Assert.NotEqual(first, second); Assert.Equal(bytes, File.ReadAllBytes(first));
    }
}
