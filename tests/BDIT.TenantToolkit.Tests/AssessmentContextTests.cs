using BDIT.TenantToolkit.Core;
using System.Diagnostics;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class AssessmentContextTests
{
    [Fact]
    public async Task Actual_headless_json_report_matches_desktop_assessment_for_combined_existing_evidence_and_does_not_write_data()
    {
        using var root = new TempRoot();
        var standard = ExchangeTestData.Standard();
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"));
        root.WriteStandard("2026.09.12.json", File.ReadAllText(source)); root.WriteManifest();
        standard = new BDIT.TenantToolkit.Engine.Standards.StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.12.json");
        var store = new EvidenceStore(root.Paths, NullLog.Instance); var profile = TestData.Profile();
        store.SaveProfiles([profile]); store.SaveMappings(new ManagedObjectMappings { TenantId = profile.TenantId });
        var graph = TestData.Snapshot(standard); var primaryFile = store.SaveSnapshot(graph);
        var exchange = ExchangeTestData.Capture(); var supplementalFile = Path.Combine(root.Root, "exchange.json");
        File.WriteAllText(supplementalFile, ToolkitJson.Serialize(exchange));
        var expected = new AssessmentEngine(SystemClock.Instance, "desktop-test").Assess(graph, standard, profile,
            store.LoadMappings(profile.TenantId), store.LoadDeviations(profile.TenantId), "desktop", exchange);
        var original = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);
        var configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? "Release" : "Debug";
        var cli = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../src/BDIT.TenantToolkit.Cli/bin/{configuration}/net10.0/bdit.dll"));
        Assert.True(File.Exists(cli), "The referenced CLI project must be built for this integration check.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { cli, "report", "--root", root.Root, "--release", "2026.09.12.json", "--snapshot", primaryFile, "--exchange-snapshot", supplementalFile, "--format", "json" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        Assert.True(process.ExitCode == 0, await stderr);
        var output = await stdout;
        var reportFile = output.Split('\n').Single(l => l.StartsWith("Report: ", StringComparison.Ordinal))[8..].Trim();
        var actual = ToolkitJson.Deserialize<AssessmentResult>(File.ReadAllText(reportFile))!;
        Assert.Equal(CanonicalJson.Sha256Value(expected.Findings), CanonicalJson.Sha256Value(actual.Findings));
        Assert.Equal(CanonicalJson.Sha256Value(expected.Summary), CanonicalJson.Sha256Value(actual.Summary));
        Assert.Equal(original.Keys.Order(), Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).Order());
        foreach (var (file, bytes) in original) Assert.Equal(bytes, File.ReadAllBytes(file));
    }
    [Fact]
    public void Combined_evidence_uses_the_same_records_and_findings_as_direct_desktop_engine_inputs_without_writing_evidence()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var standard = ExchangeTestData.Standard();
        var profile = TestData.Profile();
        var graph = TestData.Snapshot(standard);
        var exchange = ExchangeTestData.Capture();
        var clock = new FixedClock { UtcNow = ExchangeTestData.Now };
        var engine = new AssessmentEngine(clock, "test");
        store.SaveMappings(new ManagedObjectMappings { TenantId = profile.TenantId });
        var before = Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => File.ReadAllText(f));
        var direct = engine.Assess(graph, standard, profile, store.LoadMappings(profile.TenantId), store.LoadDeviations(profile.TenantId), "desktop", exchange);
        var shared = AssessmentContext.Assess(engine, store, graph, standard, profile, "headless", exchange);
        Assert.Equal(CanonicalJson.Sha256Value(direct.Findings), CanonicalJson.Sha256Value(shared.Findings));
        Assert.Equal(CanonicalJson.Sha256Value(direct.Summary), CanonicalJson.Sha256Value(shared.Summary));
        // Supplemental observations retain the engine's manual-verification boundary; parity must not turn a read into certification.
        Assert.Contains(shared.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.RequiresManualReview && f.ObservedObjects.Count > 0);
        var withoutSupplement = AssessmentContext.Assess(engine, store, graph, standard, profile, "headless");
        Assert.Contains(withoutSupplement.Findings, f => f.ControlId == "EX-004" && f.Status == FindingStatus.UnableToAssess);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(root.Paths.DataDirectory, "*", SearchOption.AllDirectories).Order());
        foreach (var entry in before) Assert.Equal(entry.Value, File.ReadAllText(entry.Key));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Supplemental_existing_formats_are_accepted_and_foreign_tenants_refused(bool wrapped)
    {
        using var root = new TempRoot();
        var capture = ExchangeTestData.Capture();
        var snapshot = ExchangeEvidenceImporter.Snapshot(capture, TestData.Profile(), ExchangeTestData.Standard());
        snapshot.IntegrityDigest = EvidenceIntegrity.Compute(snapshot);
        var file = Path.Combine(root.Root, "supplement.json");
        File.WriteAllText(file, wrapped ? ToolkitJson.Serialize(snapshot) : ToolkitJson.Serialize(capture));
        var read = AssessmentContext.ReadSupplement(file, TestData.TenantA, ExchangeTestData.Now);
        Assert.Equal(capture.Id, read.Id);
        Assert.Throws<TenantMismatchException>(() => AssessmentContext.ReadSupplement(file, TestData.TenantB, ExchangeTestData.Now));
    }

    [Fact]
    public void Modified_wrapper_is_refused_and_a_missing_supplement_cannot_become_empty_success()
    {
        using var root = new TempRoot();
        var snapshot = ExchangeEvidenceImporter.Snapshot(ExchangeTestData.Capture(), TestData.Profile(), ExchangeTestData.Standard());
        snapshot.IntegrityDigest = EvidenceIntegrity.Compute(snapshot);
        snapshot.ExchangeCapture!.Collections["transportConfig"].Items[0]["SmtpClientAuthenticationDisabled"] = false;
        var file = Path.Combine(root.Root, "supplement.json");
        File.WriteAllText(file, ToolkitJson.Serialize(snapshot));
        Assert.Throws<IntegrityException>(() => AssessmentContext.ReadSupplement(file, TestData.TenantA, ExchangeTestData.Now));
        Assert.Throws<FileNotFoundException>(() => AssessmentContext.ReadSupplement(file + ".missing", TestData.TenantA, ExchangeTestData.Now));
        File.WriteAllText(file, "{}");
        Assert.Throws<ConfigurationException>(() => AssessmentContext.ReadSupplement(file, TestData.TenantA, ExchangeTestData.Now));
    }

    [Fact]
    public void Oversized_supplement_is_refused_before_parsing()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Root, "supplement.json");
        using (var stream = File.Create(file)) stream.SetLength(ExchangeCaptureSchema.MaximumBytes * 2L + 1);
        Assert.Contains("4 MiB", Assert.Throws<ConfigurationException>(() => AssessmentContext.ReadSupplement(file, TestData.TenantA, ExchangeTestData.Now)).Message);
    }
}
