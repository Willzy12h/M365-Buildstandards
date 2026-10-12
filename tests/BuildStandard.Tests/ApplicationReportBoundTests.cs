using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ApplicationReportBoundTests
{
    [Theory]
    [InlineData("credentials")]
    [InlineData("owners")]
    [InlineData("roles")]
    public async Task Every_nested_list_cap_retains_bounded_metadata_with_an_explicit_partial_reason(string kind)
    {
        using var f = new ApplicationReportFixture(); var item = ApplicationReportFixture.Object();
        var credentials = Enumerable.Range(1, ApplicationReportRegistry.MaximumCredentials + 1).Select(i => ApplicationReportFixture.Credential("2026-09-20T00:00:00Z", id: ApplicationReportFixture.Id(i))).ToArray();
        if (kind == "credentials") item["passwordCredentials"] = new JsonArray(credentials.Select(c => (JsonNode)c).ToArray());
        var owners = Enumerable.Range(1, ApplicationReportRegistry.MaximumOwners + 1).Select(i => new JsonObject { ["id"] = ApplicationReportFixture.Id(i), ["displayName"] = "Synthetic owner" }).ToArray();
        var roles = new JsonArray(Enumerable.Range(1, ApplicationReportRegistry.MaximumAppRoles + 1).Select(i => (JsonNode)ApplicationReportFixture.Role(ApplicationReportFixture.Id(i))).ToArray());
        f.Http.Response = (r, _) => r.RequestUri!.AbsolutePath switch
        {
            "/v1.0/applications" => ApplicationReportFixture.ReadHttp.Page([item]),
            "/v1.0/servicePrincipals" when kind == "roles" => ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Principal(TestData.Operator, roles)]),
            _ when r.RequestUri.AbsolutePath.EndsWith("/owners", StringComparison.Ordinal) && kind == "owners" => ApplicationReportFixture.ReadHttp.Page(owners),
            _ => ApplicationReportFixture.ReadHttp.Page([])
        };
        var report = kind == "roles" ? await f.Consent() : await f.Expiry();
        Assert.Equal(ReportReadState.Partial, report.Status); var row = Assert.Single(report.Sections[0].Rows);
        var field = kind == "roles" ? "appRoles" : kind; var maximum = kind == "roles" ? ApplicationReportRegistry.MaximumAppRoles : kind == "owners" ? ApplicationReportRegistry.MaximumOwners : ApplicationReportRegistry.MaximumCredentials;
        Assert.Equal(maximum, row[field]!.AsArray().Count); Assert.Contains("cap", ApplicationReportEvidenceSchema.Serialise(report), StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Root_section_limit_reads_one_extra_then_stops_without_empty_success()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/applications"
            ? Enumerable.Range(1, ApplicationReportRegistry.MaximumRows + 1).Select(i => ApplicationReportFixture.Object(ApplicationReportFixture.Id(i))) : []);
        var report = await f.Expiry(); Assert.Equal(ApplicationReportRegistry.MaximumRows, report.Sections[0].Rows.Count);
        Assert.Equal(ReportReadState.Partial, report.Sections[0].Status); Assert.Contains("row cap", report.Sections[0].Error);
        Assert.Equal(ApplicationReportRegistry.MaximumChildObjects, report.Sources[2].ObjectsAttempted);
        Assert.Equal(ReportReadState.Partial, report.Sources[2].Status); Assert.Contains("fan-out", report.Sources[2].Error);
    }
    [Fact]
    public async Task Child_fanout_is_global_and_never_exceeds_its_explicit_default_bound()
    {
        using var f = new ApplicationReportFixture();
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/servicePrincipals"
            ? Enumerable.Range(1, ApplicationReportRegistry.MaximumChildObjects + 1).Select(i => ApplicationReportFixture.Principal(ApplicationReportFixture.Id(i))) : []);
        var report = await f.Consent(); Assert.Equal(ApplicationReportRegistry.MaximumChildObjects, report.Sources[2].ObjectsAttempted);
        Assert.Equal(ApplicationReportRegistry.MaximumChildObjects, f.Http.Routes.Count(r => r.Contains("/appRoleAssignments", StringComparison.Ordinal)));
        Assert.Equal(ReportReadState.Partial, report.Sections[2].Status); Assert.Contains("fan-out", report.Sections[2].Error);
    }
    [Fact]
    public async Task Per_principal_assignment_limit_and_global_section_limit_are_both_explicit()
    {
        using var f = new ApplicationReportFixture();
        var principals = Enumerable.Range(1, 21).Select(i => ApplicationReportFixture.Principal(ApplicationReportFixture.Id(i))).ToList();
        principals.Add(ApplicationReportFixture.Principal(TestData.Mam, [ApplicationReportFixture.Role()]));
        var perChild = ApplicationReportRegistry.MaximumAssignmentsPerPrincipal + 1;
        f.Http.Response = (r, _) =>
        {
            if (r.RequestUri!.AbsolutePath == "/v1.0/servicePrincipals") return ApplicationReportFixture.ReadHttp.Page(principals);
            if (!r.RequestUri.AbsolutePath.EndsWith("/appRoleAssignments", StringComparison.Ordinal)) return ApplicationReportFixture.ReadHttp.Page([]);
            var queried = r.RequestUri.AbsolutePath.Split('/')[3];
            return ApplicationReportFixture.ReadHttp.Page(Enumerable.Range(0, perChild).Select(i =>
            { var row = ApplicationReportFixture.Assignment(queried + "-assignment-" + i); row["principalId"] = queried; return row; }));
        };
        var report = await f.Consent(); Assert.Equal(ApplicationReportRegistry.MaximumRows, report.Sections[2].Rows.Count);
        Assert.Equal(ReportReadState.Partial, report.Sections[2].Status); Assert.Contains("cap", report.Sections[2].Error);
        Assert.True(report.Sources[2].ObjectsAttempted < principals.Count);
        Assert.Equal(ApplicationReportRegistry.MaximumAssignmentsPerPrincipal, report.Sections[2].Rows.Count(r => r["queriedPrincipalId"]!.ToString() == ApplicationReportFixture.Id(1)));
    }
    [Fact]
    public async Task Aggregate_metadata_byte_budget_keeps_a_readable_partial_report_under_32MiB()
    {
        using var f = new ApplicationReportFixture(); var text = new string('X', 4096);
        var roles = new JsonArray(Enumerable.Range(1, ApplicationReportRegistry.MaximumAppRoles).Select(i => (JsonNode)ApplicationReportFixture.Role(ApplicationReportFixture.Id(i), text, text)).ToArray());
        f.Http.Response = (r, _) => ApplicationReportFixture.ReadHttp.Page(r.RequestUri!.AbsolutePath == "/v1.0/servicePrincipals"
            ? Enumerable.Range(1, 20).Select(i => ApplicationReportFixture.Principal(ApplicationReportFixture.Id(i), roles)) : []);
        var report = await f.Consent(); Assert.InRange(report.Sections[0].Rows.Count, 1, 19); Assert.Equal(ReportReadState.Partial, report.Status);
        Assert.Contains("byte", report.Sections[0].Error); Assert.True(System.Text.Encoding.UTF8.GetByteCount(ApplicationReportEvidenceSchema.Serialise(report)) <= ApplicationReportEvidenceSchema.MaximumBytes);
        Assert.Empty(report.Sections[2].Rows); Assert.NotEqual(ReportReadState.Collected, report.Sections[2].Status);
    }
}
