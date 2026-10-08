using System.Diagnostics;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ScopedCliTests
{
    [Theory]
    [InlineData("control")]
    [InlineData("area")]
    [InlineData("missing-dependency")]
    [InlineData("modified")]
    [InlineData("both")]
    [InlineData("unknown")]
    [InlineData("missing-client")]
    public async Task Actual_offline_cli_shares_partial_evaluation_and_refuses_invalid_inputs(string scenario)
    {
        using var root = new TempRoot();
        var catalogue = TestData.Standard();
        root.WriteStandard("test.json", ToolkitJson.Serialize(catalogue)); root.WriteManifest();
        catalogue = new StandardsLoader(root.Paths, NullLog.Instance).Load("test.json");
        var profile = TestData.Profile(); var source = TestData.Snapshot(catalogue);
        if (scenario == "missing-dependency") source.Collections.Remove("licences");
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        if (scenario != "missing-client") store.SaveProfiles([profile]);
        store.SaveMappings(TestData.Mappings());
        source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        if (scenario == "modified") source.TenantName = "Changed after hashing";
        var input = Path.Combine(root.Root, "synthetic-source.json");
        await File.WriteAllTextAsync(input, ToolkitJson.Serialize(source));
        var bytes = await File.ReadAllBytesAsync(input);
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BDIT.TenantToolkit.Cli", "bin", config, "net10.0", "bdit.dll"));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { dll, "check", "--snapshot", input, "--root", root.Root }) start.ArgumentList.Add(arg);
        var selector = scenario == "area" ? new[] { "--area", "Entra" }
            : scenario == "both" ? new[] { "--area", "Entra", "--control", "CA-001" }
            : new[] { "--control", scenario == "unknown" ? "UNKNOWN-001" : "CA-001" };
        foreach (var arg in selector) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var accepted = scenario is "control" or "area" or "missing-dependency";
        Assert.Equal(accepted ? 0 : 2, process.ExitCode);
        if (accepted)
        {
            var actual = ScopedCheckSchema.Read(await output, catalogue, profile);
            var selection = scenario == "area" ? CheckSelection.ForArea(catalogue, profile, "Entra") : CheckSelection.ForControl(catalogue, profile, "CA-001");
            var expected = new ScopedCheckService(new FixedClock(), "test", NullLog.Instance)
                .ReviewHistorical(source, catalogue, profile, selection, TestData.Mappings(), [], "synthetic reviewer");
            Assert.Equal(expected.Assessment.Findings.Select(f => (f.ControlId, f.Status)), actual.Assessment.Findings.Select(f => (f.ControlId, f.Status)));
            Assert.Equal("historicalFiltered", actual.SourceMode); Assert.False(actual.Capture.Complete);
            Assert.Null(actual.AccountObjectId); Assert.Equal(source.CapturedAt, actual.Capture.CapturedAt);
            Assert.Empty(await error);
        }
        else { Assert.Empty(await output); Assert.Contains("Refused:", await error); }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(input));
        Assert.False(Directory.Exists(Path.Combine(root.Paths.TenantDirectory(profile.TenantId), "scoped-checks")));
        Assert.Empty(store.ListSnapshots(profile.TenantId));
    }
}
