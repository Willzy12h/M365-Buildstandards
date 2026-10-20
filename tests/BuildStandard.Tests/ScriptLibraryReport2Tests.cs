using System.Text.Json.Nodes;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ScriptLibraryReport2Tests
{
    internal static IReadOnlyDictionary<string, byte[]> Library(Action<JsonObject>? change = null) =>
        ExchangeReportRunnerTests.Library("param([string]$MailboxGuids,[bool]$IncludeHidden)\n$rows = @(Use-BditRead -Command 'Get-InboxRule' -Parameters @{ Mailbox = $MailboxGuids; IncludeHidden = $IncludeHidden })\n@{ Sections = @() }\n", manifest: manifest =>
        {
            var report = ExchangeReportRegistry.Find("exo-inbox-rules");
            manifest["execution"]!["reportId"] = report.Id;
            manifest["execution"]!["reportSchemaVersion"] = 2;
            manifest["limits"]!["maximumRows"] = 5000;
            manifest["limits"]!["timeoutSeconds"] = 300;
            manifest["parameters"] = new JsonArray(
                new JsonObject { ["name"] = "MailboxGuids", ["label"] = "Mailbox GUIDs", ["type"] = "string",
                    ["help"] = "Choose1–50 exact mailbox GUIDs separated by commas.", ["required"] = true, ["maxLength"] = 1849 },
                new JsonObject { ["name"] = "IncludeHidden", ["label"] = "Include hidden rules", ["type"] = "boolean",
                    ["help"] = "Hidden rules are omitted unless explicitly selected.", ["default"] = "false" });
            manifest["outputSchemaVersion"] = 2;
            manifest["outputSchema"] = new JsonObject
            {
                ["columns"] = new JsonArray(),
                ["sections"] = new JsonArray(report.Sections.Select(section => (JsonNode)new JsonObject
                {
                    ["id"] = section.Id,
                    ["columns"] = new JsonArray(ExchangeReportEvidenceSchema.Columns(section.RowType).Select(c => (JsonNode?)c).ToArray())
                }).ToArray())
            };
            change?.Invoke(manifest);
        });

    [Fact]
    public void Exact_report2_multi_section_descriptor_is_admitted_without_a_flat_union_or_child_envelope_bump()
    {
        var item = Assert.Single(ScriptCatalogue.Load(Library()).Entries);
        Assert.Equal(2, item.Manifest.OutputSchemaVersion);
        Assert.Empty(item.Manifest.OutputSchema.Columns);
        Assert.Equal(1, item.Manifest.Execution!.OutputSchemaVersion);
        Assert.Equal(2, ScriptCatalogue.ReportFor(item.Manifest).ReportSchemaVersion);
    }

    [Theory]
    [InlineData("missing")][InlineData("null")][InlineData("empty")][InlineData("extra")]
    [InlineData("reordered")][InlineData("duplicate")][InlineData("unknown-column")]
    [InlineData("reordered-column")][InlineData("union")][InlineData("description-version")]
    [InlineData("child-version")][InlineData("report-version")][InlineData("cap")]
    [InlineData("unknown-section-field")][InlineData("missing-columns")][InlineData("string-description-version")]
    [InlineData("optional-targets")][InlineData("short-target-field")]
    public void Resealed_malformed_descriptors_and_incompatible_versions_are_refused(string fault)
    {
        var files = Library(manifest =>
        {
            var schema = manifest["outputSchema"]!.AsObject();
            var sections = schema["sections"]!.AsArray();
            switch (fault)
            {
                case "missing": schema.Remove("sections"); break;
                case "null": schema["sections"] = null; break;
                case "empty": sections.Clear(); break;
                case "extra": sections.Add(sections[0]!.DeepClone()); break;
                case "reordered": var first=sections[0]!.DeepClone();sections[0]=sections[1]!.DeepClone();sections[1]=first; break;
                case "duplicate": sections[1]=sections[0]!.DeepClone(); break;
                case "unknown-column": sections[0]!["columns"]!.AsArray().Add("arbitrary"); break;
                case "reordered-column": var columns=sections[0]!["columns"]!.AsArray();var value=columns[0]!.GetValue<string>();columns[0]=columns[1]!.GetValue<string>();columns[1]=value;break;
                case "union": schema["columns"] = sections[0]!["columns"]!.DeepClone(); break;
                case "description-version": manifest["outputSchemaVersion"] = 1; break;
                case "child-version": manifest["execution"]!["outputSchemaVersion"] = 2; break;
                case "report-version": manifest["execution"]!["reportSchemaVersion"] = 1; break;
                case "cap": manifest["limits"]!["maximumRows"] = 5001; break;
                case "unknown-section-field": sections[0]!["arbitrary"] = true; break;
                case "missing-columns": schema.Remove("columns"); break;
                case "string-description-version": manifest["outputSchemaVersion"] = "2"; break;
                case "optional-targets": manifest["parameters"]![0]!["required"] = false; break;
                case "short-target-field": manifest["parameters"]![0]!["maxLength"] = 1000; break;
            }
        });
        Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Report1_explicit_sections_are_refused_even_when_null(bool nullValue)
    {
        var files = ExchangeReportRunnerTests.Library(manifest: m =>
            m["outputSchema"]!["sections"] = nullValue ? null : new JsonArray());
        Assert.Throws<ConfigurationException>(() => ScriptCatalogue.Load(files));
    }

    [Fact]
    public void All_fifty_target_ids_bind_and_invalid_targets_cannot_reach_run_preparation()
    {
        var item = Assert.Single(ScriptCatalogue.Load(Library()).Entries);
        var ids = string.Join(',', Enumerable.Range(1, 50).Select(n => $"12345678-1234-1234-1234-{n:000000000000}"));
        var result = ScriptInputs.Bind(item.Manifest, new Dictionary<string,string?> { ["MailboxGuids"] = ids }, DateTimeOffset.UtcNow);
        Assert.True(result.IsValid, string.Join(" ", result.Problems));
        Assert.Equal(ids, Assert.IsType<ScriptText>(Assert.Single(result.Arguments).Value).Value);
        var invalid = ScriptInputs.Bind(item.Manifest, new Dictionary<string,string?> { ["MailboxGuids"] = "friendly mailbox" }, DateTimeOffset.UtcNow);
        Assert.False(invalid.IsValid);
    }
    [Fact]
    public async Task A_metadata_only_report2_fixture_cannot_start_an_unshipped_adapter_or_even_look_up_a_host()
    {
        using var root = new TempRoot();
        var item = Assert.Single(ScriptCatalogue.Load(Library()).Entries);
        var calls = 0;
        var staging = Path.Combine(root.Root, "staging");
        var host = new ReadRunnerHost(_ => { calls++; return null; }, null, null, staging);
        var runner = new ExchangeReportRunner(host, new SystemClock());
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => runner.RunAsync(item,
            new(TestData.TenantA, TestData.Operator, "operator@synthetic.example"),
            new Dictionary<string,string?> { ["MailboxGuids"] = TestData.Office }, "7", null, CancellationToken.None));
        Assert.Contains("package-owned", error.Message);
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(staging));
    }
}
