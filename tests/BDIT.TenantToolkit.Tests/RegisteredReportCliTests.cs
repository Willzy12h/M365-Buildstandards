using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class RegisteredReportCliTests
{
    [Fact]
    public void Packaged_process_fixture_is_strict_synthetic_partial_evidence()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "build", "test-fixtures", "registered-report.json"));
        var report = ReportEvidenceSchema.Read(File.ReadAllText(file), TestData.TenantA);
        Assert.Equal(ReportReadState.Partial, report.Status); Assert.Empty(Assert.Single(report.Sections).Rows);
        Assert.Contains("Synthetic", Assert.Single(report.Limitations));
    }

    private static ReportEvidence Report(string state = ReportReadState.Collected, bool rows = false)
    {
        var definition = GraphReportRegistry.Find("intune-devices");
        var report = new ReportEvidence
        {
            Id = Guid.NewGuid().ToString(), ReportId = definition.Id, TenantId = TestData.TenantA,
            AccountObjectId = TestData.Operator, StartedAt = "2026-10-08T10:00:00Z", EndedAt = "2026-10-08T10:01:00Z",
            ToolkitVersion = "synthetic", ModuleVersion = GraphReportRegistry.AdapterVersion,
            Sources = definition.Routes.Select(r => new ReportSource { Api = "v1.0", RegisteredRoute = r.Path, Reference = definition.Reference }).ToList(),
            Status = state, Sections = [new() { Id = "devices", Status = state, Error = state == ReportReadState.Collected ? null : "Synthetic incomplete read" }]
        };
        if (rows) report.Sections[0].Rows.Add(ReportEvidenceSchema.Row(new DeviceReportRow
        {
            Id = TestData.Mam, Name = "=HYPERLINK(<script>synthetic</script>)", ReadStatus = ReportReadState.Collected
        }));
        ReportEvidenceSchema.Seal(report);
        return report;
    }

    private static async Task<(int Exit, string Output, string Error)> Run(params string[] args)
    {
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BDIT.TenantToolkit.Cli", "bin", config, "net10.0", "bdit.dll"));
        Assert.True(File.Exists(dll), "Build the actual CLI before running its process tests.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(dll); foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    [Theory]
    [InlineData("html")]
    [InlineData("csv")]
    [InlineData("json")]
    [InlineData("xlsx")]
    public async Task Actual_offline_process_reuses_registered_exports_without_changing_source_or_evidence(string format)
    {
        using var root = new TempRoot(); var report = Report(ReportReadState.Partial, rows: true);
        var input = Path.Combine(root.Root, "synthetic report evidence.json");
        File.WriteAllText(input, ReportEvidenceSchema.Serialize(report)); var bytes = File.ReadAllBytes(input);
        var evidence = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories);
        var result = await Run("report-evidence", "--input", input, "--tenant", TestData.TenantA, "--format", format, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Error + result.Output); Assert.Empty(result.Error);
        Assert.Contains("Partial", result.Output); Assert.Contains("no live reads", result.Output);
        var file = Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory));
        Assert.Contains(file, result.Output); Assert.NotEmpty(File.ReadAllBytes(file));
        if (format == "json") Assert.Equal(report.Id, ReportEvidenceSchema.Read(File.ReadAllText(file), TestData.TenantA).Id);
        if (format == "html") { var html = File.ReadAllText(file); Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>", html); Assert.Contains("Partial", html); }
        if (format == "csv")
        {
            using var zip = ZipFile.OpenRead(file); using var reader = new StreamReader(Assert.Single(zip.Entries, e => e.Name == "02_devices.csv").Open());
            Assert.Contains("'=HYPERLINK", reader.ReadToEnd());
        }
        if (format == "xlsx") { using var zip = ZipFile.OpenRead(file); Assert.Contains(zip.Entries, e => e.FullName == "xl/workbook.xml"); }
        Assert.Equal(bytes, File.ReadAllBytes(input)); Assert.Equal(evidence, Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(ReportReadState.Collected)]
    [InlineData(ReportReadState.Partial)]
    [InlineData(ReportReadState.Failed)]
    [InlineData(ReportReadState.NotAttempted)]
    [InlineData(ReportReadState.Cancelled)]
    public async Task Export_success_does_not_change_the_recorded_read_state_or_claim_empty_success(string state)
    {
        using var root = new TempRoot(); var report = Report(state);
        var input = Path.Combine(root.Root, "synthetic-empty.json"); File.WriteAllText(input, ReportEvidenceSchema.Serialize(report));
        var result = await Run("report-evidence", "--input", input, "--tenant", TestData.TenantA, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Error + result.Output); Assert.Contains(state, result.Output);
        var html = File.ReadAllText(Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory)));
        if (state == ReportReadState.Collected) Assert.Contains("Checked successfully; no objects returned.", html);
        else { Assert.Contains("not an empty successful check", html); Assert.DoesNotContain("Checked successfully; no objects returned.", html); }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("digest")]
    [InlineData("kind")]
    [InlineData("field")]
    [InlineData("duplicate")]
    [InlineData("utf8")]
    [InlineData("oversize")]
    public async Task Wrong_identity_tamper_and_untrusted_input_refuse_without_export(string scenario)
    {
        using var root = new TempRoot(); var input = Path.Combine(root.Root, "synthetic.json"); var report = Report();
        var json = ReportEvidenceSchema.Serialize(report);
        if (scenario == "digest") json = json.Replace("synthetic", "changed", StringComparison.Ordinal);
        if (scenario == "kind") json = json.Replace("reportEvidence", "tenantSnapshot", StringComparison.Ordinal);
        if (scenario == "field") { var node = JsonNode.Parse(json)!.AsObject(); node["arbitraryScript"] = "refuse"; json = node.ToJsonString(); }
        if (scenario == "duplicate") json = "{\"kind\":\"reportEvidence\"," + json[1..];
        File.WriteAllText(input, json);
        if (scenario == "utf8") File.WriteAllBytes(input, [0xff, 0xfe, 0xff]);
        if (scenario == "oversize") { using var file = File.OpenWrite(input); file.SetLength(ReportEvidenceSchema.MaximumBytes + 1L); }
        var bytes = File.ReadAllBytes(input);
        var result = await Run("report-evidence", "--input", input, "--tenant", scenario == "tenant" ? TestData.TenantB : TestData.TenantA, "--root", root.Root);
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.Contains("Refused:", result.Error);
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory)); Assert.Equal(bytes, File.ReadAllBytes(input));
        Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("format", "markdown")]
    [InlineData("format", "123")]
    [InlineData("release", "2026.09.30")]
    [InlineData("run", "true")]
    [InlineData("tenant", "invalid")]
    public async Task Unsupported_options_and_formats_refuse_before_export(string option, string value)
    {
        using var root = new TempRoot(); var input = Path.Combine(root.Root, "synthetic.json"); File.WriteAllText(input, ReportEvidenceSchema.Serialize(Report()));
        var args = new List<string> { "report-evidence", "--input", input, "--root", root.Root };
        if (option != "tenant") args.AddRange(["--tenant", TestData.TenantA]);
        args.AddRange(["--" + option, value]); var result = await Run(args.ToArray());
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.Contains("Refused:", result.Error);
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
