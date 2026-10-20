using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ExchangeReportCliTests
{
    [Theory]
    [InlineData("html")]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Actual_offline_cli_exports_two_sections_without_authentication_or_rewriting_originals(string format)
    {
        using var root = new TempRoot();
        var report = InboxRuleEvidenceV2Tests.Report();
        report.Sections[1].Rows[0]["name"] = "=HYPERLINK(<script>synthetic</script>)";
        InboxRuleEvidenceV2Tests.Read(report);
        var input = Path.Combine(root.Root, "synthetic original inbox rules.json");
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("\n" + ExchangeReportEvidenceSchema.Serialize(report) + "\n")).ToArray();
        File.WriteAllBytes(input, bytes);
        var before = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories);
        var result = await RegisteredReportCliTests.Run("exchange-report-evidence", "--input", input,
            "--tenant", TestData.TenantA, "--format", format, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Output + result.Error);
        Assert.Empty(result.Error);
        Assert.Contains("no live reads or writes", result.Output);
        Assert.Contains("source, account and live-mode claims have not been independently verified", result.Output);
        var output = Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory));
        Assert.Equal(bytes, File.ReadAllBytes(input));
        Assert.Equal(before, Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
        if (format == "html")
        {
            var html = File.ReadAllText(output);
            Assert.Contains("target-reads", html);
            Assert.Contains("rules", html);
            Assert.Contains("False (reported)", html);
            Assert.Contains("Offline supplied file", html);
            Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(bytes)), html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.DoesNotContain("<script>", html);
        }
        else
        {
            using var zip = ZipFile.OpenRead(output);
            if (format == "json")
            {
                using var original = zip.GetEntry("report.json")!.Open();
                using var buffer = new MemoryStream();
                original.CopyTo(buffer);
                Assert.Equal(bytes, buffer.ToArray());
                Assert.NotNull(zip.GetEntry("README.txt"));
            }
            else
            {
                var text = string.Join("\n", zip.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e =>
                { using var reader = new StreamReader(e.Open()); return reader.ReadToEnd(); }));
                Assert.Contains("Offline supplied file", text);
                Assert.Contains("False (reported)", text);
                Assert.Contains(format == "csv" ? "'=HYPERLINK" : "inlineStr", text);
            }
        }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("graph-kind")]
    [InlineData("duplicate-option")]
    [InlineData("auth-option")]
    [InlineData("historical-option")]
    [InlineData("unsupported-format")]
    [InlineData("numeric-format")]
    public async Task Wrong_kind_identity_or_options_refuse_without_producing_an_export(string fault)
    {
        using var root = new TempRoot();
        var report = InboxRuleEvidenceV2Tests.Read(InboxRuleEvidenceV2Tests.Report());
        var input = Path.Combine(root.Root, "synthetic input.json");
        File.WriteAllText(input, fault == "graph-kind" ? BuildStandard.Engine.Reports.ReportEvidenceSchema.Serialize(RegisteredReportCliTests.Report())
            : ExchangeReportEvidenceSchema.Serialize(report));
        var args = new List<string> { "exchange-report-evidence", "--input", input, "--tenant",
            fault == "tenant" ? TestData.TenantB : TestData.TenantA, "--root", root.Root };
        if (fault == "duplicate-option") args.AddRange(["--input", input]);
        if (fault == "auth-option") args.Add("--connect");
        if (fault == "historical-option") args.Add("--historical");
        if (fault == "unsupported-format") args.AddRange(["--format", "markdown"]);
        if (fault == "numeric-format") args.AddRange(["--format", "2"]);
        var result = await RegisteredReportCliTests.Run(args.ToArray());
        Assert.Equal(2, result.Exit);
        Assert.Empty(result.Output);
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
        Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
    }
    [Fact]
    public async Task Existing_report1_mailbox_evidence_remains_offline_readable_without_version_conversion()
    {
        using var root = new TempRoot();
        var report = InboxRuleEvidenceV2Tests.Report();
        var definition = BuildStandard.Core.Reporting.ExchangeReportRegistry.Find("exo-mailbox-inventory");
        report.ReportId = definition.Id;
        report.ReportSchemaVersion = definition.ReportSchemaVersion;
        report.AdapterVersion = definition.AdapterVersion;
        report.SourceCommands = [.. definition.SourceCommands];
        report.Parameters = [new() { Name = "IncludeArchive", Type = "Boolean", Value = "false" }];
        report.Sections = [new() { Id = "mailboxes", Status = "Collected", Rows = [] }];
        ExchangeReportEvidenceSchema.Seal(report);
        var input = Path.Combine(root.Root, "synthetic-report1.json");
        File.WriteAllText(input, ExchangeReportEvidenceSchema.Serialize(report));
        var result = await RegisteredReportCliTests.Run("exchange-report-evidence", "--input", input,
            "--tenant", TestData.TenantA, "--format", "json", "--root", root.Root);
        Assert.True(result.Exit == 0, result.Output + result.Error);
        using var zip = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory)));
        using var reader = new StreamReader(zip.GetEntry("report.json")!.Open());
        var exported = ExchangeReportEvidenceSchema.Read(reader.ReadToEnd(), TestData.TenantA);
        Assert.Equal(1, exported.ReportSchemaVersion);
        Assert.Equal(report.IntegrityDigest, exported.IntegrityDigest);
    }
}
