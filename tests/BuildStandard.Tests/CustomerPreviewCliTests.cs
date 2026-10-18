using System.Text.Json.Nodes;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Identity;
using Xunit;

namespace BuildStandard.Tests;

public sealed class CustomerPreviewCliTests
{
    [Fact]
    public async Task Offline_preview_matches_engine_and_does_not_change_profiles_or_create_evidence()
    {
        using var root = new TempRoot();
        var profiles = new[] { new TenantProfile { TenantId = TestData.TenantA, Company = "Saved original" } };
        var profileText = ToolkitJson.Serialize(profiles);
        File.WriteAllText(root.Paths.ProfilesFile, profileText);
        var input = Path.Combine(root.Root, "customers.csv");
        File.WriteAllText(input, $"Microsoft ID,Company name\n{TestData.TenantA},Different name\n{TestData.TenantB},New client\n");
        var files = Directory.GetFiles(root.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var expected = PartnerCustomerImport.Preview(File.ReadAllBytes(input), profiles);
        var result = await RegisteredReportCliTests.Run("customers", "--csv", input, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Output + result.Error);
        var json = JsonNode.Parse(result.Output)!;
        Assert.Equal("customerImportPreview", json["kind"]!.GetValue<string>());
        Assert.False(json["saved"]!.GetValue<bool>());
        Assert.Equal(ToolkitJson.Serialize(expected.Rows), ToolkitJson.Serialize(json["rows"]));
        Assert.Equal(profileText, File.ReadAllText(root.Paths.ProfilesFile));
        Assert.Equal(files, Directory.GetFiles(root.Root, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task Absent_profile_file_is_explicitly_local_preview_not_a_save()
    {
        using var root = new TempRoot();
        var input = Path.Combine(root.Root, "customers.csv");
        File.WriteAllText(input, $"Microsoft ID,Company name\n{TestData.TenantA},New client\n");
        var result = await RegisteredReportCliTests.Run("customers", "--csv", input, "--root", root.Root);
        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("preview", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(root.Paths.ProfilesFile));
    }

    [Theory]
    [InlineData("data")]
    [InlineData("profiles")]
    public async Task Blocked_profile_storage_cannot_claim_all_import_rows_are_new(string blocked)
    {
        using var root = new TempRoot();
        if (blocked == "data") { Directory.Delete(root.Paths.DataDirectory, true); File.WriteAllText(root.Paths.DataDirectory, "blocked"); }
        else Directory.CreateDirectory(root.Paths.ProfilesFile);
        var input = Path.Combine(root.Root, "customers.csv");
        File.WriteAllText(input, $"Microsoft ID,Company name\n{TestData.TenantA},New client\n");
        var result = await RegisteredReportCliTests.Run("customers", "--csv", input, "--root", root.Root);
        Assert.NotEqual(0, result.Exit);
        Assert.Contains("profile", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("customerImportPreview", result.Output);
    }

    [Fact]
    public async Task Oversized_csv_is_refused_before_a_preview_is_printed()
    {
        using var root = new TempRoot(); var input = Path.Combine(root.Root, "large.csv");
        using (var file = File.Create(input)) file.SetLength(PartnerCustomerImport.MaximumBytes + 1);
        var result = await RegisteredReportCliTests.Run("customers", "--csv", input, "--root", root.Root);
        Assert.Equal(2, result.Exit);
        Assert.Contains("5 MiB", result.Error);
        Assert.DoesNotContain("customerImportPreview", result.Output);
    }

    [Theory]
    [InlineData("--save")]
    [InlineData("--csv")]
    public async Task Unsupported_and_duplicate_options_are_refused(string option)
    {
        using var root = new TempRoot();
        var result = await RegisteredReportCliTests.Run("customers", "--csv", "unused.csv", option, "unused.csv", "--root", root.Root);
        Assert.Equal(2, result.Exit);
        Assert.Contains(option, result.Error);
        Assert.DoesNotContain("customerImportPreview", result.Output);
    }

    [Fact]
    public async Task Options_follow_the_existing_case_insensitive_CLI_contract()
    {
        using var root = new TempRoot(); var input = Path.Combine(root.Root, "customers.csv");
        File.WriteAllText(input, $"Microsoft ID,Company name\n{TestData.TenantA},New client\n");
        var result = await RegisteredReportCliTests.Run("CUSTOMERS", "--CSV", input, "--ROOT", root.Root);
        Assert.True(result.Exit == 0, result.Error);
        Assert.Contains("customerImportPreview", result.Output);
    }

}
