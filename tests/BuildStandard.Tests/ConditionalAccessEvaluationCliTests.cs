using BuildStandard.Core;
using BuildStandard.Engine.Reports;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ConditionalAccessEvaluationCliTests
{
    [Theory][InlineData(ExportFormat.Html)][InlineData(ExportFormat.Csv)][InlineData(ExportFormat.Json)][InlineData(ExportFormat.Xlsx)]
    public void Export_uses_fresh_paths_preserves_sources_and_never_overwrites_a_previous_file(ExportFormat format)
    {
        using var root=new TempRoot();var capture=ConditionalAccessEvaluationExportTests.Capture();var header=capture.MetadataBytes;var raw=capture.OriginalResponseBytes;
        var exporter=new ReportExporter(root.Paths,"Synthetic");var first=exporter.ExportConditionalAccessEvaluation(capture,format);var original=File.ReadAllBytes(first);
        var second=exporter.ExportConditionalAccessEvaluation(capture,format);Assert.NotEqual(first,second);Assert.Equal(original,File.ReadAllBytes(first));
        Assert.Equal(header,capture.MetadataBytes);Assert.Equal(raw,capture.OriginalResponseBytes);Assert.Empty(Directory.EnumerateFiles(root.Paths.TenantsDirectory,"*",SearchOption.AllDirectories));
        if(format==ExportFormat.Json){var imported=ConditionalAccessEvaluationBundle.ReadFile(first,TestData.TenantA);Assert.Equal(raw,imported.OriginalResponseBytes);}
    }
    [Theory][InlineData("html")][InlineData("csv")][InlineData("json")][InlineData("xlsx")]
    public async Task Actual_CLI_process_only_exports_supplied_historical_evidence(string format)
    {
        using var root=new TempRoot();var input=Path.Combine(root.Root,"synthetic evaluation.zip");var original=ConditionalAccessEvaluationBundle.Write(ConditionalAccessEvaluationExportTests.Capture());File.WriteAllBytes(input,original);
        var result=await RegisteredReportCliTests.Run("ca-evaluation","--input",input,"--tenant",TestData.TenantA,"--format",format,"--root",root.Root);
        Assert.True(result.Exit==0,result.Error);Assert.Contains("historical",result.Output,StringComparison.OrdinalIgnoreCase);Assert.Contains("no live",result.Output,StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.EnumerateFiles(root.Paths.ReportsDirectory));Assert.Equal(original,File.ReadAllBytes(input));
        if(format=="html"){var html=File.ReadAllText(Directory.EnumerateFiles(root.Paths.ReportsDirectory).Single());Assert.Contains("unverified",html);Assert.Contains("not proof",html,StringComparison.OrdinalIgnoreCase);}
    }
    [Theory][InlineData("tenant")][InlineData("bad-format")][InlineData("unknown-option")][InlineData("duplicate")][InlineData("missing-tenant")]
    public async Task CLI_refusals_produce_no_export_or_tenant_change(string defect)
    {
        using var root=new TempRoot();var input=Path.Combine(root.Root,"synthetic.zip");File.WriteAllBytes(input,ConditionalAccessEvaluationBundle.Write(ConditionalAccessEvaluationExportTests.Capture()));
        var args=new List<string>{"ca-evaluation","--input",input,"--root",root.Root};if(defect!="missing-tenant")args.AddRange(["--tenant",defect=="tenant"?TestData.TenantB:TestData.TenantA]);
        if(defect=="bad-format")args.AddRange(["--format","markdown"]);if(defect=="unknown-option")args.AddRange(["--run","yes"]);if(defect=="duplicate")args.AddRange(["--input",input]);
        var result=await RegisteredReportCliTests.Run(args.ToArray());Assert.True(result.Exit==2,result.Error+result.Output);Assert.Empty(Directory.EnumerateFiles(root.Paths.ReportsDirectory));
    }
    [Fact] public void Unsupported_formats_are_refused_before_any_output_file()
    {using var root=new TempRoot();Assert.Throws<ConfigurationException>(()=>new ReportExporter(root.Paths,"").ExportConditionalAccessEvaluation(ConditionalAccessEvaluationExportTests.Capture(),ExportFormat.Markdown));Assert.Empty(Directory.EnumerateFiles(root.Paths.ReportsDirectory));}
}
