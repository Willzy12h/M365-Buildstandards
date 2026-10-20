using System.Text;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json.Nodes;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using Xunit;
namespace BuildStandard.Tests;
public sealed class UsageReviewCliTests
{
    [Theory][InlineData("html")][InlineData("json")][InlineData("csv")][InlineData("xlsx")]
    public async Task Offline_process_exports_original_pack_without_tenant_calls_or_evidence_changes(string format)
    {
        using var root=new TempRoot();var report=UsageReviewSchemaTests.Report();
        var columns=UsageReportRegistry.Find("email-activity").Columns;
        string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var csv=Encoding.UTF8.GetBytes(string.Join(',',columns)+"\r\n"+string.Join(',',columns.Select(name=>Quote(name=="Report Refresh Date"?"2026-10-20":name=="Display Name"?"<script>synthetic</script>":name=="User Principal Name"?"=SYNTHETIC()":"")))+"\r\n");
        report.Sources[0].OriginalCsvBase64=Convert.ToBase64String(csv);report.Sources[0].CsvSha256=Convert.ToHexStringLower(SHA256.HashData(csv));UsageReviewSchema.Seal(report);
        var input=Path.Combine(root.Root,"synthetic usage.json");var original=Encoding.UTF8.GetBytes("\uFEFF  "+UsageReviewSchema.Serialise(report)+"\n");File.WriteAllBytes(input,original);
        var before=Directory.GetFiles(root.Paths.DataDirectory,"*",SearchOption.AllDirectories);
        var result=await RegisteredReportCliTests.Run("usage-report-evidence","--input",input,"--tenant",TestData.TenantA,"--format",format,"--root",root.Root);
        Assert.True(result.Exit==0,result.Output+result.Error);Assert.Empty(result.Error);Assert.Contains("no live reads",result.Output);Assert.Contains("Unknown",result.Output);
        var output=Assert.Single(Directory.GetFiles(root.Paths.ReportsDirectory));Assert.NotEmpty(File.ReadAllBytes(output));
        if(format=="json")Assert.Equal(original,File.ReadAllBytes(output));
        if(format=="html"){Assert.Contains("privacy",File.ReadAllText(output),StringComparison.OrdinalIgnoreCase);Assert.Contains("&lt;script&gt;",File.ReadAllText(output));Assert.DoesNotContain("<script>",File.ReadAllText(output));Assert.Contains("unverified",File.ReadAllText(output),StringComparison.OrdinalIgnoreCase);}
        if(format=="csv")
        {
            using var zip=ZipFile.OpenRead(output);var entry=zip.Entries.Single(item=>item.FullName.Contains("Email_activity",StringComparison.Ordinal));
            using var reader=new StreamReader(entry.Open());Assert.Contains("'=SYNTHETIC()",reader.ReadToEnd());
        }
        Assert.Equal(original,File.ReadAllBytes(input));Assert.Equal(before,Directory.GetFiles(root.Paths.DataDirectory,"*",SearchOption.AllDirectories));
    }
    [Theory][InlineData("duplicate")][InlineData("unknown")][InlineData("tenant")][InlineData("format")]
    public async Task Invalid_offline_options_are_refused_before_any_export(string fault)
    {
        using var root=new TempRoot();var report=UsageReviewSchemaTests.Report();UsageReviewSchema.Seal(report);var input=Path.Combine(root.Root,"source.json");File.WriteAllText(input,UsageReviewSchema.Serialise(report));
        var args=new List<string>{"usage-report-evidence","--input",input,"--tenant",fault=="tenant"?TestData.TenantB:TestData.TenantA,"--root",root.Root};
        if(fault=="duplicate")args.AddRange(["--tenant",TestData.TenantA]);if(fault=="unknown")args.AddRange(["--url","https://synthetic.invalid"]);if(fault=="format")args.AddRange(["--format","markdown"]);
        var result=await RegisteredReportCliTests.Run(args.ToArray());Assert.Equal(2,result.Exit);Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
