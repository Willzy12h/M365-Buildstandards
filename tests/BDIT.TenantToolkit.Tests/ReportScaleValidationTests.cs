using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Microsoft.VisualBasic.FileIO;
using Xunit;
using Xunit.Abstractions;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Offline measurements with correctness gates, never machine-dependent timing thresholds.</summary>
public sealed class ReportScaleValidationTests(ITestOutputHelper output)
{
    private const string CapReason = "Report row cap of 5000 reached; reading stopped and omitted records were not read or checked.";

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void Measured_round_trip_and_exports_preserve_every_row_and_unknown_at_existing_limits(int rows)
    {
        // A small warm-up runs every measured path; fixture creation and correctness checks are not timed.
        var warm = Fixture(2);
        ReportEvidenceSchema.Seal(warm);
        ReportEvidenceSchema.Read(ReportEvidenceSchema.Serialize(warm), TestData.TenantA);
        RegisteredReportDocuments.Html(warm, "Synthetic benchmark");
        CsvWriter.ZipSheets(RegisteredReportDocuments.Sheets(warm));
        XlsxWriter.Write(RegisteredReportDocuments.Sheets(warm));

        var report = Fixture(rows);
        Measure(rows, "seal", () => { ReportEvidenceSchema.Seal(report); return report.IntegrityDigest; });
        var json = ReportEvidenceSchema.Serialize(report);
        var restored = Measure(rows, "read", () => ReportEvidenceSchema.Read(json, TestData.TenantA));
        Assert.Equal(report.IntegrityDigest, restored.IntegrityDigest);
        Assert.Equal(rows, restored.Sections[0].Rows.Count);
        Assert.Equal(report.Sections[0].Rows[^1]["id"]!.GetValue<string>(), restored.Sections[0].Rows[^1]["id"]!.GetValue<string>());
        Assert.All(restored.Sections[0].Rows, row => Assert.Null(row["lastSyncAt"]));

        var html = Measure(rows, "html", () => RegisteredReportDocuments.Html(restored, "Synthetic benchmark"));
        Assert.Contains("Unknown / not returned", html);
        Assert.Contains(Id(rows), html);
        // One metadata header, one data header, provenance rows and exactly the selected data rows.
        var sheets = RegisteredReportDocuments.Sheets(restored);
        Assert.Equal(sheets.Sum(sheet => sheet.Rows.Count), Count(html, "<tr>"));

        var csv = Measure(rows, "csv", () => CsvWriter.ZipSheets(RegisteredReportDocuments.Sheets(restored)));
        using (var zip = new ZipArchive(new MemoryStream(csv)))
        {
            Assert.Equal(2, zip.Entries.Count);
            using var reader = new StreamReader(zip.GetEntry("02_devices.csv")!.Open());
            using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
            parser.SetDelimiters(",");
            var header = parser.ReadFields()!;
            var idColumn = Array.IndexOf(header, "Object ID");
            var syncColumn = Array.IndexOf(header, "Last sync at");
            Assert.True(idColumn >= 0 && syncColumn >= 0);
            var parsed = 0;
            string[]? last = null;
            while (!parser.EndOfData)
            {
                last = parser.ReadFields()!;
                Assert.Equal(header.Length, last.Length);
                Assert.Equal("Unknown / not returned", last[syncColumn]);
                parsed++;
            }
            Assert.Equal(rows, parsed);
            Assert.Equal(Id(rows), last![idColumn]);
        }

        var xlsx = Measure(rows, "xlsx", () => XlsxWriter.Write(RegisteredReportDocuments.Sheets(restored)));
        using (var zip = new ZipArchive(new MemoryStream(xlsx)))
        {
            using var stream = zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Assert.Equal(rows + 1, document.Descendants(ns + "row").Count());
            Assert.Empty(document.Descendants(ns + "f"));
            Assert.Contains(document.Descendants(ns + "t"), cell => cell.Value == Id(rows));
            var syncColumn = Array.IndexOf(sheets[1].Rows[0], "Last sync at");
            Assert.Equal(rows, document.Descendants(ns + "row").Skip(1).Count(row =>
                row.Elements(ns + "c").ElementAt(syncColumn).Descendants(ns + "t").Single().Value == "Unknown / not returned"));
        }
        Assert.Equal(json, ReportEvidenceSchema.Serialize(report));
    }

    [Fact]
    public void Exceeding_the_existing_row_limit_is_refused_before_any_export_document()
    {
        var report = Fixture(GraphReportRegistry.MaximumRows + 1);
        Assert.Throws<ConfigurationException>(() => ReportEvidenceSchema.Seal(report));
        Assert.Throws<ConfigurationException>(() => RegisteredReportDocuments.Sheets(report));
        Assert.Throws<ConfigurationException>(() => RegisteredReportDocuments.Html(report, "Synthetic benchmark"));
    }

    [Fact]
    public void A_cap_reached_partial_report_keeps_the_truncation_reason_in_every_export()
    {
        var report = Fixture(GraphReportRegistry.MaximumRows, capReached: true);
        ReportEvidenceSchema.Seal(report);
        var restored = ReportEvidenceSchema.Read(ReportEvidenceSchema.Serialize(report), TestData.TenantA);
        Assert.Equal(ReportReadState.Partial, restored.Status);
        Assert.Equal(GraphReportRegistry.MaximumRows, restored.Sections.Single().Rows.Count);
        Assert.Equal(CapReason, restored.Sections.Single().Error);
        Assert.Contains(CapReason, RegisteredReportDocuments.Html(restored, "Synthetic benchmark"), StringComparison.Ordinal);

        using var csv = new ZipArchive(new MemoryStream(CsvWriter.ZipSheets(RegisteredReportDocuments.Sheets(restored))));
        using var provenance = new StreamReader(csv.GetEntry("01_Report_provenance.csv")!.Open());
        Assert.Contains(CapReason, provenance.ReadToEnd(), StringComparison.Ordinal);

        using var xlsx = new ZipArchive(new MemoryStream(XlsxWriter.Write(RegisteredReportDocuments.Sheets(restored))));
        using var metadata = xlsx.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Contains(XDocument.Load(metadata).Descendants(ns + "t"), cell => cell.Value.Contains(CapReason, StringComparison.Ordinal));
    }

    private T Measure<T>(int rows, string stage, Func<T> operation)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var value = operation();
        timer.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var bytes = value switch { string text => Encoding.UTF8.GetByteCount(text), byte[] buffer => buffer.Length, _ => 0 };
        output.WriteLine("BDIT_SCALE " + JsonSerializer.Serialize(new
        {
            rows, stage, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds,
            allocatedBytesOnCurrentThread = allocated, outputBytes = bytes
        }));
        return value;
    }

    private static int Count(string text, string needle) => (text.Length - text.Replace(needle, "", StringComparison.Ordinal).Length) / needle.Length;
    private static string Id(int number) => new Guid(number, 0, 0, new byte[8]).ToString("D");
    private static ReportEvidence Fixture(int rows, bool capReached = false)
    {
        var definition = GraphReportRegistry.Find("intune-devices");
        return new ReportEvidence
        {
            Id = "7c787078-0000-4000-8000-000000000001", TenantId = TestData.TenantA,
            ReportId = definition.Id, SourceMode = "historical", AccountObjectId = null,
            StartedAt = "2026-10-09T00:00:00Z", EndedAt = "2026-10-09T00:01:00Z",
            ToolkitVersion = "synthetic-scale-fixture", ModuleVersion = GraphReportRegistry.AdapterVersion,
            Sources = definition.Routes.Select(route => new ReportSource
            { Api = "v1.0", RegisteredRoute = route.Path, Reference = definition.Reference }).ToList(),
            Status = ReportReadState.Partial, Limitations = ["Synthetic offline fixture; never live evidence."],
            Sections = [new ReportSection
            {
                Id = "devices", Status = ReportReadState.Partial,
                Error = capReached ? CapReason : "Last sync was not returned for these synthetic rows.",
                Rows = Enumerable.Range(1, rows).Select(i => ReportEvidenceSchema.Row(new DeviceReportRow
                {
                    Id = Id(i), Name = "Synthetic device " + i, ReadStatus = ReportReadState.Partial,
                    Error = "Last sync was not returned.",
                    Ownership = "company", EnrolmentType = "windowsAzureADJoin", ManagementAgent = "mdm",
                    OperatingSystem = "Windows", OsVersion = "synthetic", ComplianceState = "compliant",
                    UserId = TestData.Operator, LastSyncAt = null
                })).ToList()
            }]
        };
    }
}
