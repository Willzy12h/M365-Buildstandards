using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Graph;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class Preview17AuthAndProcessTests
{
    [Theory]
    [InlineData("tenant")]
    [InlineData("standard")]
    [InlineData("expired")]
    [InlineData("future")]
    public void Retained_confirmation_rejects_changed_or_expired_context(string change)
    {
        var now = DateTimeOffset.UtcNow;
        var identity = new DiscoveredTenant(TestData.TenantA, "Synthetic", "example.invalid", "engineer@example.invalid", TestData.Operator, false);
        var profile = TestData.Profile(change == "tenant" ? TestData.TenantB : TestData.TenantA);
        Assert.Throws<TenantMismatchException>(() => PendingTenantDiscovery.VerifyContext(identity, profile, "original",
            change == "standard" ? "different" : "original", change == "expired" ? now.AddMinutes(-6) : change == "future" ? now.AddMinutes(2) : now, now));
    }

    [Fact]
    public void Retained_confirmation_accepts_only_the_same_fresh_context()
    {
        var now = DateTimeOffset.UtcNow;
        var identity = new DiscoveredTenant(TestData.TenantA, "Synthetic", "example.invalid", "engineer@example.invalid", TestData.Operator, false);
        PendingTenantDiscovery.VerifyContext(identity, TestData.Profile(), "digest", "digest", now, now);
        var session = TestData.Session(mode: SessionMode.Assessment);
        identity.VerifyConnection(session);
        session.OperatorObjectId = TestData.Emergency;
        Assert.Throws<TenantMismatchException>(() => identity.VerifyConnection(session));
        session.OperatorObjectId = TestData.Operator; session.Mode = SessionMode.Deployment;
        Assert.Throws<TenantMismatchException>(() => identity.VerifyConnection(session));
    }

    [Theory]
    [InlineData("Directory.AccessAsUser.All")]
    [InlineData("Application.ReadWrite.All")]
    [InlineData("Directory.Write.All")]
    public async Task Quick_Connect_refuses_write_capable_requested_scopes_before_authentication(string scope)
    {
        using var root = new TempRoot(); using var http = new HttpClient();
        using var log = new ToolkitLogger(root.Paths.LogsDirectory, LogLevel.Debug);
        var error = await Assert.ThrowsAsync<ConfigurationException>(() => TenantDiscoveryService.DiscoverRetainedAsync(http,
            new ToolkitSettings(), TestData.Standard(), new[] { "User.Read", scope }, IntPtr.Zero, log, CancellationToken.None));
        Assert.Contains("cannot request write", error.Message);
    }

    [Fact]
    public void PowerShell_arguments_refuse_invalid_account_and_keep_paths_as_data_without_policy_bypass()
    {
        // The account is now validated before launch, a stricter boundary than merely passing hostile text as data.
        var hostile = "engineer@example.invalid; Write-Output 'must remain data'";
        Assert.Throws<ConfigurationException>(() => ExchangeCaptureRunner.CreateStartInfo("powershell.exe", "C:\\path with spaces\\read.ps1", "C:\\output\\capture.json", hostile, true));
        var account = "engineer@example.invalid";
        var start = ExchangeCaptureRunner.CreateStartInfo("powershell.exe", "C:\\path with spaces\\read.ps1", "C:\\output\\capture.json", account, true);
        Assert.False(start.UseShellExecute);
        Assert.True(start.RedirectStandardOutput); Assert.True(start.RedirectStandardError);
        Assert.Contains(account, start.ArgumentList);
        Assert.Contains("C:\\path with spaces\\read.ps1", start.ArgumentList);
        Assert.Contains("-File", start.ArgumentList); Assert.Contains("-Integrated", start.ArgumentList);
        Assert.DoesNotContain("-Command", start.ArgumentList); Assert.DoesNotContain("-ExecutionPolicy", start.ArgumentList);
        Assert.Empty(start.Arguments);
    }

    [Fact]
    public async Task Oversized_and_invalid_UTF8_process_results_are_not_imported()
    {
        using var root = new TempRoot();
        var file = Path.Combine(root.Root, "capture.json");
        await File.WriteAllBytesAsync(file, new byte[ExchangeCaptureSchema.MaximumBytes + 1]);
        await Assert.ThrowsAsync<ConfigurationException>(() => ExchangeCaptureRunner.ReadBoundedResultAsync(file, CancellationToken.None));
        await File.WriteAllBytesAsync(file, new byte[] { 0xff });
        await Assert.ThrowsAsync<System.Text.DecoderFallbackException>(() => ExchangeCaptureRunner.ReadBoundedResultAsync(file, CancellationToken.None));
    }
}
