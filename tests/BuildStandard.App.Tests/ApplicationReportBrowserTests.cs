using System.Text.Json.Nodes;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ApplicationReportBrowserTests
{
    [Fact]
    public void Choices_preserve_the_five_legacy_pairs_and_add_only_two_application_pairs()
    {
        Assert.Equal(7, GraphReportPresentation.Choices.Count);
        Assert.Equal(GraphReportRegistry.Definitions.Select(d => d.Id), GraphReportPresentation.Choices.Take(5).Select(d => d.Id));
        Assert.All(GraphReportPresentation.Choices.Take(5), d => Assert.Equal(1, d.SchemaVersion));
        Assert.Equal(new[] { "application-expiry", "application-consent" }, GraphReportPresentation.Choices.Skip(5).Select(d => d.Id));
        Assert.All(GraphReportPresentation.Choices.Skip(5), d => Assert.Equal(2, d.SchemaVersion));
        Assert.Throws<ConfigurationException>(() => GraphReportPresentation.Find(1, "application-expiry"));
        Assert.Throws<ConfigurationException>(() => GraphReportPresentation.Find(2, "intune-devices"));
        Assert.Throws<ConfigurationException>(() => GraphReportPresentation.Find(3, "application-consent"));
    }
    [Fact]
    public void Mixed_schema_history_lists_and_opens_original_application_records_without_resealing()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var expiry = ApplicationReportUiFixtures.Expiry(TestData.TenantA, TestData.Operator, "synthetic");
        var consent = ApplicationReportUiFixtures.Consent(TestData.TenantA, TestData.Operator, "synthetic");
        var legacy = new ReportEvidence { Id = Guid.NewGuid().ToString(), ReportId = "intune-devices", TenantId = TestData.TenantA,
            AccountObjectId = TestData.Operator, StartedAt = ApplicationReportUiFixtures.At, EndedAt = ApplicationReportUiFixtures.At,
            ToolkitVersion = "synthetic", ModuleVersion = GraphReportRegistry.AdapterVersion, Status = ReportReadState.Collected,
            Sources = GraphReportRegistry.Find("intune-devices").Routes.Select(r => new ReportSource { Api = "v1.0", RegisteredRoute = r.Path,
                Reference = GraphReportRegistry.Find("intune-devices").Reference }).ToList(),
            Sections = [new() { Id = "devices", Status = ReportReadState.Collected }] };
        ReportEvidenceSchema.Seal(legacy); store.SaveReport(legacy);
        var files = new[] { store.SaveApplicationReport(expiry), store.SaveApplicationReport(consent) };
        var browser = new GraphReportEvidenceBrowser(store); var listing = browser.List(TestData.TenantA);
        Assert.True(listing.Complete); Assert.Equal(3, listing.Entries.Count); Assert.All(listing.Entries, e => Assert.True(e.CanOpen, e.Problem));
        foreach (var report in new ApplicationReportEvidence[] { expiry, consent })
        {
            var accepted = Assert.IsType<ReportEnvelopeDispatcher.Envelope>(browser.OpenStored(TestData.TenantA, report.Id));
            Assert.Equal(ApplicationReportEvidenceSchema.Serialise(report), ApplicationReportEvidenceSchema.Serialise(accepted.Schema2!));
            Assert.Contains(listing.Entries, e => e.Label.Contains(ApplicationReportRegistry.Find(2, report.ReportId).Name, StringComparison.Ordinal));
        }
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var supplied = Assert.IsType<ReportEnvelopeDispatcher.Envelope>(browser.OpenSupplied(file, TestData.TenantA));
            Assert.NotNull(supplied.Schema2); Assert.Equal(bytes, File.ReadAllBytes(file));
        }
        Assert.Equal(3, Directory.GetFiles(Path.GetDirectoryName(files[0])!).Length);
    }
    [Fact]
    public void Large_native_fixture_is_strict_and_keeps_each_nested_child_projection()
    {
        var report = ApplicationReportUiFixtures.Expiry(TestData.TenantA, TestData.Operator, "synthetic", 120);
        ApplicationReportEvidenceSchema.Validate(report, TestData.TenantA);
        var sheets = ApplicationReportDocuments.Sheets(report);
        Assert.Equal(121, sheets.Single(s => s.Name == "applications").Rows.Count);
        Assert.Equal(241, sheets.Single(s => s.Name == "Credentials").Rows.Count);
        Assert.Equal(481, sheets.Single(s => s.Name == "Owners").Rows.Count);
    }
    [Theory]
    [InlineData("expiry")] [InlineData("consent")]
    public void Child_tables_keep_parent_identity_limitations_and_original_provenance(string family)
    {
        ApplicationReportEvidence report = family == "expiry" ? ApplicationReportUiFixtures.Expiry(TestData.TenantA, TestData.Operator, "synthetic")
            : ApplicationReportUiFixtures.Consent(TestData.TenantA, TestData.Operator, "synthetic");
        var wrapped = ReportEnvelopeDispatcher.Wrap(report, TestData.TenantA);
        Assert.Same(report, wrapped.Schema2);
        var sheets = GraphReportPresentation.Sheets(wrapped, true);
        Assert.Contains(RegisteredReportDocuments.SuppliedFileNotice, sheets[0].Rows.SelectMany(r => r));
        Assert.Contains(report.IntegrityDigest, sheets[0].Rows.SelectMany(r => r));
        var children = family == "expiry" ? new[] { "Credentials", "Owners" } : new[] { "Role definitions" };
        foreach (var name in children)
        {
            var sheet = Assert.Single(sheets, s => s.Name == name);
            Assert.True(sheet.Rows.Count >= 3); Assert.All(sheet.Rows.Skip(1), r => Assert.True(Guid.TryParse(r[0], out _)));
        }
        Assert.ThrowsAny<ToolkitException>(() => ReportEvidenceSchema.Read(ApplicationReportEvidenceSchema.Serialise(report), TestData.TenantA));
        Assert.Throws<TenantMismatchException>(() => ReportEnvelopeDispatcher.Wrap(report, TestData.TenantB));
        report.IntegrityDigest = new string('f', 64);
        Assert.ThrowsAny<ToolkitException>(() => ReportEnvelopeDispatcher.Wrap(report, TestData.TenantA));
    }
    [Theory]
    [InlineData("schema")] [InlineData("id")] [InlineData("unknown")] [InlineData("digest")]
    [InlineData("tenant")] [InlineData("date")] [InlineData("horizon")]
    public void Supplied_consent_refuses_unregistered_fields_pairs_and_modified_evidence(string fault)
    {
        using var root = new TempRoot(); var report = ApplicationReportUiFixtures.Consent(TestData.TenantA, TestData.Operator, "synthetic");
        var json = JsonNode.Parse(ApplicationReportEvidenceSchema.Serialise(report))!.AsObject();
        switch (fault)
        {
            case "schema": json["schemaVersion"] = 1; break;
            case "id": json["reportId"] = "intune-devices"; break;
            case "unknown": json["secretText"] = "synthetic-not-admitted"; break;
            case "digest": json["integrityDigest"] = new string('a', 64); break;
            case "tenant": json["tenantId"] = TestData.TenantB; break;
            case "date": json["parameters"]!["start"] = ApplicationReportUiFixtures.At; break;
            case "horizon": json["parameters"]!["horizonDays"] = 30; break;
        }
        var file = Path.Combine(root.Root, "supplied.json"); File.WriteAllText(file, json.ToJsonString()); var before = File.ReadAllBytes(file);
        Assert.ThrowsAny<ToolkitException>(() => new GraphReportEvidenceBrowser(new(root.Paths, NullLog.Instance)).OpenSupplied(file, TestData.TenantA));
        Assert.Equal(before, File.ReadAllBytes(file)); Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public void Application_stored_identity_and_supplied_encoding_size_and_links_keep_strict_admission()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var report = ApplicationReportUiFixtures.Expiry(TestData.TenantA, TestData.Operator, "synthetic");
        var saved = store.SaveApplicationReport(report); var browser = new GraphReportEvidenceBrowser(store);
        var other = Guid.NewGuid().ToString(); File.Copy(saved, Path.Combine(Path.GetDirectoryName(saved)!, other + ".json"));
        Assert.Throws<IntegrityException>(() => browser.OpenStored(TestData.TenantA, other));
        Assert.Contains(browser.List(TestData.TenantA).Entries, e => e.Id == other && !e.CanOpen);
        var file = Path.Combine(root.Root, "invalid.json"); File.WriteAllBytes(file, [0xff]);
        Assert.Throws<ConfigurationException>(() => browser.OpenSupplied(file, TestData.TenantA));
        using (var stream = new FileStream(file, FileMode.Create)) stream.SetLength(GraphReportEvidenceBrowser.MaximumFileBytes + 1L);
        Assert.Throws<ConfigurationException>(() => browser.OpenSupplied(file, TestData.TenantA));
        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(root.Root, "linked.json"); File.CreateSymbolicLink(link, saved);
            Assert.Throws<ConfigurationException>(() => browser.OpenSupplied(link, TestData.TenantA));
        }
    }
}
