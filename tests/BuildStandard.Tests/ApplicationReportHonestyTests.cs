using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ApplicationReportHonestyTests
{
    [Theory]
    [InlineData(ReportReadState.Failed)]
    [InlineData(ReportReadState.NotAttempted)]
    [InlineData(ReportReadState.Cancelled)]
    [InlineData(ReportReadState.Partial)]
    public async Task Resealed_unavailable_root_cannot_present_a_collected_empty_section_under_a_partial_report(string sourceState)
    {
        using var f = new ApplicationReportFixture(); var report = await f.Expiry();
        SetOutcome(report.Sections[0], ReportReadState.Partial, "Synthetic incomplete application inventory.");
        SetOutcome(report.Sources[0], ReportReadState.Partial, "Synthetic incomplete application inventory.");
        SetOutcome(report.Sources[2], ReportReadState.NotAttempted, "No eligible exact applications were queried.");
        SetOutcome(report.Sources[1], sourceState, "Synthetic service principal metadata refusal.");
        SetOutcome(report.Sources[3], ReportReadState.NotAttempted, "Service principal inventory unavailable.");
        report.Status = ReportReadState.Partial;
        RejectResealedImportAndExports(f, report);
    }
    [Fact]
    public async Task Resealed_partial_row_cannot_be_promoted_to_a_collected_section()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications" ? [ApplicationReportFixture.Object()] : []);
        var report = await f.Expiry();
        report.Sections[0].Rows[0]["readStatus"] = ReportReadState.Partial;
        report.Sections[0].Rows[0]["error"] = "Synthetic returned row remains incomplete.";
        RejectResealedImportAndExports(f, report, "Partial rows cannot become a successful section.");
    }
    [Fact]
    public async Task Resealed_incomplete_registered_source_cannot_claim_a_collected_report()
    {
        using var f = new ApplicationReportFixture(); var report = await f.Expiry();
        SetOutcome(report.Sources[2], ReportReadState.NotAttempted, "This supplied source claim did not attempt ownership reads.");
        RejectResealedImportAndExports(f, report, "Incomplete source reads cannot claim a collected report.");
    }
    [Theory]
    [InlineData("root-count")]
    [InlineData("owner-count")]
    [InlineData("owner-attempts")]
    [InlineData("owner-failed-source")]
    [InlineData("owner-collected-source")]
    public async Task Resealed_registered_counts_and_owner_read_claims_cannot_contradict_returned_metadata(string fault)
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications" ? [ApplicationReportFixture.Object()] : []);
        var report = await f.Expiry();
        if (fault == "root-count") report.Sources[0].Rows = 0;
        if (fault == "owner-count") report.Sources[2].Rows = 1;
        if (fault == "owner-attempts") report.Sources[2].ObjectsAttempted = 0;
        if (fault == "owner-failed-source") SetOutcome(report.Sources[2], ReportReadState.Failed, "Supplied owner query failed.");
        if (fault == "owner-collected-source")
        {
            var row = report.Sections[0].Rows[0]; row["ownersReadStatus"] = ReportReadState.Partial; row["ownerReadReason"] = "Ownership pagination remains incomplete.";
            row["readStatus"] = ReportReadState.Partial; row["error"] = "Ownership remains incomplete.";
            SetOutcome(report.Sections[0], ReportReadState.Partial, "Ownership remains incomplete.");
            SetOutcome(report.Sources[0], ReportReadState.Partial, "Ownership remains incomplete."); report.Status = ReportReadState.Partial;
        }
        RejectResealedImportAndExports(f, report);
    }
    [Fact]
    public async Task Resealed_owner_sources_cannot_double_the_shared_child_fanout_allowance()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/v1.0/applications" => ApplicationReportFixture.ReadHttp.Page(Enumerable.Range(1, ApplicationReportRegistry.MaximumChildObjects).Select(i => ApplicationReportFixture.Object(ApplicationReportFixture.Id(i)))),
            "/v1.0/servicePrincipals" => ApplicationReportFixture.ReadHttp.Page(Enumerable.Range(1001, ApplicationReportRegistry.MaximumChildObjects).Select(i => ApplicationReportFixture.Object(ApplicationReportFixture.Id(i)))),
            _ => ApplicationReportFixture.ReadHttp.Page([])
        };
        var report = await f.Expiry();
        Assert.Equal(ApplicationReportRegistry.MaximumChildObjects, report.Sources[2].ObjectsAttempted); Assert.Equal(0, report.Sources[3].ObjectsAttempted);
        foreach (var row in report.Sections[1].Rows)
        { row["ownersReadStatus"] = ReportReadState.Collected; row["ownerReadReason"] = null; row["readStatus"] = ReportReadState.Collected; row["error"] = null; }
        SetOutcome(report.Sources[3], ReportReadState.Collected, null); report.Sources[3].ObjectsAttempted = ApplicationReportRegistry.MaximumChildObjects;
        RejectResealedImportAndExports(f, report, "Combined child-object attempts exceed the report fan-out bound.");
    }
    [Fact]
    public async Task Exactly_saturated_metadata_budget_does_not_claim_unqueried_root_pages_collected()
    {
        using var f = new ApplicationReportFixture();
        const int budget = ApplicationReportEvidenceSchema.MaximumBytes - 1024 * 1024;
        static ApplicationPrincipalReportRow Principal(int index, int length) => new()
        {
            Id = ApplicationReportFixture.Id(index), Name = "Same synthetic name", ApplicationId = TestData.ClientId,
            IdentityStatus = "Exact", ReadStatus = ReportReadState.Collected, AppRolesReadStatus = ReportReadState.Collected,
            AppRoles = Enumerable.Range(1, ApplicationReportRegistry.MaximumAppRoles).Select(i => new ApplicationRoleMetadata
            { Id = ApplicationReportFixture.Id(i), DisplayName = new string('X', length), Value = new string('X', length), IsEnabled = true }).ToList()
        };
        static int AdmissionBytes(ApplicationPrincipalReportRow row)
        {
            var node = ApplicationReportEvidenceSchema.Row(row);
            static int Nodes(JsonNode? value) => value is JsonObject o ? 1 + o.Sum(p => Nodes(p.Value)) : value is JsonArray a ? 1 + a.Sum(Nodes) : 1;
            return Encoding.UTF8.GetByteCount(node.ToJsonString()) + Nodes(node) * 24 + 1024;
        }
        var full = Principal(1, 4096); var fullSize = AdmissionBytes(full);
        var baseSize = AdmissionBytes(Principal(1, 1)); var fullCount = (budget - baseSize) / fullSize;
        var rows = Enumerable.Range(1, fullCount).Select(i => Principal(i, 4096)).ToList();
        var last = Principal(fullCount + 1, 1); var remaining = budget - fullCount * fullSize - baseSize;
        foreach (var role in last.AppRoles)
        {
            var length = Math.Min(4095, remaining); role.DisplayName = new string('X', 1 + length); remaining -= length;
            length = Math.Min(4095, remaining); role.Value = new string('X', 1 + length); remaining -= length;
        }
        Assert.Equal(0, remaining); rows.Add(last); Assert.Equal(budget, rows.Sum(AdmissionBytes));
        var objects = rows.Select(row => ApplicationReportFixture.Principal(row.Id!, new JsonArray(row.AppRoles.Select(role =>
            (JsonNode)ApplicationReportFixture.Role(role.Id!, role.DisplayName!, role.Value!)).ToArray()))).ToArray();
        const string unread = "https://graph.microsoft.com/v1.0/servicePrincipals?$skiptoken=unqueried-after-exact-budget";
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath == "/v1.0/servicePrincipals"
            ? ApplicationReportFixture.ReadHttp.Page(objects, unread) : ApplicationReportFixture.ReadHttp.Page([]);
        var report = await f.Consent();
        Assert.Equal(rows.Count, report.Sections[0].Rows.Count);
        Assert.DoesNotContain(f.Http.Routes, r => r.Contains("unqueried-after-exact-budget", StringComparison.Ordinal));
        Assert.Equal(ReportReadState.Partial, report.Sections[0].Status);
        Assert.Equal(ReportReadState.Partial, report.Sources[0].Status);
        Assert.Contains("byte", report.Sources[0].Error, StringComparison.OrdinalIgnoreCase);
        Assert.True(Encoding.UTF8.GetByteCount(ApplicationReportEvidenceSchema.Serialise(report)) <= ApplicationReportEvidenceSchema.MaximumBytes);
    }
    private static void SetOutcome(ReportSection section, string state, string? reason) { section.Status = state; section.Error = reason; }
    private static void SetOutcome(ApplicationReportSource source, string state, string? reason) { source.Status = state; source.Error = reason; }
    private static void RejectResealedImportAndExports(ApplicationReportFixture fixture, ApplicationReportEvidence report, string? expectedReason = null)
    {
        report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
        Assert.True(EvidenceIntegrity.Verify<object>(report, report.IntegrityDigest));
        var json = ApplicationReportEvidenceSchema.Serialise(report); var file = Path.Combine(fixture.Root.Root, "resealed-supplied.json"); File.WriteAllText(file, json);
        var exporter = new ReportExporter(fixture.Root.Paths, "Synthetic");
        var exceptions = new List<Exception?> { Record.Exception(() => ReportEnvelopeDispatcher.ReadFile(file, TestData.TenantA)) };
        foreach (var format in new[] { ExportFormat.Html, ExportFormat.Json, ExportFormat.Csv, ExportFormat.Xlsx })
            exceptions.Add(Record.Exception(() => exporter.ExportApplicationReport(report, format, suppliedFile: true)));
        Assert.All(exceptions, error => { var refused = Assert.IsType<ConfigurationException>(error); if (expectedReason is not null) Assert.Equal(expectedReason, refused.Message); });
        Assert.Empty(Directory.EnumerateFiles(fixture.Root.Paths.ReportsDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(json, File.ReadAllText(file));
    }
}
