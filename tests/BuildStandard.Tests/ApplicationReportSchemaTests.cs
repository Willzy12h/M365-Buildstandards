using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ApplicationReportSchemaTests
{
    [Theory]
    [InlineData("expiry")]
    [InlineData("consent")]
    public async Task Concrete_schema_dispatch_preserves_typed_parameters_and_original_bytes(string kind)
    {
        using var f = new ApplicationReportFixture(); var report = kind == "expiry" ? await f.Expiry(365) : await f.Consent();
        var json = ApplicationReportEvidenceSchema.Serialise(report); var envelope = ReportEnvelopeDispatcher.Read(json, TestData.TenantA);
        Assert.Equal(2, envelope.SchemaVersion); Assert.Null(envelope.Schema1); Assert.Equal(json, ApplicationReportEvidenceSchema.Serialise(envelope.Schema2!));
        if (kind == "expiry") Assert.Equal(365, Assert.IsType<ApplicationExpiryEvidence>(envelope.Schema2).Parameters.HorizonDays);
        else Assert.IsType<ApplicationConsentEvidence>(envelope.Schema2);
        Assert.Throws<ConfigurationException>(() => ReportEvidenceSchema.Read(json, TestData.TenantA));
        Assert.Throws<TenantMismatchException>(() => ReportEnvelopeDispatcher.Read(json, TestData.TenantB));
    }
    [Fact]
    public async Task Schema1_reader_registration_wire_and_export_remain_unchanged()
    {
        using var f = new ApplicationReportFixture();
        var legacy = await new GraphReportService(f.Clock).CaptureAsync(f.Graph.ForReports(), f.Session, "directory-audit",
            new ReportParameters { Start = "2026-09-10T00:00:00Z", End = "2026-09-11T00:00:00Z" }, default);
        var original = ReportEvidenceSchema.Serialize(legacy); var routed = ReportEnvelopeDispatcher.Read(original, TestData.TenantA);
        Assert.Equal(1, routed.SchemaVersion); Assert.Null(routed.Schema2); Assert.Equal(original, ReportEvidenceSchema.Serialize(routed.Schema1!));
        var node = JsonNode.Parse(original)!.AsObject(); node["parameters"]!["horizonDays"] = 30;
        Assert.Throws<ConfigurationException>(() => ReportEvidenceSchema.Read(node.ToJsonString(), TestData.TenantA));
        legacy.SchemaVersion = 2;
        var exporter = new ReportExporter(f.Root.Paths, "Synthetic");
        Assert.Throws<ConfigurationException>(() => exporter.ExportReport(legacy, ExportFormat.Json));
        Assert.Empty(Directory.EnumerateFiles(f.Root.Paths.ReportsDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(5, GraphReportRegistry.Definitions.Count);
    }
    [Theory]
    [InlineData("unknown-root")]
    [InlineData("unknown-parameters")]
    [InlineData("duplicate-root")]
    [InlineData("wrong-schema-id")]
    [InlineData("wrong-kind")]
    [InlineData("wrong-adapter")]
    [InlineData("horizon-zero")]
    [InlineData("horizon-fractional")]
    [InlineData("horizon-string")]
    [InlineData("missing-horizon")]
    [InlineData("tampered")]
    public async Task Unknown_duplicate_mismatched_or_untyped_fields_refuse_before_export(string fault)
    {
        using var f = new ApplicationReportFixture(); var report = await f.Expiry();
        var node = JsonNode.Parse(ApplicationReportEvidenceSchema.Serialise(report))!.AsObject();
        switch (fault)
        {
            case "unknown-root": node["secretText"] = "SYNTHETIC_SECRET_VALUE"; break;
            case "unknown-parameters": node["parameters"]!["end"] = "2026-01-01T00:00:00Z"; break;
            case "wrong-schema-id": node["schemaVersion"] = 1; break;
            case "wrong-kind": node["kind"] = "tenantSnapshot"; break;
            case "wrong-adapter": node["moduleVersion"] = "unregistered"; break;
            case "horizon-zero": node["parameters"]!["horizonDays"] = 0; break;
            case "horizon-fractional": node["parameters"]!["horizonDays"] = 1.5; break;
            case "horizon-string": node["parameters"]!["horizonDays"] = "30"; break;
            case "missing-horizon": node["parameters"]!.AsObject().Remove("horizonDays"); break;
            case "tampered": node["limitations"]![0] = "Edited without resealing"; break;
        }
        var json = node.ToJsonString(); if (fault == "duplicate-root") json = json.Replace("\"kind\":", "\"kind\":\"reportEvidence\",\"kind\":", StringComparison.Ordinal);
        Assert.ThrowsAny<ToolkitException>(() => ReportEnvelopeDispatcher.Read(json, TestData.TenantA));
        Assert.Empty(Directory.EnumerateFiles(f.Root.Paths.ReportsDirectory, "*", SearchOption.AllDirectories));
    }
    [Theory]
    [InlineData("horizonDays")]
    [InlineData("start")]
    public async Task Consent_parameters_are_strictly_empty(string parameter)
    {
        using var f = new ApplicationReportFixture(); var report = await f.Consent();
        var node = JsonNode.Parse(ApplicationReportEvidenceSchema.Serialise(report))!.AsObject(); node["parameters"]![parameter] = 30;
        Assert.Throws<ConfigurationException>(() => ApplicationReportEvidenceSchema.Read(node.ToJsonString(), TestData.TenantA));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task Invalid_typed_horizon_refuses_before_token_or_http_read(int days)
    {
        using var f = new ApplicationReportFixture(); await Assert.ThrowsAsync<ConfigurationException>(() => f.Expiry(days));
        Assert.Empty(f.Http.Routes); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public async Task Wrong_resource_role_name_and_credential_classification_refuse_even_when_resealed()
    {
        using var f = new ApplicationReportFixture(); f.ConsentData(); var consent = await f.Consent();
        consent.Sections[2].Rows[0]["roleName"] = "Same display name guessed from another resource";
        consent.IntegrityDigest = EvidenceIntegrity.Compute<object>(consent);
        Assert.Throws<ConfigurationException>(() => ApplicationReportEvidenceSchema.Validate(consent, TestData.TenantA));
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications"
            ? [ApplicationReportFixture.Object(passwords: [ApplicationReportFixture.Credential("2026-09-20T00:00:00Z")])] : []);
        var expiry = await f.Expiry(); expiry.Sections[0].Rows[0]["credentials"]![0]!["classification"] = "Expired";
        expiry.IntegrityDigest = EvidenceIntegrity.Compute<object>(expiry);
        Assert.Throws<ConfigurationException>(() => ApplicationReportEvidenceSchema.Validate(expiry, TestData.TenantA));
    }
    [Fact]
    public async Task Restricted_clone_routes_are_selected_by_schema_and_id_without_widening_legacy_reads_or_writes()
    {
        using var f = new ApplicationReportFixture();
        Assert.Throws<ConfigurationException>(() => f.Graph.ForApplicationReports(1, "application-expiry"));
        Assert.Throws<ConfigurationException>(() => f.Graph.ForApplicationReports(2, "intune-devices"));
        await Assert.ThrowsAsync<WriteDeniedException>(() => f.Graph.ForReports().GetAsync(GraphApi.V1, "/applications", default));
        await Assert.ThrowsAsync<WriteDeniedException>(() => f.Graph.ForApplicationReports(2, "application-expiry").GetAsync(GraphApi.V1, "/oauth2PermissionGrants", default));
        await Assert.ThrowsAsync<WriteDeniedException>(() => f.Graph.ForApplicationReports(2, "application-consent").GetAsync(GraphApi.V1, "/applications", default));
        await Assert.ThrowsAsync<WriteDeniedException>(() => f.Graph.ForApplicationReports(2, "application-consent").GetAsync(GraphApi.V1, "/servicePrincipals/" + TestData.Operator + "/owners", default));
        await Assert.ThrowsAsync<WriteDeniedException>(() => f.Graph.ForApplicationReports(2, "application-expiry").GetAsync(GraphApi.V1, "/applications/" + TestData.Operator + "/passwordCredentials", default));
        await Assert.ThrowsAsync<WriteDeniedException>(() => f.Graph.ForApplicationReports(2, "application-consent").WriteAsync(GraphApi.V1, GraphWriteMethod.Post, "/servicePrincipals/" + TestData.Operator, new(), default));
        Assert.Empty(f.Http.Routes); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public async Task Immutable_store_and_supplied_loader_preserve_record_identity_and_reader_limits()
    {
        using var f = new ApplicationReportFixture(); var report = await f.Expiry(); var store = new EvidenceStore(f.Root.Paths, NullLog.Instance);
        var path = store.SaveApplicationReport(report); var bytes = File.ReadAllBytes(path);
        Assert.Equal(report.Id, store.LoadApplicationReport(TestData.TenantA, report.Id)!.Id);
        Assert.Throws<SafetyViolationException>(() => store.SaveApplicationReport(report)); Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Throws<ConfigurationException>(() => store.LoadReport(TestData.TenantA, report.Id));
        var supplied = Path.Combine(f.Root.Root, "supplied.json"); File.WriteAllBytes(supplied, Encoding.UTF8.GetPreamble().Concat(bytes).ToArray());
        Assert.Equal(report.Id, ReportEnvelopeDispatcher.ReadFile(supplied, TestData.TenantA).Schema2!.Id);
        Assert.Throws<TenantMismatchException>(() => ReportEnvelopeDispatcher.ReadFile(supplied, TestData.TenantB));
        File.WriteAllBytes(supplied, [0xff, 0xfe, 0xff]); Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadFile(supplied, TestData.TenantA));
        using (var stream = new FileStream(supplied, FileMode.Create)) stream.SetLength(ApplicationReportEvidenceSchema.MaximumBytes + 1L);
        Assert.Throws<ConfigurationException>(() => ReportEnvelopeDispatcher.ReadFile(supplied, TestData.TenantA));
        Assert.Equal(32 * 1024 * 1024, ApplicationReportEvidenceSchema.MaximumBytes); Assert.Equal(bytes, File.ReadAllBytes(path));
    }
}
