using System.Text;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SecurityIncidentOutputTests
{
    [Theory][InlineData(ExportFormat.Html)][InlineData(ExportFormat.Json)][InlineData(ExportFormat.Csv)][InlineData(ExportFormat.Xlsx)]
    public void Incident_exports_preserve_originals_window_null_priority_and_inert_source_text(ExportFormat format)
    {
        using var root = new TempRoot();
        var fixture = SecurityIncidentEvidenceTests.Fixture(true);
        fixture["sections"]![0]!["rows"]![0]!["name"] = "<script>synthetic</script>";
        fixture["sections"]![0]!["rows"]![0]!["incidentWebUrl"] = "https://example.invalid/incident";
        var bytes = Encoding.UTF8.GetBytes("  " + SecurityIncidentEvidenceTests.Seal(fixture) + "\n");
        var report = OperationalContextFile.Read(bytes, TestData.TenantA).Report;
        var path = new ReportExporter(root.Paths, "Synthetic").ExportOperationalContext(report, format, true, bytes);
        Assert.NotEmpty(File.ReadAllBytes(path));
        if (format == ExportFormat.Json) Assert.Equal(bytes, File.ReadAllBytes(path));
        var sheets = RegisteredReportDocuments.OperationalSheets(report, true);
        Assert.Contains(sheets[0].Rows, row => row.Contains("2026-10-19T12:00:00Z"));
        Assert.Contains("Reported incident workflow state", sheets[1].Rows[0]);
        var priority = sheets[1].Rows[0].ToList().IndexOf("Priority score");
        Assert.True(priority >= 0); Assert.Contains("Unknown", sheets[1].Rows[1][priority]);
        if (format == ExportFormat.Html)
        {
            var html = File.ReadAllText(path);
            Assert.Contains("&lt;script&gt;synthetic&lt;/script&gt;", html);
            Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("href=", html);
            Assert.Contains(SecurityIncidentEvidenceTests.Notice, html);
        }
    }
}
