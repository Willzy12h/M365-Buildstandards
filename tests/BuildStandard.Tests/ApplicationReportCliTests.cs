using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ApplicationReportCliTests
{
    [Theory]
    [InlineData("html")][InlineData("json")][InlineData("csv")][InlineData("xlsx")]
    public async Task Explicit_historical_cli_preserves_original_bytes_and_warns_in_every_export(string format)
    {
        using var fixture = new ApplicationReportFixture(); var report = await Report(fixture, "expiry");
        report.ReportSchemaVersion = 1; report.ModuleVersion = ApplicationReportRegistry.AdapterVersion;
        foreach (var source in report.Sources) source.RetainedOwnerOutcomes = null;
        report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
        var input = Path.Combine(fixture.Root.Root, "original expiry.json");
        File.WriteAllText(input, "\n" + ApplicationReportEvidenceSchema.Serialise(report) + "\n", new UTF8Encoding(true));
        var original = File.ReadAllBytes(input);
        var refused = await RegisteredReportCliTests.Run("report-evidence", "--input", input, "--tenant", TestData.TenantA, "--root", fixture.Root.Root);
        Assert.Equal(2, refused.Exit); Assert.Contains(ApplicationReportEvidenceSchema.HistoricalOwnerNotice, refused.Error);
        Assert.Empty(Directory.GetFiles(fixture.Root.Paths.ReportsDirectory));
        var result = await RegisteredReportCliTests.Run("report-evidence", "--input", input, "--tenant", TestData.TenantA, "--root", fixture.Root.Root, "--format", format, "--historical");
        Assert.True(result.Exit == 0, result.Error); Assert.Contains(ApplicationReportEvidenceSchema.HistoricalOwnerNotice, result.Output);
        var export = Assert.Single(Directory.GetFiles(fixture.Root.Paths.ReportsDirectory));
        Assert.Contains(ApplicationReportEvidenceSchema.HistoricalOwnerNotice, Text(export));
        if (format == "json")
        {
            using var zip = ZipFile.OpenRead(export); using var payload = zip.GetEntry("report.json")!.Open();
            using var bytes = new MemoryStream(); payload.CopyTo(bytes); Assert.Equal(original, bytes.ToArray());
        }
        Assert.Equal(original, File.ReadAllBytes(input));
    }
    [Theory]
    [InlineData("current")][InlineData("consent")][InlineData("value")]
    public async Task Historical_flag_cannot_downgrade_current_or_consent_records_or_take_a_value(string fault)
    {
        using var fixture = new ApplicationReportFixture(); var report = await Report(fixture, fault == "consent" ? "consent" : "expiry");
        var input = Path.Combine(fixture.Root.Root, "record.json"); File.WriteAllText(input, ApplicationReportEvidenceSchema.Serialise(report));
        var args = new List<string> { "report-evidence", "--input", input, "--tenant", TestData.TenantA, "--root", fixture.Root.Root, "--historical" };
        if (fault == "value") args.Add("false");
        var result = await RegisteredReportCliTests.Run(args.ToArray());
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.Empty(Directory.GetFiles(fixture.Root.Paths.ReportsDirectory));
    }
    private static Task<ApplicationReportEvidence> Report(ApplicationReportFixture fixture, string kind)
    {
        if (kind == "consent")
        {
            fixture.ConsentData(); fixture.Session.Scopes.Remove("Directory.Read.All");
            return fixture.Consent();
        }
        fixture.Http.Response = (request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/v1.0/applications" => ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object(name: "=HYPERLINK(<script>synthetic</script>)", passwords: [ApplicationReportFixture.Credential("2026-09-20T00:00:00Z")])]),
            "/v1.0/servicePrincipals" => ApplicationReportFixture.ReadHttp.Page([ApplicationReportFixture.Object(TestData.Mam)]),
            "/v1.0/applications/" + TestData.Operator + "/owners" => ApplicationReportFixture.ReadHttp.Page([new() { ["id"] = TestData.Emergency }]),
            _ => ApplicationReportFixture.ReadHttp.Page([])
        };
        return fixture.Expiry(45);
    }
    private static string Text(string file) => Path.GetExtension(file) is ".zip" or ".xlsx" ? ZipText(file) : File.ReadAllText(file);
    private static string ZipText(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        return string.Join("\n", zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
    }
    [Theory]
    [InlineData("expiry", "html")]
    [InlineData("expiry", "json")]
    [InlineData("expiry", "csv")]
    [InlineData("expiry", "xlsx")]
    [InlineData("consent", "html")]
    [InlineData("consent", "json")]
    [InlineData("consent", "csv")]
    [InlineData("consent", "xlsx")]
    public async Task Actual_offline_process_exports_schema2_full_nested_metadata_and_original_provenance(string kind, string format)
    {
        using var fixture = new ApplicationReportFixture(); var report = await Report(fixture, kind);
        Assert.Equal(ReportReadState.Partial, report.Status);
        var input = Path.Combine(fixture.Root.Root, "supplied application report.json");
        var json = ApplicationReportEvidenceSchema.Serialise(report); File.WriteAllText(input, json); var bytes = File.ReadAllBytes(input);
        var data = Directory.GetFiles(fixture.Root.Paths.DataDirectory, "*", SearchOption.AllDirectories);
        var calls = fixture.Http.Routes.Count; var tokens = fixture.Tokens.Calls;
        var result = await RegisteredReportCliTests.Run("report-evidence", "--input", input, "--tenant", TestData.TenantA, "--format", format, "--root", fixture.Root.Root);
        Assert.True(result.Exit == 0, result.Error + result.Output); Assert.Empty(result.Error);
        Assert.Contains("Offline export · read status Partial", result.Output); Assert.Contains(report.StartedAt + " to " + report.EndedAt, result.Output);
        Assert.Contains("no live reads or writes. Export success does not prove collection success.", result.Output);
        var exported = Assert.Single(Directory.GetFiles(fixture.Root.Paths.ReportsDirectory)); Assert.Contains(exported, result.Output);
        var text = Text(exported); Assert.Contains(TestData.Operator, text); Assert.Contains(TestData.Mam, text);
        if (kind == "expiry") { Assert.Contains(TestData.Office, text); Assert.Contains(TestData.Emergency, text); Assert.Contains("ExpiringWithinHorizon", text); }
        else { Assert.Contains("nDJ_opaque-Base64url_Assignment-1", text); Assert.Contains("Read synthetic records", text); Assert.Contains("NotAttempted", text); }
        if (format == "json")
        {
            Assert.Equal(json, File.ReadAllText(exported).TrimStart('\uFEFF'));
            var loaded = ReportEnvelopeDispatcher.ReadFile(exported, TestData.TenantA).Schema2!;
            Assert.Equal(report.IntegrityDigest, loaded.IntegrityDigest); Assert.Equal(report.AccountObjectId, loaded.AccountObjectId); Assert.Equal(report.SourceMode, loaded.SourceMode);
            if (kind == "expiry") Assert.Equal(45, Assert.IsType<ApplicationExpiryEvidence>(loaded).Parameters.HorizonDays);
        }
        else Assert.Contains(RegisteredReportDocuments.SuppliedFileNotice, text);
        if (format == "html") { Assert.DoesNotContain("<script>", text); if (kind == "expiry") Assert.Contains("&lt;script&gt;", text); }
        if (format == "csv" && kind == "expiry") Assert.Contains("'=HYPERLINK", text);
        if (format == "xlsx") Assert.DoesNotContain("<f>", text);
        Assert.Equal(bytes, File.ReadAllBytes(input)); Assert.Equal(data, Directory.GetFiles(fixture.Root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(calls, fixture.Http.Routes.Count); Assert.Equal(tokens, fixture.Tokens.Calls);
    }
    [Theory]
    [InlineData("expiry", "tenant")]
    [InlineData("expiry", "digest")]
    [InlineData("expiry", "schema1")]
    [InlineData("expiry", "schema2-legacy-id")]
    [InlineData("expiry", "source")]
    [InlineData("expiry", "secret-row")]
    [InlineData("consent", "tenant")]
    [InlineData("consent", "digest")]
    [InlineData("consent", "schema1")]
    [InlineData("consent", "schema2-legacy-id")]
    [InlineData("consent", "source")]
    [InlineData("consent", "secret-row")]
    public async Task Actual_process_refuses_wrong_tenant_tamper_mixed_schema_and_resealed_source_claims_without_output(string kind, string fault)
    {
        using var fixture = new ApplicationReportFixture(); var report = await Report(fixture, kind);
        if (fault == "source")
        {
            var index = kind == "expiry" ? 1 : 2;
            report.Sections[index].Rows.Clear(); report.Sources[index].Rows = 0;
            report.Sections[index].Status = ReportReadState.Collected; report.Sections[index].Error = null;
            report.Sources[index].Status = ReportReadState.Failed; report.Sources[index].Error = "Synthetic unavailable metadata source.";
            if (kind == "expiry")
            { report.Sources[3].ObjectsAttempted = 0; report.Sources[3].Rows = 0; report.Sources[3].Status = ReportReadState.NotAttempted; report.Sources[3].Error = "No service principal inventory for owner reads."; }
            report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report);
            Assert.True(EvidenceIntegrity.Verify<object>(report, report.IntegrityDigest));
        }
        if (fault == "secret-row")
        { report.Sections[0].Rows[0]["secretText"] = "SYNTHETIC_FIELD_MUST_REFUSE"; report.IntegrityDigest = EvidenceIntegrity.Compute<object>(report); }
        var json = ApplicationReportEvidenceSchema.Serialise(report); var node = JsonNode.Parse(json)!.AsObject();
        if (fault == "digest") node["limitations"]![0] = "Changed without resealing";
        if (fault == "schema1") node["schemaVersion"] = 1;
        if (fault == "schema2-legacy-id") node["reportId"] = "intune-devices";
        var input = Path.Combine(fixture.Root.Root, "untrusted.json"); File.WriteAllText(input, node.ToJsonString()); var bytes = File.ReadAllBytes(input);
        var result = await RegisteredReportCliTests.Run("report-evidence", "--input", input, "--tenant", fault == "tenant" ? TestData.TenantB : TestData.TenantA, "--root", fixture.Root.Root);
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.Contains("Refused:", result.Error);
        if (fault == "source") Assert.Contains("Section read state contradicts its registered source outcome.", result.Error);
        if (fault == "secret-row") Assert.Contains("Invalid registered metadata row fields.", result.Error);
        Assert.Empty(Directory.GetFiles(fixture.Root.Paths.ReportsDirectory)); Assert.Empty(Directory.GetFiles(fixture.Root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
        Assert.Equal(bytes, File.ReadAllBytes(input));
    }
    [Theory]
    [InlineData("schema1")]
    [InlineData("expiry")]
    [InlineData("consent")]
    public async Task Actual_process_refuses_a_linked_input_location_before_export(string kind)
    {
        using var fixture = new ApplicationReportFixture(); var folder = Path.Combine(fixture.Root.Root, "real-input"); Directory.CreateDirectory(folder);
        var input = Path.Combine(folder, "report.json");
        var json = kind == "schema1" ? ReportEvidenceSchema.Serialize(RegisteredReportCliTests.Report()) : ApplicationReportEvidenceSchema.Serialise(await Report(fixture, kind));
        File.WriteAllText(input, json); var bytes = File.ReadAllBytes(input); var linked = Path.Combine(fixture.Root.Root, "linked-input");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "/c", "mklink", "/J", linked, folder }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await output + await error);
        }
        else Directory.CreateSymbolicLink(linked, folder);
        var result = await RegisteredReportCliTests.Run("report-evidence", "--input", Path.Combine(linked, "report.json"), "--tenant", TestData.TenantA, "--root", fixture.Root.Root);
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.Contains("Report evidence parent links are refused.", result.Error);
        Assert.Empty(Directory.GetFiles(fixture.Root.Paths.ReportsDirectory)); Assert.Equal(bytes, File.ReadAllBytes(input));
    }
}
