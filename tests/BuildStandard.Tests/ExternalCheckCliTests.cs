using System.Text.Json.Nodes;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ExternalCheckCliTests
{
    [Theory][InlineData("html")][InlineData("json")][InlineData("csv")][InlineData("xlsx")]
    public async Task Actual_offline_command_exports_claims_without_creating_tenant_evidence(string format)
    {
        using var root = new TempRoot(); var original = ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture());
        var input = Path.Combine(root.Root, "external.json"); File.WriteAllBytes(input, original);
        var before = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories);
        var result = await RegisteredReportCliTests.Run("external-checks", "--input", input, "--tenant", TestData.TenantA, "--format", format, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Output + result.Error);
        var output = JsonNode.Parse(result.Output)!; Assert.Equal("Partial", output["sourceState"]!.GetValue<string>());
        Assert.Contains("Untrusted", output["assurance"]!.GetValue<string>()); Assert.True(File.Exists(output["file"]!.GetValue<string>()));
        Assert.Equal(original, File.ReadAllBytes(input)); Assert.Equal(before, Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories));
    }
    [Theory][InlineData("tenant")][InlineData("duplicate")][InlineData("scope")][InlineData("format")]
    public async Task Ambiguous_or_unapproved_options_refuse_without_outputs(string fault)
    {
        using var root = new TempRoot(); var input = Path.Combine(root.Root, "external.json"); File.WriteAllBytes(input, ExternalCheckResultsTests.Bytes(ExternalCheckResultsTests.Fixture()));
        var args = new List<string> { "external-checks", "--input", input, "--tenant", fault == "tenant" ? TestData.TenantB : TestData.TenantA, "--root", root.Root };
        if (fault == "duplicate") args.AddRange(["--input", input]);
        if (fault == "scope") args.AddRange(["--scope", "Directory.Read.All"]);
        if (fault == "format") args.AddRange(["--format", "markdown"]);
        var result = await RegisteredReportCliTests.Run(args.ToArray()); Assert.Equal(2, result.Exit);
        Assert.Empty(Directory.GetFiles(root.Paths.ReportsDirectory));
    }
}
