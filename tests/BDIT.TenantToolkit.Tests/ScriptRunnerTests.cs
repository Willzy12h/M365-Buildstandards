using System.Diagnostics;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Engine.Scripts;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// Run for read-only library items (INT-081, proposed). Each process test runs the real launcher and the real generated
/// Copy wrapper in a real PowerShell (Windows PowerShell 5.1 on Windows, PowerShell 7 elsewhere when installed), against
/// a synthetic stand-in module named BditStubExchange. The stand-in answers the Exchange connection cmdlets and logs every
/// read, so a refused run can be shown to have read nothing. No Microsoft module is loaded and no tenant is contacted.
/// </summary>
public sealed class ScriptRunnerTests : IDisposable
{
    private const string Tenant = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
    private const string OtherTenant = "9b2c1d4e-0000-4000-8000-000000000000";
    private const string Account = "admin@contoso.example";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private const string Stub = """
        function Write-StubLog([string]$Text) { if ($env:BDIT_STUB_LOG) { [IO.File]::AppendAllText($env:BDIT_STUB_LOG, $Text + "`n") } }
        function Connect-ExchangeOnline { Write-StubLog 'connect' }
        function Disconnect-ExchangeOnline { Write-StubLog 'disconnect' }
        function Get-ConnectionInformation {
            [pscustomobject]@{ State = 'Connected'; IsEopSession = $false; TenantID = $env:BDIT_STUB_TENANT; UserPrincipalName = $env:BDIT_STUB_UPN }
        }
        function Get-StubRows {
            param([int]$Count)
            Write-StubLog 'read'
            switch ($env:BDIT_STUB_SCENARIO) {
                'sleep' { Start-Sleep -Seconds 120 }
                'throw' { throw 'Synthetic read failure.' }
                'big' { return @(1..4000 | ForEach-Object { [pscustomobject]@{ Name = 'row' + $_; Value = ('x' * 1000) } }) }
                'console' { foreach ($i in 1..4000) { [Console]::Out.WriteLine('noise ' + ('y' * 500)) } }
            }
            @(1..$Count | ForEach-Object { [pscustomobject]@{ Name = 'row' + $_; Value = '=1+1, "quoted"' } })
        }
        Export-ModuleMember -Function *
        """;

    private const string Body = """
        param(
            [int]$Count = 3,
            [switch]$Partial,
            [switch]$Unknown
        )
        Set-StrictMode -Version Latest
        $rows = @(Get-StubRows -Count $Count)
        if ($Partial) { Write-Warning 'BDIT:PARTIAL Synthetic: more rows exist.' }
        if ($Unknown) { Write-Warning 'BDIT:UNKNOWN Synthetic: one value could not be read.' }
        $rows
        """;

    private readonly string _work = Path.Combine(Path.GetTempPath(), "bdit-run-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _modules;
    private readonly string _log;

    public ScriptRunnerTests()
    {
        _modules = Path.Combine(_work, "modules");
        var module = Path.Combine(_modules, "BditStubExchange", "1.0.0");
        Directory.CreateDirectory(module);
        File.WriteAllText(Path.Combine(module, "BditStubExchange.psm1"), Stub);
        File.WriteAllText(Path.Combine(module, "BditStubExchange.psd1"),
            "@{ ModuleVersion = '1.0.0'; RootModule = 'BditStubExchange.psm1'; GUID = '" + Guid.NewGuid() + "'; FunctionsToExport = '*' }");
        _log = Path.Combine(_work, "stub.log");
    }

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The PowerShell used as the test host: Windows PowerShell 5.1 at its fixed location on Windows (always present on the
    /// CI runner), otherwise PowerShell 7 if installed. Null only on a non-Windows machine without PowerShell.
    /// </summary>
    private static (string Executable, string Runtime)? TestHost()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(File.Exists(OwnedPowerShellProcess.WindowsPowerShellPath()), "Windows PowerShell 5.1 must be present to test Run.");
            return (OwnedPowerShellProcess.WindowsPowerShellPath(), "5.1");
        }
        foreach (var candidate in new[] { Environment.GetEnvironmentVariable("BDIT_TEST_PWSH"), "/opt/pwsh/pwsh", "/usr/bin/pwsh", "/usr/local/bin/pwsh" })
            if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate)) return (candidate, "7");
        return null;
    }

    private Func<ScriptManifest, ScriptRunHost> Host(string scenario = "", string tenant = Tenant, string upn = Account, bool stub = true)
    {
        var host = TestHost()!.Value;
        var environment = new Dictionary<string, string>
        {
            ["BDIT_STUB_TENANT"] = tenant, ["BDIT_STUB_UPN"] = upn, ["BDIT_STUB_SCENARIO"] = scenario, ["BDIT_STUB_LOG"] = _log,
            // Only the stand-in folder is added in front; the real Exchange module is never named by these items.
            ["PSModulePath"] = stub ? _modules + Path.PathSeparator + Environment.GetEnvironmentVariable("PSModulePath") : Environment.GetEnvironmentVariable("PSModulePath") ?? ""
        };
        return _ => new ScriptRunHost(host.Executable, host.Runtime, environment);
    }

    private static ScriptEntry Entry(string body = Body, string module = "BditStubExchange", int maximumRows = 100, ScriptMode mode = ScriptMode.ReadOnly)
    {
        var parameters = new List<ScriptParameter>
        {
            new("Count", "Rows", ScriptParameterType.Integer, "How many synthetic rows.", Minimum: 0, Maximum: 10_000, Default: "3"),
            new("Partial", "Say partial", ScriptParameterType.Boolean, "Warn BDIT:PARTIAL."),
            new("Unknown", "Say unknown", ScriptParameterType.Boolean, "Warn BDIT:UNKNOWN.")
        };
        var sha = ScriptCatalogue.Sha256(Encoding.ASCII.GetBytes(body));
        var manifest = new ScriptManifest(1, "test.synthetic-rows", "Synthetic rows", "test", "Synthetic", "Synthetic rows for the runner tests.",
            Array.Empty<string>(), mode, "test/Get-SyntheticRows.ps1", sha, new[] { "5.1", "7" }, new[] { new ScriptModule(module, "1.0.0") },
            Array.Empty<string>(), Array.Empty<string>(), new[] { "exchangeOnline" }, parameters, Array.Empty<ScriptRule>(),
            new ScriptOutputSchema(new[] { "Name", "Value" }), 1, new ScriptLimits(maximumRows, 60), "Synthetic stand-in module.", ScriptLiveStatus.Unverified,
            new[] { "Synthetic." });
        return new ScriptEntry(manifest, body, new string('a', 64));
    }

    private static ScriptRunRequest Request(ScriptEntry entry, params (string Name, string? Value)[] fields) =>
        new(entry, ScriptInputs.Bind(entry.Manifest, fields.ToDictionary(f => f.Name, f => f.Value), Now),
            new ScriptCopyTarget(Tenant, "Contoso (synthetic)", Account), Now);

    private string[] Log() => File.Exists(_log) ? File.ReadAllLines(_log).Where(l => l.Length > 0).ToArray() : Array.Empty<string>();

    // ---- checks made before anything starts ---------------------------------------------------------------------

    [Fact]
    public async Task A_change_item_is_never_run_whatever_the_caller_asks()
    {
        var called = false;
        var runner = new ScriptRunner(_ => { called = true; throw new InvalidOperationException("No host may be resolved for a change item."); });
        var entry = Entry(mode: ScriptMode.CopyOnlyChange);
        Assert.Throws<SafetyViolationException>(() => ScriptRunner.Prepare(Request(entry)));
        await Assert.ThrowsAsync<SafetyViolationException>(() => runner.RunAsync(Request(entry), null, CancellationToken.None));
        Assert.False(called);
    }

    [Fact]
    public void A_body_that_no_longer_matches_its_pin_or_a_forged_argument_is_refused_before_starting()
    {
        var entry = Entry();
        var tampered = entry with { Script = entry.Script + "\nGet-Date\n" };
        Assert.Throws<IntegrityException>(() => ScriptRunner.Prepare(Request(tampered)));

        var request = Request(entry);
        var forged = request with { Binding = new ScriptBinding(new[] { new ScriptArgument("Count = 1; Remove-Item x #", new ScriptNumber(1)) }, Array.Empty<string>()) };
        Assert.Throws<SafetyViolationException>(() => ScriptRunner.Prepare(forged));

        var invalid = request with { Binding = ScriptInputs.Bind(entry.Manifest, new Dictionary<string, string?> { ["Count"] = "many" }, Now) };
        Assert.Throws<ConfigurationException>(() => ScriptRunner.Prepare(invalid));
    }

    [Fact]
    public void Run_always_pins_the_tenant_and_the_account()
    {
        var entry = Entry();
        Assert.Throws<TenantMismatchException>(() => ScriptRunner.Prepare(Request(entry) with { Target = new ScriptCopyTarget(null, null, Account) }));
        Assert.Throws<TenantMismatchException>(() => ScriptRunner.Prepare(Request(entry) with { Target = new ScriptCopyTarget(Tenant, "Contoso", null) }));
        Assert.Throws<TenantMismatchException>(() => ScriptRunner.Prepare(Request(entry) with { Target = new ScriptCopyTarget(Tenant, "Contoso", "not an account") }));
    }

    [Fact]
    public void The_wrapper_that_runs_is_exactly_the_copy_script()
    {
        var entry = ScriptCatalogue.Shipped.Find("exo.mailbox-inventory");
        var binding = ScriptInputs.Bind(entry.Manifest, new Dictionary<string, string?>(), Now);
        var target = new ScriptCopyTarget(Tenant, "Contoso (synthetic)", Account);
        var wrapper = ScriptRunner.Prepare(new ScriptRunRequest(entry, binding, target, Now));
        Assert.Equal(ScriptCopy.Generate(entry, binding, target, Now), wrapper);
        Assert.Contains("$expectedTenantId = '" + Tenant + "'", wrapper);
        Assert.Contains("$signInAs = '" + Account + "'", wrapper);
    }

    [Fact]
    public void The_process_is_the_fixed_launcher_with_plain_arguments_and_no_policy_change()
    {
        var start = ScriptRunner.CreateStartInfo(new ScriptRunHost("powershell.exe", "5.1"), "C:\\run dir\\Run-LibraryScript.ps1", "C:\\run dir\\Library-Item.ps1",
            "C:\\run dir\\output.csv", new[] { new ScriptModule("ExchangeOnlineManagement", "3.7.0") });
        Assert.False(start.UseShellExecute);
        Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-File", "C:\\run dir\\Run-LibraryScript.ps1", "-Wrapper", "C:\\run dir\\Library-Item.ps1",
            "-OutputCsv", "C:\\run dir\\output.csv", "-Modules", "ExchangeOnlineManagement:3.7.0" }, start.ArgumentList);
        foreach (var forbidden in new[] { "-ExecutionPolicy", "Bypass", "-Command", "-EncodedCommand", "-ec", "Install-Module" })
            Assert.DoesNotContain(start.ArgumentList, a => a.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        var launcher = ScriptRunner.Launcher();
        Assert.All(launcher, c => Assert.True(c <= 0x7E));
        foreach (var forbidden in new[] { "Install-Module", "Set-ExecutionPolicy", "-ExecutionPolicy", "Invoke-Expression", "Bypass" })
            Assert.DoesNotContain(forbidden, launcher, StringComparison.OrdinalIgnoreCase);
    }

    // ---- real process runs against the stand-in module -------------------------------------------------------------

    [Fact]
    public async Task A_completed_run_keeps_the_declared_columns_and_rows_and_disconnects()
    {
        if (TestHost() is null) return;
        var progress = new List<string>();
        var result = await new ScriptRunner(Host()).RunAsync(Request(Entry(), ("Count", "4")), new SyncProgress(progress), CancellationToken.None);
        Assert.True(result.End == ScriptRunEnd.Completed, result.Failure);
        Assert.Equal(ReportReadState.Collected, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[] { "Name", "Value" }, result.Columns);
        Assert.Equal(4, result.Rows.Count);
        Assert.Equal(new[] { "row1", "=1+1, \"quoted\"" }, result.Rows[0]);
        Assert.False(result.Partial);
        Assert.False(result.UnknownValues);
        Assert.Equal(new[] { "connect", "read", "disconnect" }, Log());
        Assert.Contains(result.Messages, m => m.StartsWith("Connected to tenant " + Tenant, StringComparison.Ordinal));
        Assert.Contains(progress, p => p.StartsWith("PowerShell started", StringComparison.Ordinal));
        var expected = ScriptCopy.Generate(Entry(), Request(Entry(), ("Count", "4")).Binding, new ScriptCopyTarget(Tenant, "Contoso (synthetic)", Account), Now);
        Assert.Equal(ScriptCatalogue.Sha256(Encoding.UTF8.GetBytes(expected)), result.WrapperSha256);
    }

    [Fact]
    public async Task Partial_and_unknown_warnings_are_carried_into_the_result()
    {
        if (TestHost() is null) return;
        var result = await new ScriptRunner(Host()).RunAsync(Request(Entry(), ("Partial", "true"), ("Unknown", "true")), null, CancellationToken.None);
        Assert.True(result.End == ScriptRunEnd.Completed, result.Failure);
        Assert.Equal(ReportReadState.Partial, result.Status);
        Assert.True(result.PartialWarning);
        Assert.True(result.UnknownValues);
        Assert.Contains(result.Warnings, w => w.StartsWith("BDIT:PARTIAL", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.StartsWith("BDIT:UNKNOWN", StringComparison.Ordinal));
        Assert.Equal(3, result.Rows.Count);
    }

    [Fact]
    public async Task Rows_beyond_the_manifest_limit_are_cut_and_the_run_is_marked_partial()
    {
        if (TestHost() is null) return;
        var result = await new ScriptRunner(Host()).RunAsync(Request(Entry(maximumRows: 2), ("Count", "5")), null, CancellationToken.None);
        Assert.True(result.End == ScriptRunEnd.Completed, result.Failure);
        Assert.True(result.RowsTruncated);
        Assert.Equal(ReportReadState.Partial, result.Status);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public async Task Another_tenant_or_account_is_refused_with_nothing_read()
    {
        if (TestHost() is null) return;
        var tenant = await new ScriptRunner(Host(tenant: OtherTenant)).RunAsync(Request(Entry()), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.ScriptFailed, tenant.End);
        Assert.Equal(ReportReadState.Failed, tenant.Status);
        Assert.Empty(tenant.Rows);
        Assert.Contains(OtherTenant, tenant.Failure);
        Assert.Contains("Nothing was read", tenant.Failure);
        Assert.DoesNotContain("read", Log());
        Assert.Contains("disconnect", Log());

        File.Delete(_log);
        var account = await new ScriptRunner(Host(upn: "other@contoso.example")).RunAsync(Request(Entry()), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.ScriptFailed, account.End);
        Assert.Contains("other@contoso.example", account.Failure);
        Assert.DoesNotContain("read", Log());
    }

    [Fact]
    public async Task A_failing_read_is_a_failed_run_with_the_reason_and_no_rows()
    {
        if (TestHost() is null) return;
        var result = await new ScriptRunner(Host("throw")).RunAsync(Request(Entry()), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.ScriptFailed, result.End);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Synthetic read failure.", result.Failure);
        Assert.Empty(result.Rows);
        Assert.Contains("disconnect", Log());
    }

    [Fact]
    public async Task A_missing_module_is_reported_and_nothing_signs_in()
    {
        if (TestHost() is null) return;
        var body = Body;
        var result = await new ScriptRunner(Host()).RunAsync(Request(Entry(body, module: "BditAbsentModule")), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.ModuleMissing, result.End);
        Assert.Contains("BditAbsentModule 1.0.0 or later", result.Failure);
        Assert.Contains("does not install modules", result.Failure);
        Assert.Empty(Log());
    }

    [Fact]
    public async Task The_manifest_timeout_stops_the_whole_process_tree()
    {
        if (TestHost() is null) return;
        var clock = Stopwatch.StartNew();
        var runner = new ScriptRunner(Host("sleep"), new ScriptRunLimits(Timeout: TimeSpan.FromSeconds(4)));
        var result = await runner.RunAsync(Request(Entry()), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.TimedOut, result.End);
        Assert.Equal(ReportReadState.Failed, result.Status);
        Assert.Null(result.ExitCode);
        Assert.Empty(result.Rows);
        Assert.Contains("4-second", result.Failure);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "The run was not stopped at its limit.");
        // Killed while reading: the wrapper never reached its disconnect, and no later read happened.
        Assert.Equal(new[] { "connect", "read" }, Log());
    }

    [Fact]
    public async Task Cancelling_stops_the_process_and_records_a_cancelled_run()
    {
        if (TestHost() is null) return;
        using var cancel = new CancellationTokenSource();
        var progress = new SyncProgress(new List<string>(), p => { if (p.StartsWith("PowerShell started", StringComparison.Ordinal)) cancel.CancelAfter(TimeSpan.FromSeconds(2)); });
        var clock = Stopwatch.StartNew();
        var result = await new ScriptRunner(Host("sleep")).RunAsync(Request(Entry()), progress, cancel.Token);
        Assert.Equal(ScriptRunEnd.Cancelled, result.End);
        Assert.Equal(ReportReadState.Cancelled, result.Status);
        Assert.Empty(result.Rows);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "Cancelling did not stop the run.");
    }

    [Fact]
    public async Task An_oversized_result_or_console_flood_is_stopped_and_nothing_is_kept()
    {
        if (TestHost() is null) return;
        var big = await new ScriptRunner(Host("big"), new ScriptRunLimits(MaximumCsvBytes: 64 * 1024)).RunAsync(Request(Entry(maximumRows: 10_000)), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.OutputLimit, big.End);
        Assert.Equal(ReportReadState.Failed, big.Status);
        Assert.Empty(big.Rows);

        var noisy = await new ScriptRunner(Host("console"), new ScriptRunLimits(MaximumOutputBytes: 64 * 1024)).RunAsync(Request(Entry()), null, CancellationToken.None);
        Assert.Equal(ScriptRunEnd.OutputLimit, noisy.End);
        Assert.Contains("console output", noisy.Failure);
        Assert.Empty(noisy.Rows);
    }

    // ---- the kept record ------------------------------------------------------------------------------------------

    private static ScriptRunResult Completed(params string[][] rows) => new("7", new string('b', 64), Now, Now.AddSeconds(5), ScriptRunEnd.Completed, 0,
        new[] { "Name", "Value" }, rows, false, false, false, Array.Empty<string>(), new[] { "Connected to tenant " + Tenant + " as " + Account + "." }, null);

    [Fact]
    public void A_run_record_round_trips_through_the_store_and_is_never_replaced()
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var request = Request(Entry(), ("Count", "2"));
        var record = ScriptRunSchema.Create(request, Completed(new[] { "row1", "=cmd|' /C calc'!A0" }), "1.0.0-test");
        var file = store.SaveScriptRun(record);
        Assert.StartsWith(store.ScriptRunsDirectory(Tenant), file);
        var loaded = store.LoadScriptRun(Tenant, record.Id)!;
        Assert.Equal(ReportReadState.Collected, loaded.Status);
        Assert.Equal(record.IntegrityDigest, loaded.IntegrityDigest);
        Assert.Equal(new[] { "Count" }, loaded.Inputs.Select(i => i.Name));
        Assert.Equal("2", loaded.Inputs[0].Value);
        Assert.Equal(Account, loaded.Account);
        Assert.Equal(Entry().Manifest.ScriptSha256, loaded.ScriptSha256);
        Assert.Throws<SafetyViolationException>(() => store.SaveScriptRun(record));
        Assert.Null(store.LoadScriptRun(OtherTenant, record.Id));

        // The exported CSV keeps the declared columns and cannot start a spreadsheet formula.
        var csv = ScriptRunSchema.ToCsv(loaded);
        Assert.StartsWith("\uFEFF\"Name\",\"Value\"\r\n", csv);
        Assert.Contains("\"'=cmd|' /C calc'!A0\"", csv);
    }

    [Fact]
    public void The_strict_reader_refuses_tampering_unknown_members_and_states_that_claim_too_much()
    {
        var request = Request(Entry());
        var record = ScriptRunSchema.Create(request, Completed(new[] { "row1", "v" }), "1.0.0-test");
        var json = ScriptRunSchema.Serialize(record);
        Assert.Equal(record.Id, ScriptRunSchema.Read(json, Tenant).Id);
        Assert.Throws<TenantMismatchException>(() => ScriptRunSchema.Read(json, OtherTenant));
        Assert.Throws<ConfigurationException>(() => ScriptRunSchema.Read(json.Replace("\"row1\"", "\"row9\"", StringComparison.Ordinal), Tenant));
        Assert.Throws<ConfigurationException>(() => ScriptRunSchema.Read(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"extra\": true", StringComparison.Ordinal), Tenant));

        // A failed run cannot keep rows, a cancelled run cannot claim Collected, and a cut table must say partial.
        var failed = ScriptRunSchema.Create(request, Completed() with { End = ScriptRunEnd.ScriptFailed, ExitCode = 2, Failure = "The script stopped." }, "1.0.0-test");
        Assert.Equal(ReportReadState.Failed, failed.Status);
        failed.Rows.Add(new List<string> { "row1", "v" });
        Assert.Throws<ConfigurationException>(() => ScriptRunSchema.Seal(failed));
        var cancelled = ScriptRunSchema.Create(request, Completed() with { End = ScriptRunEnd.Cancelled, ExitCode = null, Failure = "Cancelled." }, "1.0.0-test");
        Assert.Equal(ReportReadState.Cancelled, cancelled.Status);
        cancelled.Status = ReportReadState.Collected;
        Assert.Throws<ConfigurationException>(() => ScriptRunSchema.Seal(cancelled));
        var cut = ScriptRunSchema.Create(request, Completed(new[] { "row1", "v" }) with { RowsTruncated = true }, "1.0.0-test");
        Assert.Equal(ReportReadState.Partial, cut.Status);
        cut.Partial = false;
        Assert.Throws<ConfigurationException>(() => ScriptRunSchema.Seal(cut));
        var wrongShape = ScriptRunSchema.Create(request, Completed(new[] { "row1", "v" }), "1.0.0-test");
        wrongShape.Rows[0].Add("extra");
        Assert.Throws<ConfigurationException>(() => ScriptRunSchema.Seal(wrongShape));
    }

    [Fact]
    public void The_csv_reader_accepts_what_export_csv_writes_and_refuses_anything_malformed()
    {
        Assert.Equal(new[] { new[] { "A", "B" }, new[] { "1", "x, \"y\"\nz" }, new[] { "", "" } },
            ScriptRunCsv.Parse("\"A\",\"B\"\r\n\"1\",\"x, \"\"y\"\"\nz\"\r\n\"\",\"\"\r\n"));
        Assert.Equal(new[] { new[] { "A", "B" }, new[] { "1", "2" } }, ScriptRunCsv.Parse("A,B\n1,2\n\n"));
        Assert.Throws<ConfigurationException>(() => ScriptRunCsv.Parse("\"A\",\"B\"\r\n\"1\"\r\n"));
        Assert.Throws<ConfigurationException>(() => ScriptRunCsv.Parse("\"A\",\"B\r\n"));
        Assert.Throws<ConfigurationException>(() => ScriptRunCsv.Parse("A,B\n1,x\"y\n"));
        Assert.Throws<ConfigurationException>(() => ScriptRunCsv.Parse("\"A\"x,B\n"));
    }

    /// <summary>Reports synchronously, so a test sees each message in order without a synchronisation context.</summary>
    private sealed class SyncProgress : IProgress<string>
    {
        private readonly List<string> _seen;
        private readonly Action<string>? _on;
        public SyncProgress(List<string> seen, Action<string>? on = null) { _seen = seen; _on = on; }
        public void Report(string value) { lock (_seen) _seen.Add(value); _on?.Invoke(value); }
    }
}
