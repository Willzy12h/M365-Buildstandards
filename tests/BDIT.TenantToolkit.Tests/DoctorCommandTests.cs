using System.Globalization;
using System.Text.Json;
using BDIT.TenantToolkit.Cli;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Diagnostics;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class DoctorCommandTests
{
    private static (int Exit, string Output, string Error) Invoke(OfflinePackageFixture fixture, string[] args,
        OfflineDiagnosticIo? io = null, CancellationToken ct = default, IClock? clock = null, OfflineDiagnosticHost? host = null)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var exit = DoctorCommand.Run(args, fixture.Root, output, error, ct, fixture.Service(io, clock: clock, host: host));
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public void Actual_handler_json_and_text_have_the_same_shared_engine_findings_and_controlled_times()
    {
        using var fixture = new OfflinePackageFixture();
        var json = Invoke(fixture, ["--format", "json"], clock: new TwoTimesClock());
        var text = Invoke(fixture, [], clock: new TwoTimesClock());
        var shared = fixture.Service(clock: new TwoTimesClock()).Check(fixture.Root);
        Assert.Equal(0, json.Exit); Assert.Equal(0, text.Exit);
        Assert.Empty(json.Error); Assert.Empty(text.Error);
        var parsed = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(json.Output);
        Assert.Equal("offlinePrerequisites", parsed.Kind); Assert.Equal(1, parsed.SchemaVersion);
        Assert.Equal("2026-10-10T12:00:00.000Z", parsed.StartedAt); Assert.Equal("2026-10-10T12:00:05.000Z", parsed.EndedAt);
        Assert.Equal(shared.Findings, parsed.Findings);
        foreach (var finding in shared.Findings)
        {
            Assert.Contains($"[{finding.Disposition}] {finding.Id} ({finding.Applicability}): {finding.Explanation}", text.Output);
            Assert.Contains("Next: " + finding.NextStep, text.Output);
        }
        using var document = JsonDocument.Parse(json.Output);
        Assert.Equal("offlinePrerequisites", document.RootElement.GetProperty("kind").GetString());
        foreach (var privateValue in new[] { fixture.Root, OfflinePackageFixture.Private, "builtOn", "sourceCommit" })
        { Assert.DoesNotContain(privateValue, json.Output); Assert.DoesNotContain(privateValue, text.Output); }
    }

    [Theory]
    [InlineData("--repair", "yes")]
    [InlineData("--format", "html")]
    [InlineData("--format")]
    [InlineData("--root")]
    [InlineData("--root", "--format", "json")]
    [InlineData("--format", "json", "--format", "text")]
    [InlineData("--root", ".", "--root", ".")]
    [InlineData("--format", "json", "private-option")]
    public void Invalid_options_refuse_before_any_diagnostic_read_or_probe(params string[] args)
    {
        using var fixture = new OfflinePackageFixture();
        var io = new OfflineDiagnosticIo { Attributes = _ => throw new ApplicationException(OfflinePackageFixture.Private), CreateProbe = _ => throw new ApplicationException(OfflinePackageFixture.Private) };
        var result = Invoke(fixture, args, io);
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output);
        Assert.StartsWith("Refused:", result.Error);
        Assert.DoesNotContain(OfflinePackageFixture.Private, result.Error);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("file")]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("network")]
    public void Invalid_requested_root_refuses_with_safe_stderr_and_no_result(string kind)
    {
        using var fixture = new OfflinePackageFixture();
        var root = kind switch { "missing" => fixture.FilePath("private-missing"), "file" => fixture.FilePath("VERSION.json"), "malformed" => "private\0path", "network" => "\\\\private-server\\private-share", _ => " " };
        var result = Invoke(fixture, ["--root", root, "--format", "json"]);
        Assert.Equal(2, result.Exit); Assert.Empty(result.Output); Assert.StartsWith("Refused:", result.Error);
        Assert.DoesNotContain("private", result.Error); Assert.DoesNotContain(fixture.Root, result.Error);
    }

    [Theory]
    [InlineData("rootRead")]
    [InlineData("packageRead")]
    [InlineData("rootWrite")]
    public void Admitted_access_failures_return_one_with_readable_incomplete_result_and_no_private_error(string kind)
    {
        using var fixture = new OfflinePackageFixture();
        var io = new OfflineDiagnosticIo
        {
            Attributes = path => kind == "rootRead" && path == fixture.Root ? throw new UnauthorizedAccessException(OfflinePackageFixture.Private) : File.GetAttributes(path),
            Read = path => kind == "packageRead" && path.EndsWith("App.dll", StringComparison.Ordinal) ? throw new IOException(OfflinePackageFixture.Private) : File.OpenRead(path),
            CreateProbe = path => kind == "rootWrite" ? throw new UnauthorizedAccessException(OfflinePackageFixture.Private) : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
        };
        var result = Invoke(fixture, ["--format", "json"], io);
        Assert.Equal(1, result.Exit); Assert.Empty(result.Error);
        var diagnostic = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(result.Output);
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, diagnostic.Status);
        Assert.Contains(diagnostic.Findings, f => f.Applicability == OfflineFindingApplicability.Required && f.Disposition == OfflineFindingDisposition.Unknown);
        Assert.DoesNotContain(OfflinePackageFixture.Private, result.Output); Assert.DoesNotContain(fixture.Root, result.Output);
    }

    [Theory]
    [InlineData("attributes")]
    [InlineData("read")]
    [InlineData("probe")]
    [InlineData("cleanup")]
    [InlineData("invalidOperation")]
    public void Unexpected_private_errors_are_sanitised_inside_actual_handler_before_general_CLI_catch(string stage)
    {
        using var fixture = new OfflinePackageFixture();
        var io = new OfflineDiagnosticIo
        {
            Attributes = path => stage == "attributes" ? throw new ApplicationException(OfflinePackageFixture.Private) : File.GetAttributes(path),
            Read = path => stage == "read" ? throw new ApplicationException(OfflinePackageFixture.Private)
                : stage == "invalidOperation" ? throw new InvalidOperationException(OfflinePackageFixture.Private) : File.OpenRead(path),
            CreateProbe = path => stage == "probe" ? throw new ApplicationException(OfflinePackageFixture.Private) : new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None),
            DeleteProbe = path => { if (stage == "cleanup") throw new ApplicationException(OfflinePackageFixture.Private); File.Delete(path); }
        };
        var result = Invoke(fixture, ["--format", "json"], io);
        Assert.Equal(3, result.Exit); Assert.Empty(result.Output);
        Assert.Contains("No readiness was certified", result.Error);
        Assert.DoesNotContain(OfflinePackageFixture.Private, result.Error); Assert.DoesNotContain(fixture.Root, result.Error);
    }

    [Fact]
    public void Output_failure_and_private_stderr_failure_do_not_escape_to_raw_general_catch()
    {
        using var fixture = new OfflinePackageFixture(); using var writer = new PrivateThrowWriter();
        var exit = DoctorCommand.Run(["--format", "json"], fixture.Root, writer, writer, default, fixture.Service());
        Assert.Equal(3, exit);
    }

    [Fact]
    public void Cancellation_returns_130_with_a_separate_cancelled_result()
    {
        using var fixture = new OfflinePackageFixture(); using var cts = new CancellationTokenSource(); cts.Cancel();
        var result = Invoke(fixture, ["--format", "json"], ct: cts.Token);
        Assert.Equal(130, result.Exit); Assert.Empty(result.Error);
        var diagnostic = ToolkitJson.Deserialize<OfflinePrerequisiteResult>(result.Output);
        Assert.Equal(OfflinePrerequisiteStatus.Cancelled, diagnostic.Status);
        Assert.All(diagnostic.Findings.Where(f => f.Applicability == OfflineFindingApplicability.Required), f => Assert.Equal(OfflineFindingDisposition.Unknown, f.Disposition));
    }

    [Fact]
    public void Unsupported_platform_is_required_failure_and_optional_host_presence_does_not_prove_execution()
    {
        using var fixture = new OfflinePackageFixture();
        fixture.Write("unexecuted-host.exe", OfflinePackageFixture.Private);
        var host = new OfflineDiagnosticHost(false, _ => null);
        Assert.Equal(1, Invoke(fixture, ["--format", "json"], host: host).Exit);
        var reads = new List<string>();
        var io = new OfflineDiagnosticIo { Read = path => { reads.Add(path); return File.OpenRead(path); } };
        var observed = fixture.Service(io, host: new(true, _ => fixture.FilePath("unexecuted-host.exe"))).Check(fixture.Root);
        Assert.Equal(OfflinePrerequisiteStatus.Ready, observed.Status);
        Assert.Equal(OfflineFindingDisposition.Pass, OfflinePrerequisiteTests.Finding(observed, "powershell.7").Disposition);
        Assert.Contains("not executed or version-verified", OfflinePrerequisiteTests.Finding(observed, "powershell.7").Explanation);
        Assert.DoesNotContain(fixture.FilePath("unexecuted-host.exe"), reads);
    }

    [Fact]
    public void Public_handler_default_service_runs_offline_with_explicit_root()
    {
        using var fixture = new OfflinePackageFixture(); using var output = new StringWriter(); using var error = new StringWriter();
        var exit = DoctorCommand.Run(["--root", fixture.Root, "--format", "json"], null, output, error);
        Assert.Equal(OperatingSystem.IsWindows() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64 ? 0 : 1, exit);
        Assert.Equal("offlinePrerequisites", ToolkitJson.Deserialize<OfflinePrerequisiteResult>(output.ToString()).Kind);
        Assert.Empty(error.ToString());
    }

    private sealed class TwoTimesClock : IClock
    {
        private int _reads;
        public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero).AddSeconds(_reads++ == 0 ? 0 : 5);
    }
    private sealed class PrivateThrowWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => throw new IOException(OfflinePackageFixture.Private);
    }
}
