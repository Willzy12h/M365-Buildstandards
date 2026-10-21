using System.Text;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OperationalContextCliTests
{
    [Theory][InlineData("html")][InlineData("json")][InlineData("csv")][InlineData("xlsx")]
    public async Task Offline_command_exports_original_and_truthful_source_states_without_managed_insertion(string format)
    {
        using var root = new TempRoot(); var report = OperationalContextOutputTests.Report();
        var original = Encoding.UTF8.GetBytes("\uFEFF  " + OperationalContextSchema.Serialise(report) + "\n");
        var file = Path.Combine(root.Root, "synthetic operational.json"); File.WriteAllBytes(file, original);
        var before = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories);
        var result = await RegisteredReportCliTests.Run("operational-report-evidence", "--input", file, "--tenant", TestData.TenantA, "--format", format, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Output + result.Error); Assert.Empty(result.Error);
        Assert.Contains("no live reads", result.Output); Assert.Contains("Partial", result.Output);
        var output = Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory)); Assert.NotEmpty(File.ReadAllBytes(output));
        if (format == "json") Assert.Equal(original, File.ReadAllBytes(output));
        if (format == "html") { Assert.Contains("Offline supplied file", File.ReadAllText(output)); Assert.DoesNotContain("<script>", File.ReadAllText(output)); }
        Assert.Equal(before, Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories)); Assert.Equal(original, File.ReadAllBytes(file));
    }
    [Theory][InlineData("json")][InlineData("html")][InlineData("csv")][InlineData("xlsx")]
    public async Task Partner_offline_process_preserves_source_customer_separation_without_connecting(string format)
    {
        using var root=new TempRoot();var report=PartnerContextOutputTests.Report();var original=Encoding.UTF8.GetBytes("\uFEFF  "+OperationalContextSchema.Serialise(report)+"\n");
        var file=Path.Combine(root.Root,"supplied partner.json");File.WriteAllBytes(file,original);
        var wrong=await RegisteredReportCliTests.Run("operational-report-evidence","--input",file,"--tenant",TestData.TenantB,"--format",format,"--root",root.Root);
        Assert.Equal(2,wrong.Exit);Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
        var result=await RegisteredReportCliTests.Run("operational-report-evidence","--input",file,"--tenant",TestData.TenantA,"--format",format,"--root",root.Root);
        Assert.True(result.Exit==0,result.Output+result.Error);Assert.Contains("no live reads",result.Output);var output=Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory));
        if(format=="json")Assert.Equal(original,File.ReadAllBytes(output));
        if(format=="html"){var html=File.ReadAllText(output);Assert.Contains("Selected customer tenant ID",html);Assert.Contains(TestData.TenantB,html);Assert.Contains("not effective access",html);}
        Assert.Equal(original,File.ReadAllBytes(file));Assert.Empty(Directory.GetFiles(root.Paths.DataDirectory,"*",SearchOption.AllDirectories));
    }
    [Theory][InlineData("duplicate")][InlineData("tenant")][InlineData("format")][InlineData("unknown")]
    public async Task Wrong_offline_identity_and_options_refuse_before_export(string fault)
    {
        using var root = new TempRoot(); var report = OperationalContextOutputTests.Report();
        var file = Path.Combine(root.Root, "source.json"); File.WriteAllText(file, OperationalContextSchema.Serialise(report));
        var arguments = new List<string> { "operational-report-evidence", "--input", file, "--tenant", fault == "tenant" ? TestData.TenantB : TestData.TenantA, "--root", root.Root };
        if (fault == "duplicate") arguments.AddRange(["--tenant", TestData.TenantA]);
        if (fault == "format") arguments.AddRange(["--format", "markdown"]);
        if (fault == "unknown") arguments.AddRange(["--url", "https://synthetic.invalid"]);
        var result = await RegisteredReportCliTests.Run(arguments.ToArray()); Assert.Equal(2, result.Exit); Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
