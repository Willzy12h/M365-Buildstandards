using System.Diagnostics;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
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

    [Theory]
    [InlineData("area")]
    [InlineData("other-tenant")]
    [InlineData("missing-file")]
    public async Task Offline_check_uses_separate_exchange_evidence_as_report_does(string scenario)
    {
        using var root = new TempRoot();
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.12.json"));
        root.WriteStandard("2026.09.12.json", await File.ReadAllTextAsync(source)); root.WriteManifest();
        var catalogue = new StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.12.json");
        var profile = TestData.Profile();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        store.SaveProfiles([profile]); store.SaveMappings(TestData.Mappings());
        var graph = TestData.Snapshot(catalogue, capturedAt: ExchangeTestData.Now);
        graph.IntegrityDigest = EvidenceIntegrity.Compute(graph);
        var graphFile = Path.Combine(root.Root, "synthetic-graph.json");
        await File.WriteAllTextAsync(graphFile, ToolkitJson.Serialize(graph));
        var exchange = ExchangeTestData.Capture();
        if (scenario == "other-tenant") { exchange.TenantId = TestData.TenantB; exchange.ExchangeTenantId = TestData.TenantB; exchange.PurviewTenantId = TestData.TenantB; }
        var exchangeFile = Path.Combine(root.Root, scenario == "missing-file" ? "absent-exchange.json" : "synthetic-exchange.json");
        if (scenario != "missing-file") await File.WriteAllTextAsync(exchangeFile, ToolkitJson.Serialize(exchange));
        var before = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BDIT.TenantToolkit.Cli", "bin", config, "net10.0", "bdit.dll"));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { dll, "check", "--snapshot", graphFile, "--exchange-snapshot", exchangeFile, "--area", "Exchange", "--root", root.Root, "--release", "2026.09.12.json" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }

        if (scenario == "area")
        {
            Assert.True(process.ExitCode == 0, await error);
            var actual = ScopedCheckSchema.Read(await output, catalogue, profile);
            var expected = new ScopedCheckService(new FixedClock { UtcNow = ExchangeTestData.Now.AddMinutes(1) }, "test", NullLog.Instance)
                .ReviewHistorical(graph, catalogue, profile, CheckSelection.ForArea(catalogue, profile, "Exchange"), TestData.Mappings(), [], "synthetic reviewer", exchange);
            Assert.Equal(expected.Assessment.Findings.Select(f => (f.ControlId, f.Status)), actual.Assessment.Findings.Select(f => (f.ControlId, f.Status)));
            Assert.Contains(actual.Assessment.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.RequiresManualReview);
            Assert.Equal(exchange.Id, actual.SeparateExchange!.Id);
            Assert.Equal(SnapshotIntegrityState.Intact, actual.Assessment.SnapshotIntegrity);
        }
        else
        {
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await output); Assert.Contains("Refused:", await error);
        }
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).Order());
        foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
    }
}
