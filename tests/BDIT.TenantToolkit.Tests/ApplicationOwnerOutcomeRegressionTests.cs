using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ApplicationOwnerOutcomeRegressionTests
{
    private static async Task<ApplicationReportEvidence> Capture(ApplicationReportFixture f, bool rootIncomplete = false)
    {
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/v1.0/applications" when r.RequestUri.Query.Contains("skiptoken", StringComparison.Ordinal) => ApplicationReportFixture.ReadHttp.Error(),
            "/v1.0/applications" => ApplicationReportFixture.ReadHttp.Page(
                [ApplicationReportFixture.Object(), ApplicationReportFixture.Object(TestData.Mam)],
                rootIncomplete ? "https://graph.microsoft.com/v1.0/applications?$skiptoken=synthetic" : null),
            "/v1.0/applications/" + TestData.Mam + "/owners" when !rootIncomplete => ApplicationReportFixture.ReadHttp.Error(),
            _ => ApplicationReportFixture.ReadHttp.Page([])
        };
        return await f.Expiry();
    }
    [Theory]
    [InlineData(ExportFormat.Html)]
    [InlineData(ExportFormat.Json)]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    public async Task Promoted_failed_owner_read_is_refused_by_reader_and_every_export_even_when_resealed(ExportFormat format)
    {
        using var f = new ApplicationReportFixture(); var report = await Capture(f);
        var row = report.Sections[0].Rows.Single(r => r["id"]!.GetValue<string>() == TestData.Mam);
        row["ownersReadStatus"] = ReportReadState.Collected; row["ownerReadReason"] = null;
        row["readStatus"] = ReportReadState.Collected; row["error"] = null;
        report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
        var path = Path.Combine(f.Root.Root, "promoted.json"); File.WriteAllText(path, ApplicationReportEvidenceSchema.Serialise(report));
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadFile(path, TestData.TenantA));
        Assert.Throws<ConfigurationException>(() => new ReportExporter(f.Root.Paths, "Synthetic").ExportApplicationReport(report, format));
        Assert.Empty(Directory.EnumerateFiles(f.Root.Paths.ReportsDirectory));
    }
    [Fact]
    public async Task Incomplete_root_does_not_create_a_failed_retained_owner_outcome()
    {
        using var f = new ApplicationReportFixture(); var report = await Capture(f, rootIncomplete: true);
        var counts = Assert.IsType<ApplicationOwnerOutcomes>(report.Sources[2].RetainedOwnerOutcomes);
        Assert.Equal(2, counts.Collected); Assert.Equal(0, counts.Failed);
        Assert.Equal(ReportReadState.Partial, report.Sources[2].Status);
        Assert.All(report.Sections[0].Rows, r => Assert.Equal(ReportReadState.Collected, r["ownersReadStatus"]!.GetValue<string>()));
        Assert.NotNull(ReportEnvelopeDispatcher.Read(ApplicationReportEvidenceSchema.Serialise(report), TestData.TenantA).Schema2);
    }
    [Theory]
    [InlineData("missing")][InlineData("null")][InlineData("negative")][InlineData("overflow")]
    [InlineData("sum")][InlineData("wrong-bucket")][InlineData("root-field")][InlineData("revision")][InlineData("adapter")]
    public async Task Resealed_invalid_counts_or_revision_pairs_are_refused(string fault)
    {
        using var f = new ApplicationReportFixture(); var report = await Capture(f);
        var counts = report.Sources[2].RetainedOwnerOutcomes!;
        switch (fault)
        {
            case "missing": case "null": report.Sources[2].RetainedOwnerOutcomes = null; break;
            case "negative": counts.Collected = -1; break;
            case "overflow": counts.Collected = int.MaxValue; break;
            case "sum": counts.Collected++; break;
            case "wrong-bucket": counts.Collected = 2; counts.Failed = 0; break;
            case "root-field": report.Sources[0].RetainedOwnerOutcomes = new(); break;
            case "revision": report.ReportSchemaVersion = 1; break;
            case "adapter": report.ModuleVersion = ApplicationReportRegistry.AdapterVersion; break;
        }
        report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
        var node = JsonNode.Parse(ApplicationReportEvidenceSchema.Serialise(report))!.AsObject();
        if (fault == "null") node["sources"]![2]!["retainedOwnerOutcomes"] = null;
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.Read(node.ToJsonString(), TestData.TenantA));
    }
    [Theory]
    [InlineData("missing-bucket")][InlineData("unknown-bucket")][InlineData("fractional")][InlineData("duplicate-bucket")]
    public async Task Strict_wire_counts_reject_unknown_missing_fractional_and_duplicate_members(string fault)
    {
        using var f = new ApplicationReportFixture(); var report = await Capture(f);
        var node = JsonNode.Parse(ApplicationReportEvidenceSchema.Serialise(report))!.AsObject();
        var counts = node["sources"]![2]!["retainedOwnerOutcomes"]!.AsObject();
        if (fault == "missing-bucket") counts.Remove("failed");
        if (fault == "unknown-bucket") counts["guess"] = 0;
        if (fault == "fractional") counts["failed"] = 0.5;
        var json = node.ToJsonString();
        if (fault == "duplicate-bucket") json = json.Replace("\"failed\":1", "\"failed\":1,\"failed\":1", StringComparison.Ordinal);
        Assert.ThrowsAny<ToolkitException>(() => ReportEnvelopeDispatcher.Read(json, TestData.TenantA));
    }
    private static byte[] Legacy(ApplicationReportEvidence report)
    {
        report.ReportSchemaVersion = 1; report.ModuleVersion = ApplicationReportRegistry.AdapterVersion;
        foreach (var source in report.Sources) source.RetainedOwnerOutcomes = null;
        report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
        // Preserve whitespace and BOM too, not only object/digest semantics.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("\n" + ApplicationReportEvidenceSchema.Serialise(report) + "\n")).ToArray();
    }
    [Theory]
    [InlineData(ExportFormat.Html)]
    [InlineData(ExportFormat.Json)]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Xlsx)]
    public async Task Original_revision_is_explicit_historical_only_and_every_view_keeps_the_limitation(ExportFormat format)
    {
        using var f = new ApplicationReportFixture(); var report = await Capture(f, true); var original = Legacy(report);
        var path = Path.Combine(f.Root.Root, "original.json"); File.WriteAllBytes(path, original);
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadFile(path, TestData.TenantA));
        Assert.Throws<ConfigurationException>(() => ApplicationReportEvidenceSchema.Seal(report));
        var envelope = ReportEnvelopeDispatcher.ReadHistoricalFile(path, TestData.TenantA);
        Assert.Equal(ApplicationReportEvidenceSchema.HistoricalOwnerNotice, envelope.AssuranceNotice);
        var export = envelope.Export(new ReportExporter(f.Root.Paths, "Synthetic"), format, suppliedFile: true);
        if (format == ExportFormat.Html) Assert.Contains(envelope.AssuranceNotice, File.ReadAllText(export));
        else
        {
            using var zip = ZipFile.OpenRead(export);
            if (format == ExportFormat.Json)
            {
                using var payload = zip.GetEntry("report.json")!.Open(); using var bytes = new MemoryStream(); payload.CopyTo(bytes);
                Assert.Equal(original, bytes.ToArray());
            }
            Assert.Contains(zip.Entries, entry => { using var stream = entry.Open(); using var reader = new StreamReader(stream); return reader.ReadToEnd().Contains(envelope.AssuranceNotice, StringComparison.Ordinal); });
        }
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(1, envelope.Schema2!.ReportSchemaVersion); Assert.Equal(report.IntegrityDigest, envelope.Schema2.IntegrityDigest);
    }
    [Fact]
    public async Task Historical_mode_refuses_new_revision_consent_and_added_null_outcome_fields()
    {
        using var f = new ApplicationReportFixture(); var report = await Capture(f);
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadHistorical(ApplicationReportEvidenceSchema.Serialise(report), TestData.TenantA));
        var consent = await f.Consent(); Assert.Equal(1, consent.ReportSchemaVersion); Assert.Equal(ApplicationReportRegistry.AdapterVersion, consent.ModuleVersion);
        Assert.NotNull(ReportEnvelopeDispatcher.Read(ApplicationReportEvidenceSchema.Serialise(consent), TestData.TenantA).Schema2);
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadHistorical(ApplicationReportEvidenceSchema.Serialise(consent), TestData.TenantA));
        var node = JsonNode.Parse(Encoding.UTF8.GetString(Legacy(report)).TrimStart('\uFEFF'))!.AsObject();
        node["sources"]![2]!["retainedOwnerOutcomes"] = null;
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadHistorical(node.ToJsonString(), TestData.TenantA));
    }
}
