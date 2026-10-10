using System.Diagnostics;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ConfigurationInventoryTests
{
    [Fact]
    public void Inventory_preserves_all_returned_settings_assignments_and_identity_without_changing_source()
    {
        var standard = TestData.Standard(); var capture = TestData.Snapshot(standard);
        capture.Collections["conditionalAccess"].Items.Add(new JsonObject
        {
            ["id"] = "synthetic-policy", ["displayName"] = "Synthetic policy", ["state"] = "disabled",
            ["optional"] = null, ["assignments"] = new JsonArray(new JsonObject { ["target"] = "synthetic-group" })
        });
        capture.Collections["conditionalAccess"].Count = 1;
        capture.IntegrityDigest = EvidenceIntegrity.Compute(capture);
        var before = ToolkitJson.Serialize(capture);
        var html = ConfigurationInventoryHtml.Render(capture, standard);
        Assert.Contains("Synthetic policy", html); Assert.Contains("synthetic-policy", html);
        Assert.Contains("assignments", html); Assert.Contains("synthetic-group", html); Assert.Contains("disabled", html);
        Assert.Contains("optional", html); Assert.Contains("null", html);
        Assert.Contains(capture.TenantId, html); Assert.Contains(capture.CapturedAt, html);
        Assert.DoesNotContain("Evidence integrity is unverified", html);
        Assert.Equal(before, ToolkitJson.Serialize(capture));
    }

    [Theory]
    [InlineData(CaptureStatus.Error)]
    [InlineData(CaptureStatus.NotAttempted)]
    public void Unreadable_empty_collections_never_claim_successful_absence(string status)
    {
        var capture = TestData.Snapshot(TestData.Standard());
        capture.Collections.Clear(); capture.Collections["synthetic"] = new CollectionCapture { Status = status, Error = "Synthetic failed read" };
        capture.Complete = true; // Inconsistent legacy metadata cannot hide the actual collection state.
        var html = ConfigurationInventoryHtml.Render(capture);
        Assert.Contains("INCOMPLETE", html); Assert.Contains("absence is unknown", html);
        Assert.DoesNotContain("Read completed successfully; no objects", html);
        Assert.Contains("Synthetic failed read", html);
    }

    [Fact]
    public void Successful_empty_read_is_distinct_from_missing_expected_collections()
    {
        var standard = TestData.Standard(); var capture = TestData.Snapshot(standard);
        capture.Collections.Remove("conditionalAccess");
        var html = ConfigurationInventoryHtml.Render(capture, standard);
        Assert.Contains("Read completed successfully; no objects were returned", html);
        Assert.Contains("Not recorded — unable to check", html); Assert.Contains("INCOMPLETE", html);
    }

    [Fact]
    public void Incomplete_assignments_and_inconsistent_counts_remain_unknown()
    {
        var capture = TestData.Snapshot(TestData.Standard()); capture.Collections.Clear();
        capture.Collections["partial"] = new CollectionCapture { Status = CaptureStatus.Collected, DetailIncomplete = true };
        capture.Collections["count"] = new CollectionCapture { Status = CaptureStatus.Collected, Count = 1 };
        var html = ConfigurationInventoryHtml.Render(capture);
        Assert.Contains("detail or assignments incomplete", html);
        Assert.Contains("recorded count differs", html);
        Assert.DoesNotContain("Read completed successfully; no objects", html);
    }

    [Fact]
    public void Hostile_names_paths_and_properties_are_inert_HTML()
    {
        var capture = TestData.Snapshot(TestData.Standard());
        capture.TenantName = "<script>alert('tenant')</script>";
        capture.Collections["conditionalAccess"].Path = "<img src=x onerror=alert(1)>";
        capture.Collections["conditionalAccess"].Items.Add(new JsonObject { ["displayName"] = "<script>alert('object')</script>", ["<svg onload=alert(1)>"] = "hostile" });
        var html = ConfigurationInventoryHtml.Render(capture);
        Assert.DoesNotContain("<script>", html); Assert.DoesNotContain("<img src=x", html); Assert.DoesNotContain("<svg onload", html);
        Assert.Contains("&lt;script&gt;", html); Assert.Contains("Content-Security-Policy", html);
    }

    [Fact]
    public void Modified_evidence_is_bannered_and_a_different_catalogue_cannot_relabel_the_capture()
    {
        var standard = TestData.Standard(); var capture = TestData.Snapshot(standard);
        capture.IntegrityDigest = EvidenceIntegrity.Compute(capture); capture.TenantName = "Changed after capture";
        standard.Release = "different-release"; standard.Collections["conditionalAccess"].Label = "Wrong catalogue label";
        var html = ConfigurationInventoryHtml.Render(capture, standard);
        Assert.Contains("Evidence integrity is unverified", html); Assert.DoesNotContain("Wrong catalogue label", html);
        Assert.Contains(capture.StandardRelease, html);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public async Task Actual_offline_CLI_exports_inventory_and_refuses_modified_source(bool modified, int expectedExit)
    {
        using var root = new TempRoot(); var standard = TestData.Standard(); var capture = TestData.Snapshot(standard);
        var store = new EvidenceStore(root.Paths, NullLog.Instance); store.SaveSnapshot(capture);
        var input = Path.Combine(root.Root, "synthetic-capture.json");
        if (modified) capture.TenantName = "Modified";
        await File.WriteAllTextAsync(input, ToolkitJson.Serialize(capture));
        var config = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var dll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BDIT.TenantToolkit.Cli", "bin", config, "net10.0", "bdit.dll"));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { dll, "inventory", "--snapshot", input, "--root", root.Root }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        Assert.Equal(expectedExit, process.ExitCode);
        var files = Directory.Exists(root.Paths.ReportsDirectory) ? Directory.GetFiles(root.Paths.ReportsDirectory, "configuration-*.html") : Array.Empty<string>();
        if (modified) { Assert.Empty(files); Assert.Contains("Refused", await stderr); }
        else { Assert.Single(files); Assert.Contains("Tenant configuration inventory", await File.ReadAllTextAsync(files[0])); Assert.Contains("no live reads or writes", await stdout); }
    }

    [Fact]
    public void Supplemental_service_data_retains_separate_provenance_and_cross_tenant_data_is_refused()
    {
        var capture = TestData.Snapshot(TestData.Standard()); capture.ExchangeCapture = ExchangeTestData.Capture();
        var html = ConfigurationInventoryHtml.Render(capture);
        Assert.Contains("Separate Exchange / Purview observations", html); Assert.Contains(capture.ExchangeCapture.CapturedAt, html);
        Assert.Contains("UnifiedAuditLogIngestionEnabled", html); Assert.Contains("acceptedDomains", html);
        capture.ExchangeCapture.ExchangeTenantId = TestData.TenantB;
        Assert.Throws<BDIT.TenantToolkit.Core.TenantMismatchException>(() => ConfigurationInventoryHtml.Render(capture));
    }
}
