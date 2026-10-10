using System.IO.Compression;
using System.Net;
using System.Xml.Linq;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Microsoft.VisualBasic.FileIO;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ReportExportRobustnessTests
{
    private const string Detail = "Synthetic later page failed; returned rows do not establish a complete inventory.";
    private static readonly string LongName = "Jos\u00e9 e\u0301 \U0001F600, \"quoted\"\r\nsecond line " + new string('x', 40000);

    [Theory]
    [InlineData(ExportFormat.Html)]
    [InlineData(ExportFormat.Json)]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    public void Partial_multilingual_reports_preserve_fidelity_unknowns_and_previous_exports(ExportFormat format)
    {
        using var root = new TempRoot();
        var report = Fixture();
        var before = ReportEvidenceSchema.Serialize(report);
        var exporter = new ReportExporter(root.Paths, "Synthetic <company>");
        var first = exporter.ExportReport(report, format);
        var original = File.ReadAllBytes(first);
        var second = exporter.ExportReport(report, format);
        Assert.NotEqual(first, second);
        Assert.Equal(original, File.ReadAllBytes(first));
        Assert.Equal(before, ReportEvidenceSchema.Serialize(report));
        Assert.Equal(2, Directory.GetFiles(root.Paths.ReportsDirectory).Length);

        switch (format)
        {
            case ExportFormat.Json:
                var parsed = ReportEvidenceSchema.Read(File.ReadAllText(second), TestData.TenantA);
                Assert.Equal(ReportReadState.Partial, parsed.Status);
                Assert.Equal(Detail, parsed.Sections[0].Error);
                Assert.Equal(LongName, parsed.Sections[0].Rows[0]["name"]!.GetValue<string>());
                Assert.Null(parsed.Sections[0].Rows[0]["lastSyncAt"]);
                Assert.Equal(report.IntegrityDigest, parsed.IntegrityDigest);
                break;
            case ExportFormat.Html:
                var html = File.ReadAllText(second);
                Assert.Contains(WebUtility.HtmlEncode(LongName), html);
                Assert.Contains("Read status: Partial", html);
                Assert.Contains(WebUtility.HtmlEncode(Detail), html);
                Assert.Contains("Unknown / not returned", html);
                Assert.DoesNotContain("<script>", html);
                Assert.DoesNotContain("Synthetic <company>", html);
                break;
            case ExportFormat.Csv:
                using (var zip = new ZipArchive(File.OpenRead(second)))
                {
                    using var metadata = new StreamReader(zip.GetEntry("01_Report_provenance.csv")!.Open());
                    Assert.Contains(Detail, metadata.ReadToEnd());
                    using var reader = new StreamReader(zip.GetEntry("02_devices.csv")!.Open());
                    using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
                    parser.SetDelimiters(",");
                    var header = parser.ReadFields()!;
                    var row = parser.ReadFields()!;
                    Assert.True(parser.EndOfData);
                    Assert.Equal(header.Length, row.Length);
                    Assert.Equal(LongName.Replace("\r\n", "\n", StringComparison.Ordinal), row[Array.IndexOf(header, "Name")].Replace("\r\n", "\n", StringComparison.Ordinal));
                    Assert.Equal("'=1+1", row[Array.IndexOf(header, "Operating system")]);
                    Assert.Equal("<script>synthetic</script>", row[Array.IndexOf(header, "Compliance state")]);
                    Assert.Equal("Unknown / not returned", row[Array.IndexOf(header, "Last sync at")]);
                }
                break;
            case ExportFormat.Xlsx:
                using (var zip = new ZipArchive(File.OpenRead(second)))
                {
                    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
                    var document = XDocument.Load(stream);
                    Assert.Empty(document.Descendants(ns + "f"));
                    var rows = document.Descendants(ns + "row").ToArray();
                    Assert.Equal(2, rows.Length);
                    var header = rows[0].Descendants(ns + "t").Select(cell => cell.Value).ToArray();
                    var data = rows[1].Descendants(ns + "t").Select(cell => cell.Value).ToArray();
                    var name = data[Array.IndexOf(header, "Name")];
                    Assert.StartsWith("Jos\u00e9 e\u0301 \U0001F600, \"quoted\"", name, StringComparison.Ordinal);
                    Assert.Contains("TRUNCATED FOR EXCEL", name);
                    Assert.InRange(name.Length, 1, 32767); // Excel's cell limit; no dependency on the writer's private cutoff.
                    Assert.Equal("=1+1", data[Array.IndexOf(header, "Operating system")]);
                    Assert.Equal("Unknown / not returned", data[Array.IndexOf(header, "Last sync at")]);
                    using var metaStream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                    Assert.Contains(XDocument.Load(metaStream).Descendants(ns + "t"), cell => cell.Value.Contains(Detail, StringComparison.Ordinal));
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [Theory]
    [InlineData(ExportFormat.Html)]
    [InlineData(ExportFormat.Json)]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    public void Modified_evidence_is_refused_before_any_output_or_existing_file_change(ExportFormat format)
    {
        using var root = new TempRoot();
        var sentinel = Path.Combine(root.Paths.ReportsDirectory, "existing-synthetic-report.txt");
        File.WriteAllText(sentinel, "Preserve this earlier report.");
        var bytes = File.ReadAllBytes(sentinel);
        var report = Fixture();
        report.Sections[0].Rows[0]["name"] = "Modified after sealing";
        Assert.Throws<ConfigurationException>(() => new ReportExporter(root.Paths, "Synthetic").ExportReport(report, format));
        Assert.Equal(new[] { sentinel }, Directory.GetFiles(root.Paths.ReportsDirectory));
        Assert.Equal(bytes, File.ReadAllBytes(sentinel));
    }

    private static ReportEvidence Fixture()
    {
        var definition = GraphReportRegistry.Find("intune-devices");
        var report = new ReportEvidence
        {
            Id = "7c787078-0000-4000-8000-000000000002", ReportId = definition.Id, TenantId = TestData.TenantA,
            SourceMode = "historical", StartedAt = "2026-10-09T00:00:00Z", EndedAt = "2026-10-09T00:01:00Z",
            ToolkitVersion = "synthetic-robustness-fixture", ModuleVersion = GraphReportRegistry.AdapterVersion,
            Sources = definition.Routes.Select(route => new ReportSource { Api = "v1.0", RegisteredRoute = route.Path, Reference = definition.Reference }).ToList(),
            Status = ReportReadState.Partial, Limitations = ["Synthetic historical test only."],
            Sections = [new ReportSection
            {
                Id = "devices", Status = ReportReadState.Partial, Error = Detail,
                Rows = [ReportEvidenceSchema.Row(new DeviceReportRow
                {
                    Id = TestData.Operator, Name = LongName, ReadStatus = ReportReadState.Collected,
                    OperatingSystem = "=1+1", ComplianceState = "<script>synthetic</script>", LastSyncAt = null
                })]
            }]
        };
        ReportEvidenceSchema.Seal(report);
        return report;
    }
}
