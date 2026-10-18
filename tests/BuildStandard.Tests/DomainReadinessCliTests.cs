using System.Text;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class DomainReadinessCliTests
{
    [Fact] public async Task Offline_command_exports_shared_worksheet_and_original_bytes_without_evidence_or_a_client()
    {
        using var root=new TempRoot();var input=Path.Combine(root.Root,"original.json");
        var bytes=Encoding.UTF8.GetBytes(ToolkitJson.Serialize(DomainReadinessSchemaTests.Valid()));File.WriteAllBytes(input,bytes);
        var before=Directory.GetFiles(root.Paths.DataDirectory,"*",SearchOption.AllDirectories);
        var output=Path.Combine(root.Root,"exports");
        var result=await RegisteredReportCliTests.Run("domain-report","--input",input,"--output",output,"--root",root.Root);
        Assert.True(result.Exit==0,result.Output+result.Error);Assert.Contains("Historical",result.Output);Assert.Contains("no DNS queries",result.Output);
        var folder=Assert.Single(Directory.GetDirectories(output));Assert.Equal(bytes,File.ReadAllBytes(Path.Combine(folder,"report.json")));
        Assert.Equal(DomainReadinessDocuments.Html(DomainReadinessDocument.Import(bytes)),File.ReadAllText(Path.Combine(folder,"readiness.html")));
        Assert.Equal(before,Directory.GetFiles(root.Paths.DataDirectory,"*",SearchOption.AllDirectories));Assert.Equal(bytes,File.ReadAllBytes(input));
    }
    [Theory][InlineData("--run")][InlineData("--input")][InlineData("--tenant")][InlineData("--server")]
    public async Task Query_identity_override_and_duplicate_options_are_refused_before_any_export(string option)
    {
        using var root=new TempRoot();var result=await RegisteredReportCliTests.Run("domain-report","--input","unused.json",option,"unused","--output",root.Paths.ReportsDirectory,"--root",root.Root);
        Assert.Equal(2,result.Exit);Assert.Contains(option,result.Error);Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
    [Fact] public async Task Invalid_supplied_envelope_never_becomes_an_export_or_success()
    {
        using var root=new TempRoot();var input=Path.Combine(root.Root,"bad.json");File.WriteAllText(input,"{}");
        var result=await RegisteredReportCliTests.Run("domain-report","--input",input,"--output",root.Paths.ReportsDirectory,"--root",root.Root);
        Assert.Equal(2,result.Exit);Assert.Empty(result.Output);Assert.Empty(Directory.GetFileSystemEntries(root.Paths.ReportsDirectory));
    }
}
