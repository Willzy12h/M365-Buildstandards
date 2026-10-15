using System.Diagnostics;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Diagnostics;
using Xunit;

namespace BuildStandard.Tests;

public sealed class DoctorCliDispatchTests
{
    private static async Task<(int Exit, string Output, string Error)> Run(string[] args, string? environmentRoot = null)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BuildStandard.Cli", "bin", configuration, "net10.0", "BuildStandard.Cli.dll"));
        Assert.True(File.Exists(cli), "Build the actual CLI before its process tests.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(cli);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        start.Environment.Remove("BDIT_TOOLKIT_ROOT");
        start.Environment.Remove("BUILDSTANDARD_ROOT");
        if (environmentRoot is not null) start.Environment["BDIT_TOOLKIT_ROOT"] = environmentRoot;
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    [Theory]
    [InlineData("doctor", "explicit")]
    [InlineData("DOCTOR", "explicit")]
    [InlineData("doctor", "environment")]
    public async Task Actual_process_reuses_shared_findings_preserves_files_and_prints_no_private_context(string command, string rootMode)
    {
        using var fixture = new OfflinePackageFixture();
        foreach (var path in new[] { "data/profiles.json", "data/tenants/private/evidence.json", "logs/existing.log", "reports/private.json" })
            fixture.Write(path, OfflinePackageFixture.Private);
        var before = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var args = rootMode == "explicit" ? new[] { command, "--root", fixture.Root, "--format", "json" } : new[] { command, "--format", "json" };
        var result = await Run(args, rootMode == "environment" ? fixture.Root : null);
        var expected = new OfflinePrerequisiteService().Check(fixture.Root);
        Assert.Equal(expected.Status == OfflinePrerequisiteStatus.Ready ? 0 : 1, result.Exit);
        Assert.Empty(result.Error);
        var actual = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(result.Output);
        Assert.Equal("offlinePrerequisites", actual.Kind); Assert.Equal(1, actual.SchemaVersion);
        Assert.Equal(expected.Status, actual.Status); Assert.Equal(expected.Findings, actual.Findings);
        Assert.DoesNotContain(fixture.Root, result.Output); Assert.DoesNotContain(OfflinePackageFixture.Private, result.Output);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order());
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        fixture.NoProbes();
    }

    [Fact]
    public async Task Actual_default_text_contains_same_findings_as_json_and_neither_is_deployment_evidence()
    {
        using var fixture = new OfflinePackageFixture();
        var json = await Run(["doctor", "--root", fixture.Root, "--format", "json"]);
        var text = await Run(["doctor", "--root", fixture.Root]);
        Assert.Equal(json.Exit, text.Exit); Assert.Empty(json.Error); Assert.Empty(text.Error);
        var parsed = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(json.Output);
        foreach (var finding in parsed.Findings)
        {
            Assert.Contains($"[{finding.Disposition}] {finding.Id} ({finding.Applicability}): {finding.Explanation}", text.Output);
            Assert.Contains("Next: " + finding.NextStep, text.Output);
        }
        Assert.Contains("tenant readiness, write approval and script execution are not certified", text.Output);
        Assert.DoesNotContain("tenantSnapshot", json.Output);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("format")]
    [InlineData("positional")]
    public async Task Doctor_strict_options_are_dispatched_before_the_general_parser_and_refuse_safely(string scenario)
    {
        using var fixture = new OfflinePackageFixture();
        var tail = scenario switch
        {
            "unknown" => new[] { "--repair", OfflinePackageFixture.Private },
            "duplicate" => new[] { "--root", fixture.Root, "--root", fixture.Root },
            "missing" => new[] { "--format" },
            "format" => new[] { "--format", OfflinePackageFixture.Private },
            _ => new[] { OfflinePackageFixture.Private }
        };
        var result = await Run(new[] { "doctor" }.Concat(tail).ToArray());
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.StartsWith("Refused:", result.Error);
        Assert.DoesNotContain(OfflinePackageFixture.Private, result.Error); Assert.DoesNotContain(fixture.Root, result.Error);
        fixture.NoProbes();
    }

    [Theory]
    [InlineData("explicit")]
    [InlineData("environment")]
    public async Task Invalid_explicit_or_default_root_stays_inside_doctor_sanitisation(string rootMode)
    {
        using var fixture = new OfflinePackageFixture();
        var missing = fixture.FilePath("private-user-missing-root");
        var result = rootMode == "explicit" ? await Run(["doctor", "--root", missing, "--format", "json"])
            : await Run(["doctor", "--format", "json"], missing);
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output);
        Assert.DoesNotContain(missing, result.Error); Assert.DoesNotContain("private-user", result.Error);
        Assert.StartsWith("Refused:", result.Error);
    }

    [Fact]
    public async Task Missing_published_metadata_is_readable_needs_attention_with_exit_one()
    {
        using var fixture = new OfflinePackageFixture();
        File.Delete(fixture.FilePath("VERSION.json"));
        var result = await Run(["doctor", "--root", fixture.Root, "--format", "json"]);
        Assert.Equal(1, result.Exit); Assert.Empty(result.Error);
        var diagnostic = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(result.Output);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, diagnostic.Status);
        Assert.Equal(OfflineFindingDisposition.Unknown, OfflinePrerequisiteTests.Finding(diagnostic, "package.metadata").Disposition);
    }

    [Fact]
    public async Task Existing_help_and_unknown_command_exits_are_preserved_and_doctor_limits_are_visible()
    {
        var help = await Run(["--help"]); Assert.Equal(0, help.Exit); Assert.Empty(help.Error);
        Assert.Contains("buildstandard doctor [--root <folder>] [--format text|json]", help.Output);
        Assert.Contains("No repair, installation, sign-in or script execution", help.Output);
        Assert.Contains("130 cancelled", help.Output);
        var unknown = await Run(["unknown-safe-command"]);
        Assert.Equal(64, unknown.Exit); Assert.Contains("Unknown command", unknown.Error);
    }
    [Fact]
    public async Task Help_names_neutral_primary_entry_and_describes_legacy_alias()
    {
        var result = await Run(new[] { "--help" });
        Assert.Equal(0, result.Exit);
        Assert.StartsWith("buildstandard ", result.Output);
        Assert.Contains("offline reports and local prerequisite diagnostics. Never connects to a tenant.", result.Output);
        Assert.Contains("buildstandard doctor", result.Output);
        Assert.Contains("bdit.cmd remains a compatible alias", result.Output);
        Assert.Contains("BUILDSTANDARD_ROOT", result.Output);
        Assert.Empty(result.Error);
    }
}
