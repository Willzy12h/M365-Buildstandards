using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Scripts;
using BDIT.TenantToolkit.Engine.Scripts.Runner;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// INT-088 slice 2: the owned read-only runner. Every run here launches a real PowerShell process with the engine's own
/// pinned wrapper, against a synthetic stand-in for ExchangeOnlineManagement (Fixtures/ReadRunner). The stand-in is
/// versioned 99.0.0 so it always outranks an installed module, and the module path is limited to it and PowerShell's own
/// modules: no Microsoft module is loaded, nobody signs in and no tenant is contacted. Each case runs on every PowerShell
/// found at its standard location: Windows PowerShell 5.1 and PowerShell 7 on Windows, PowerShell 7 elsewhere.
/// </summary>
public sealed class ExchangeReportRunnerTests
{
    private const string Account = "operator@synthetic.example";
    private const string ItemId = "exo.synthetic-mailbox-inventory";
    private static readonly ReadRunContext Context = new(TestData.TenantA, TestData.Operator, Account);
    private static readonly Dictionary<string, string?> NoArchive = new() { ["IncludeArchive"] = "false" };
    internal static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ReadRunner");

    // ---- fixtures ----------------------------------------------------------------------------------------------------

    internal static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static JsonObject Manifest(string scriptSha256, int timeoutSeconds = 600) => new()
    {
        ["schemaVersion"] = 2, ["id"] = ItemId, ["name"] = "Synthetic mailbox inventory", ["category"] = "exchange-online",
        ["area"] = "Mailboxes and calendars", ["description"] = "Synthetic runner regression item.", ["keywords"] = new JsonArray(),
        ["mode"] = "readOnly", ["scriptPath"] = "exchange-online/Get-SyntheticMailboxInventory.ps1", ["scriptSha256"] = scriptSha256,
        ["supportedRuntimes"] = new JsonArray("5.1", "7"),
        ["modules"] = new JsonArray(new JsonObject { ["name"] = "ExchangeOnlineManagement", ["minimumVersion"] = "3.7.0" }),
        ["roles"] = new JsonArray("View-Only Recipients"), ["scopes"] = new JsonArray(), ["resources"] = new JsonArray("exchangeOnline"),
        ["parameters"] = new JsonArray(new JsonObject { ["name"] = "IncludeArchive", ["label"] = "Include archives", ["type"] = "boolean", ["help"] = "Also read archive state." }),
        ["rules"] = new JsonArray(),
        ["outputSchema"] = new JsonObject { ["columns"] = new JsonArray(ExchangeReportEvidenceSchema.Columns(typeof(MailboxReportRow)).Select(c => (JsonNode?)c).ToArray()) },
        ["outputSchemaVersion"] = 1, ["limits"] = new JsonObject { ["maximumRows"] = 10000, ["timeoutSeconds"] = timeoutSeconds },
        ["prerequisites"] = "Synthetic.", ["liveStatus"] = "unverified", ["limitations"] = new JsonArray(),
        ["execution"] = new JsonObject
        {
            ["adapter"] = "exchangeOnline", ["reportId"] = "exo-mailbox-inventory", ["reportSchemaVersion"] = 1,
            ["outputKind"] = "scriptReadResult", ["outputSchemaVersion"] = 1, ["runnerTemplateSha256"] = ReadRunnerTemplate.Sha256
        }
    };

    /// <summary>A one-item schema 2 library. The callbacks change the manifest or registry before their pins are computed.</summary>
    internal static IReadOnlyDictionary<string, byte[]> Library(string? body = null, Action<JsonObject>? manifest = null,
        Action<JsonObject>? registry = null, int timeoutSeconds = 600)
    {
        var script = Encoding.ASCII.GetBytes(body ?? File.ReadAllText(Path.Combine(FixtureDirectory, "Get-SyntheticMailboxInventory.ps1")));
        var m = Manifest(Sha(script), timeoutSeconds);
        manifest?.Invoke(m);
        var manifestBytes = Encoding.UTF8.GetBytes(m.ToJsonString());
        var entry = new JsonObject
        {
            ["id"] = ItemId, ["manifest"] = "exchange-online/Get-SyntheticMailboxInventory.json", ["manifestSha256"] = Sha(manifestBytes),
            ["scriptSha256"] = Sha(script), ["manifestSchemaVersion"] = 2
        };
        var r = new JsonObject { ["schemaVersion"] = 2, ["scripts"] = new JsonArray(entry) };
        registry?.Invoke(r);
        return new Dictionary<string, byte[]>
        {
            ["registry.json"] = Encoding.UTF8.GetBytes(r.ToJsonString()),
            ["exchange-online/Get-SyntheticMailboxInventory.json"] = manifestBytes,
            ["exchange-online/Get-SyntheticMailboxInventory.ps1"] = script
        };
    }

    internal static ScriptEntry Entry(string? body = null, int timeoutSeconds = 600) =>
        ScriptCatalogue.Load(Library(body, timeoutSeconds: timeoutSeconds)).Find(ItemId);

    private static IReadOnlyList<(string Version, string Path)> FindRuntimes()
    {
        var found = new List<(string, string)>();
        if (OperatingSystem.IsWindows())
        {
            var ps51 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var ps7 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            if (File.Exists(ps51)) found.Add(("5.1", ps51));
            if (File.Exists(ps7)) found.Add(("7", ps7));
        }
        else
        {
            var pwsh = new[] { "/opt/microsoft/powershell/7/pwsh", "/usr/bin/pwsh", "/usr/local/bin/pwsh" }.FirstOrDefault(File.Exists);
            if (pwsh is not null) found.Add(("7", pwsh));
        }
        return found;
    }

    /// <summary>Every PowerShell this machine has. None at all fails the theory rather than skipping it.</summary>
    public static TheoryData<string> Runtimes()
    {
        var data = new TheoryData<string>();
        foreach (var (version, _) in FindRuntimes()) data.Add(version);
        return data;
    }

    private static string RuntimePath(string runtime) => FindRuntimes().Single(r => r.Version == runtime).Path;

    /// <summary>The synthetic module first, then only PowerShell's own modules. No user or machine module path.</summary>
    internal static Dictionary<string, string> FakeEnvironment(string runtime, string root, string scenario)
    {
        var path = RuntimePath(runtime);
        var home = Path.GetDirectoryName(new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path)!;
        var modules = new List<string> { Path.Combine(FixtureDirectory, "Modules"), Path.Combine(home, "Modules") };
        return new()
        {
            ["PSModulePath"] = string.Join(Path.PathSeparator, modules), ["BDIT_FAKE_SCENARIO"] = scenario,
            ["BDIT_FAKE_TENANT"] = TestData.TenantA, ["BDIT_FAKE_LOG"] = Path.Combine(root, "fake.log"),
            ["BDIT_FAKE_STAGING"] = Path.Combine(root, "staging")
        };
    }

    private sealed class Run : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "bdit-runner-" + Guid.NewGuid().ToString("N"));
        public Run() => Directory.CreateDirectory(Path.Combine(Root, "staging"));
        public string Log => File.Exists(Path.Combine(Root, "fake.log")) ? File.ReadAllText(Path.Combine(Root, "fake.log")) : "";
        public int Reads => Log.Split('\n').Count(l => l.StartsWith("Read ", StringComparison.Ordinal));
        public List<string> Progress { get; } = [];
        public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } }

        public Task<ReadRunResult> Execute(string runtime, string scenario, ScriptEntry? entry = null, IReadOnlyDictionary<string, string?>? inputs = null,
            Dictionary<string, string>? extra = null, string? origin = null, CancellationToken ct = default, Action<string>? onProgress = null)
        {
            var environment = FakeEnvironment(runtime, Root, scenario);
            foreach (var (key, value) in extra ?? []) environment[key] = value;
            var host = new ReadRunnerHost(_ => RuntimePath(runtime), environment, origin, Path.Combine(Root, "staging"));
            var progress = new SyncProgress(text => { lock (Progress) Progress.Add(text); onProgress?.Invoke(text); });
            return new ExchangeReportRunner(host, new SystemClock()).RunAsync(entry ?? Entry(), Context, inputs ?? NoArchive, runtime, progress, ct);
        }
    }

    /// <summary>Reports progress on the reporting thread, so a test can react to a marker at once.</summary>
    private sealed class SyncProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }

    // ---- the pinned wrapper ------------------------------------------------------------------------------------------

    [Fact]
    public void The_wrapper_matches_its_engine_pin_and_is_plain_ascii()
    {
        var bytes = ReadRunnerTemplate.Bytes();
        Assert.Equal(ReadRunnerTemplate.Sha256, Sha(bytes));
        Assert.All(bytes, b => Assert.True(b is (byte)'\n' or (byte)'\t' || b is >= 0x20 and <= 0x7E, "The wrapper must be plain ASCII for Windows PowerShell 5.1."));
        Assert.DoesNotContain((byte)'\r', bytes);
    }

    // ---- real runs against the synthetic module ----------------------------------------------------------------------

    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_read_only_run_seals_strict_evidence_from_the_verified_connection(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "happy");

        Assert.Equal(ReadRunOutcome.Completed, result.Outcome);
        var evidence = Assert.IsType<ExchangeReportEvidence>(result.Evidence);
        Assert.Equal(ReportReadState.Collected, evidence.Status);
        Assert.Equal("99.0.0", evidence.ModuleVersion);
        Assert.Equal(runtime == "5.1" ? "5.1" : "7", evidence.RuntimeVersion[..(runtime == "5.1" ? 3 : 1)]);
        Assert.Equal(Account, evidence.ObservedAccountUpn);
        Assert.Equal(TestData.Operator, evidence.InitiatingAccountObjectId);
        Assert.Equal(result.RunId, evidence.RunId);
        Assert.Equal(ReadRunnerTemplate.Sha256, evidence.RunnerTemplateSha256);
        Assert.Equal("false", Assert.Single(evidence.Parameters).Value);
        var rows = evidence.Sections.Single().Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Unlimited", rows[1]["prohibitSendQuotaRaw"]!.GetValue<string>());
        Assert.Null(rows[1]["externalDirectoryObjectId"]);
        // The connection was opened with the confirmed account as its hint and only the registered commands; three reads; no write.
        Assert.Contains("Connect " + Account + " Get-EXOMailbox,Get-EXOMailboxStatistics", run.Log);
        Assert.Equal(3, run.Reads);
        Assert.Contains("Disconnect", run.Log);
        Assert.DoesNotContain("WRITE", run.Log);
        Assert.Contains(run.Progress, p => p.StartsWith("Reading Get-EXOMailboxStatistics", StringComparison.Ordinal));
        // The record is ordinary strict evidence and stores like any other; the staging folder is gone.
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        store.SaveExchangeReport(evidence);
        Assert.Equal(evidence.Id, store.LoadExchangeReport(TestData.TenantA, evidence.Id)!.Id);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(run.Root, "staging")));
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task Archive_state_is_recorded_only_when_the_run_includes_archives(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "happy", inputs: new Dictionary<string, string?> { ["IncludeArchive"] = "true" });

        var evidence = Assert.IsType<ExchangeReportEvidence>(result.Evidence);
        Assert.Equal("true", evidence.Parameters.Single().Value);
        Assert.All(evidence.Sections.Single().Rows, r => Assert.False(r["hasArchive"]!.GetValue<bool>()));
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_failed_size_read_is_partial_and_never_zero(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "stats-fail");

        var evidence = Assert.IsType<ExchangeReportEvidence>(result.Evidence);
        Assert.Equal(ReportReadState.Partial, evidence.Status);
        var shared = evidence.Sections.Single().Rows.Single(r => r["mailboxType"]!.GetValue<string>() == "SharedMailbox");
        Assert.Equal(ReportReadState.Failed, shared["primarySizeReadStatus"]!.GetValue<string>());
        Assert.Null(shared["primarySizeRaw"]);
        Assert.Equal("The mailbox size could not be read.", shared["error"]!.GetValue<string>());
    }

    [Theory]
    [MemberData(nameof(IdentityCases))]
    public async Task Another_tenant_or_account_is_refused_before_any_read(string runtime, string scenario)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, scenario);

        Assert.Equal(ReadRunOutcome.Refused, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.Equal(0, run.Reads);
        Assert.Contains("another tenant or account", result.Reason, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> IdentityCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (runtime, _) in FindRuntimes())
            foreach (var scenario in new[] { "wrong-account", "wrong-tenant", "two-connections" }) data.Add(runtime, scenario);
        return data;
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_sign_in_failure_reads_nothing(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "connect-fails");

        Assert.Equal(ReadRunOutcome.Failed, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.Equal(0, run.Reads);
    }

    /// <summary>The gate rechecks after every read: an account change after the last read still keeps no row from the run.</summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task An_account_change_during_the_run_keeps_no_rows(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "account-changes");

        var evidence = Assert.IsType<ExchangeReportEvidence>(result.Evidence);
        Assert.Equal(ReportReadState.Failed, evidence.Status);
        Assert.Empty(evidence.Sections.Single().Rows);
        Assert.Contains("no longer matched", evidence.Sections.Single().Error, StringComparison.Ordinal);
        Assert.Equal(3, run.Reads);
    }

    /// <summary>The item requires a version above the stand-in's, so no installed module of any version can qualify.</summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_missing_module_is_reported_and_nothing_is_signed_in(string runtime)
    {
        using var run = new Run();
        var entry = ScriptCatalogue.Load(Library(manifest: m => m["modules"]![0]!["minimumVersion"] = "100.0.0")).Find(ItemId);
        var result = await run.Execute(runtime, "happy", entry);

        Assert.Equal(ReadRunOutcome.ModuleMissing, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.Equal("", run.Log);
    }

    /// <summary>Bodies that pass the library's text check but not the wrapper's syntax-tree check never reach sign-in.</summary>
    [Theory]
    [MemberData(nameof(WrapperRefusals))]
    public async Task The_wrapper_refuses_a_body_its_syntax_check_rejects_before_sign_in(string runtime, string body)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "happy", Entry(body));

        Assert.Equal(ReadRunOutcome.Refused, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.Equal("", run.Log);
    }

    public static TheoryData<string, string> WrapperRefusals()
    {
        const string read = "Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{}\n";
        var bodies = new[]
        {
            "param([bool]$IncludeArchive)\n" + read + "$x = 1\n$x | ForEach-Object ToString\n",
            "param([bool]$IncludeArchive)\n" + read + "$sb = { $true }\n1 | Where-Object -FilterScript $sb\n",
            "param([bool]$IncludeArchive)\n" + read + "$m = 'ToString'\n'x'.$m()\n",
            "param([bool]$IncludeArchive)\n" + read + "[IO.File]::Exists('x')\n",
            "param([bool]$IncludeArchive)\n" + read + "@(1).ForEach('ToString')\n",
            "param([bool]$IncludeArchive)\n" + read + "$e = { 1 }\n1 | Select-Object -Property @{ n = 'a'; e = $e }\n",
            "param([bool]$IncludeArchive)\n" + read + "$h = $PSCmdlet\n"
        };
        var data = new TheoryData<string, string>();
        foreach (var (runtime, _) in FindRuntimes()) foreach (var body in bodies) data.Add(runtime, body);
        return data;
    }

    /// <summary>
    /// The body runs in its own module scope, so the wrapper's variables (captured commands, request, module) are invisible
    /// to it. Here a body that tries to read one records only its own prefix.
    /// </summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task The_body_cannot_see_the_wrappers_variables(string runtime)
    {
        var body = File.ReadAllText(Path.Combine(FixtureDirectory, "Get-SyntheticMailboxInventory.ps1"))
            .Replace("name = [string]$mailbox.DisplayName", "name = 'seen:' + [string]$moduleName + [string]$sourceCommands + [string]$identity + [string]$Result + [string]$Stop + [string]$Request", StringComparison.Ordinal)
            // Strict mode would stop at the first missing variable; off, a missing one reads as empty and a visible one shows.
            .Replace("Set-StrictMode -Version Latest", "Set-StrictMode -Off", StringComparison.Ordinal);
        using var run = new Run();
        var result = await run.Execute(runtime, "happy", Entry(body));

        var evidence = Assert.IsType<ExchangeReportEvidence>(result.Evidence);
        Assert.All(evidence.Sections.Single().Rows, r => Assert.Equal("seen:", r["name"]!.GetValue<string>()));
    }

    /// <summary>The gate accepts only a read's own parameters: a common parameter or a script block is refused at the gate.</summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task The_gate_refuses_common_parameters_and_script_block_values(string runtime)
    {
        foreach (var parameters in new[] { "@{ OutVariable = 'x' }", "@{ Filter = { 1 } }" })
        {
            using var run = new Run();
            var result = await run.Execute(runtime, "happy", Entry("param([bool]$IncludeArchive)\nUse-BditRead -Command 'Get-EXOMailbox' -Parameters " + parameters + "\n"));
            Assert.Equal(ReadRunOutcome.Refused, result.Outcome);
            Assert.Equal(0, run.Reads);
        }
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_cooperative_stop_keeps_earlier_rows_only_as_cancelled(string runtime)
    {
        using var run = new Run();
        using var cts = new CancellationTokenSource();
        var result = await run.Execute(runtime, "slow-stats", extra: new() { ["BDIT_FAKE_COOPERATE"] = "1" }, ct: cts.Token,
            onProgress: p => { if (p.StartsWith("Reading Get-EXOMailboxStatistics", StringComparison.Ordinal)) cts.Cancel(); });

        Assert.Equal(ReadRunOutcome.Cancelled, result.Outcome);
        var evidence = Assert.IsType<ExchangeReportEvidence>(result.Evidence);
        Assert.Equal(ReportReadState.Cancelled, evidence.Status);
        Assert.Equal(ReportReadState.Cancelled, evidence.Sections.Single().Status);
        // The slow read returned, then the gate saw the stop: no further read was made.
        Assert.Equal(2, run.Reads);
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_read_that_ignores_the_stop_is_ended_after_the_grace_period(string runtime)
    {
        using var run = new Run();
        using var cts = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();
        var result = await run.Execute(runtime, "slow-stats", ct: cts.Token,
            onProgress: p => { if (p.StartsWith("Reading Get-EXOMailboxStatistics", StringComparison.Ordinal)) cts.Cancel(); });

        Assert.Equal(ReadRunOutcome.Cancelled, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(25), "The process tree must be ended shortly after the grace period.");
        Assert.DoesNotContain("Disconnect", run.Log);
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_run_past_its_time_limit_is_ended_and_keeps_no_rows(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "hang", Entry(timeoutSeconds: 10));

        Assert.Equal(ReadRunOutcome.TimedOut, result.Outcome);
        Assert.Null(result.Evidence);
    }

    [Theory, MemberData(nameof(Runtimes))]
    public async Task Progress_output_past_its_limit_ends_the_run(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "chatty");

        Assert.Equal(ReadRunOutcome.Failed, result.Outcome);
        Assert.Null(result.Evidence);
        Assert.Contains("1 MiB", result.Reason, StringComparison.Ordinal);
    }

    /// <summary>Diagnostics past 256 KiB are drained and discarded: the run is not blocked and not promoted or failed by them.</summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Excess_diagnostics_are_drained_without_blocking_the_run(string runtime)
    {
        using var run = new Run();
        var result = await run.Execute(runtime, "noisy-errors");

        Assert.Equal(ReadRunOutcome.Completed, result.Outcome);
        Assert.True(result.DiagnosticsTruncated);
        Assert.Equal(ReportReadState.Collected, result.Evidence!.Status);
    }

    [Fact]
    public async Task A_missing_runtime_is_reported_without_creating_anything()
    {
        using var run = new Run();
        var host = new ReadRunnerHost(_ => null, null, null, Path.Combine(run.Root, "staging"));
        var result = await new ExchangeReportRunner(host, new SystemClock()).RunAsync(Entry(), Context, NoArchive, "7", null, CancellationToken.None);

        Assert.Equal(ReadRunOutcome.RuntimeUnavailable, result.Outcome);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(run.Root, "staging")));
    }

    /// <summary>
    /// Without the start marker no runner start is established, whatever the exit code. On Windows the real host refuses the
    /// wrapper under a Restricted policy; elsewhere a host that exits without running anything stands in for it.
    /// </summary>
    [Fact]
    public async Task A_host_that_does_not_run_the_wrapper_is_host_refused()
    {
        using var run = new Run();
        ReadRunnerHost host;
        if (OperatingSystem.IsWindows())
        {
            var runtime = FindRuntimes()[0];
            var environment = FakeEnvironment(runtime.Version, run.Root, "happy");
            environment["PSExecutionPolicyPreference"] = "Restricted";
            host = new ReadRunnerHost(_ => runtime.Path, environment, null, Path.Combine(run.Root, "staging"));
        }
        else host = new ReadRunnerHost(_ => "/bin/false", null, null, Path.Combine(run.Root, "staging"));
        var result = await new ExchangeReportRunner(host, new SystemClock()).RunAsync(Entry(), Context, NoArchive, "7", null, CancellationToken.None);

        Assert.Equal(ReadRunOutcome.HostRefused, result.Outcome);
        Assert.Contains("security owner", result.Reason, StringComparison.Ordinal);
        Assert.Equal("", run.Log);
    }

    /// <summary>
    /// The materialised wrapper inherits the application's download marking. Under RemoteSigned an unmarked local wrapper
    /// runs, but one carrying an Internet marking is refused, which is what policy would do to the distribution itself.
    /// </summary>
    [Fact]
    public async Task The_wrapper_inherits_the_applications_download_marking()
    {
        using var run = new Run();
        var origin = Path.Combine(run.Root, "app.exe");
        File.WriteAllText(origin, "synthetic");
        if (!OperatingSystem.IsWindows())
        {
            // No alternate data streams here: nothing is inherited and nothing is invented.
            var target = Path.Combine(run.Root, "target.ps1");
            File.WriteAllText(target, "x");
            OriginMetadata.Inherit(origin, target);
            Assert.Equal(new[] { "app.exe", "staging", "target.ps1" }, Directory.EnumerateFileSystemEntries(run.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            return;
        }
        File.WriteAllText(origin + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        foreach (var (runtime, path) in FindRuntimes())
        {
            var environment = FakeEnvironment(runtime, run.Root, "happy");
            environment["PSExecutionPolicyPreference"] = "RemoteSigned";
            var unmarked = await new ExchangeReportRunner(new ReadRunnerHost(_ => path, environment, null, Path.Combine(run.Root, "staging")), new SystemClock())
                .RunAsync(Entry(), Context, NoArchive, runtime, null, CancellationToken.None);
            Assert.Equal(ReadRunOutcome.Completed, unmarked.Outcome);
            var marked = await new ExchangeReportRunner(new ReadRunnerHost(_ => path, environment, origin, Path.Combine(run.Root, "staging")), new SystemClock())
                .RunAsync(Entry(), Context, NoArchive, runtime, null, CancellationToken.None);
            Assert.Equal(ReadRunOutcome.HostRefused, marked.Outcome);
        }
    }

    // ---- the wrapper's own request binding, run directly ------------------------------------------------------------

    private const string TypedBody = """
        param([string]$Text, [datetime]$When, [long]$Count, [bool]$Flag)
        $seen = [string]::Join('|', [string[]]@(($Text -is [string]), $Text, ($When -is [datetime]), $When.ToString('yyyy-MM-ddTHH:mm:ss'), $When.Kind, ($Count -is [long]), $Count, ($Flag -is [bool]), $Flag))
        @{ Sections = @(@{ Id = 'mailboxes'; Status = 'Collected'; Error = $null; Rows = @(@{ id = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; name = $seen; readStatus = 'Collected'; error = $null; externalDirectoryObjectId = $null; primarySmtpAddress = $null; mailboxType = 'UserMailbox'; primarySizeReadStatus = 'NotAttempted'; primarySizeRaw = $null; quotaReadStatus = 'NotAttempted'; issueWarningQuotaRaw = $null; prohibitSendQuotaRaw = $null; prohibitSendReceiveQuotaRaw = $null; archiveReadStatus = 'NotAttempted'; hasArchive = $null; archiveSizeReadStatus = 'NotAttempted'; archiveSizeRaw = $null; archiveQuotaRaw = $null }) }) }
        """;

    private static string B64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private static JsonObject TypedRequest(string body) => new()
    {
        ["schemaVersion"] = 1, ["kind"] = "scriptReadRequest", ["runId"] = "99999999-9999-4999-8999-999999999999", ["scriptId"] = ItemId,
        ["manifestSha256"] = new string('a', 64), ["scriptSha256"] = Sha(Encoding.ASCII.GetBytes(body)), ["runnerTemplateSha256"] = ReadRunnerTemplate.Sha256,
        ["adapter"] = "exchangeOnline", ["reportId"] = "exo-mailbox-inventory", ["reportSchemaVersion"] = 1, ["tenantId"] = TestData.TenantA,
        ["expectedAccountUpn"] = Account, ["module"] = new JsonObject { ["name"] = "ExchangeOnlineManagement", ["minimumVersion"] = "3.7.0" },
        ["sourceCommands"] = new JsonArray("Get-EXOMailbox", "Get-EXOMailboxStatistics"),
        ["sections"] = new JsonArray(new JsonObject { ["id"] = "mailboxes", ["columns"] = new JsonArray(ExchangeReportEvidenceSchema.Columns(typeof(MailboxReportRow)).Select(c => (JsonNode?)c).ToArray()) }),
        ["maximumRows"] = 10000,
        ["parameters"] = new JsonArray(
            new JsonObject { ["name"] = "Text", ["type"] = "Text", ["bound"] = true, ["payload"] = B64("2026-10-10T08:00:00Z") },
            new JsonObject { ["name"] = "When", ["type"] = "Date", ["bound"] = true, ["payload"] = B64("2026-02-03") },
            new JsonObject { ["name"] = "Count", ["type"] = "Integer", ["bound"] = true, ["payload"] = 42 },
            new JsonObject { ["name"] = "Flag", ["type"] = "Boolean", ["bound"] = true, ["payload"] = true })
    };

    /// <summary>Runs the pinned wrapper by hand, as the runner would, with a request the test controls.</summary>
    private static async Task<(int Exit, string Output, JsonObject? Result)> Wrapper(string runtime, JsonObject request, string body,
        Func<byte[], string>? digest = null, Action<string>? afterStaging = null)
    {
        using var run = new Run();
        var directory = Path.Combine(run.Root, "staging");
        var requestBytes = Encoding.UTF8.GetBytes(request.ToJsonString());
        File.WriteAllBytes(Path.Combine(directory, "ReadRunner.ps1"), ReadRunnerTemplate.Bytes());
        File.WriteAllBytes(Path.Combine(directory, "body.ps1"), Encoding.ASCII.GetBytes(body));
        File.WriteAllBytes(Path.Combine(directory, "request.json"), requestBytes);
        afterStaging?.Invoke(directory);
        var start = new ProcessStartInfo(RuntimePath(runtime)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-NoLogo", "-NoProfile", "-File", Path.Combine(directory, "ReadRunner.ps1"), "-Request", Path.Combine(directory, "request.json"),
            "-RequestSha256", (digest ?? Sha)(requestBytes), "-Body", Path.Combine(directory, "body.ps1"), "-Result", Path.Combine(directory, "result.json"),
            "-Stop", Path.Combine(directory, "stop.flag") })
            start.ArgumentList.Add(a);
        foreach (var (key, value) in FakeEnvironment(runtime, run.Root, "happy")) start.Environment[key] = value;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(limit.Token);
        var resultFile = Path.Combine(directory, "result.json");
        return (process.ExitCode, await output, File.Exists(resultFile) ? JsonNode.Parse(File.ReadAllText(resultFile))!.AsObject() : null);
    }

    /// <summary>
    /// Text and dates travel as UTF-8 base64, so date-looking text stays text and a date is parsed only with its declared
    /// format as UTC. Integers and booleans keep their exact .NET types.
    /// </summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task Typed_values_bind_with_their_declared_types_and_no_culture_coercion(string runtime)
    {
        var (exit, output, result) = await Wrapper(runtime, TypedRequest(TypedBody), TypedBody);

        Assert.True(exit == 0, output);
        var name = result!["sections"]![0]!["rows"]![0]!["name"]!.GetValue<string>();
        Assert.Equal("True|2026-10-10T08:00:00Z|True|2026-02-03T00:00:00|Utc|True|42|True|True", name);
    }

    [Theory]
    [MemberData(nameof(MalformedRequests))]
    public async Task A_malformed_request_is_refused_before_anything_is_imported(string runtime, string change)
    {
        var request = TypedRequest(TypedBody);
        var parameters = request["parameters"]!.AsArray();
        Func<byte[], string>? digest = null;
        switch (change)
        {
            case "digest": digest = _ => new string('0', 64); break;
            case "integer-as-text": parameters[2]!["payload"] = "42"; break;
            case "boolean-as-text": parameters[3]!["payload"] = "true"; break;
            case "date-not-base64": parameters[1]!["payload"] = "2026-02-03"; break;
            case "date-invalid": parameters[1]!["payload"] = B64("2026-02-30"); break;
            case "date-with-time": parameters[1]!["payload"] = B64("2026-02-03T00:00:00Z"); break;
            case "text-invalid-utf8": parameters[0]!["payload"] = Convert.ToBase64String([0xC3, 0x28]); break;
            case "unknown-type": parameters[0]!["type"] = "Script"; break;
            case "duplicate": parameters.Add(parameters[0]!.DeepClone()); break;
            case "unbound-with-payload": parameters[0]!["bound"] = false; break;
            case "extra-field": request["outputDirectory"] = "C:\\"; break;
            case "extra-parameter-field": parameters[0]!["value"] = "x"; break;
            case "kind": request["kind"] = "scriptReadResult"; break;
            case "adapter": request["adapter"] = "purview"; break;
        }
        var (exit, output, result) = await Wrapper(runtime, request, TypedBody, digest);

        Assert.Equal(2, exit);
        Assert.Contains("BDIT:FAILED:RequestInvalid", output, StringComparison.Ordinal);
        Assert.DoesNotContain("BDIT:CONNECTING", output, StringComparison.Ordinal);
        Assert.Null(result);
    }

    public static TheoryData<string, string> MalformedRequests()
    {
        var data = new TheoryData<string, string>();
        foreach (var (runtime, _) in FindRuntimes())
            foreach (var change in new[] { "digest", "integer-as-text", "boolean-as-text", "date-not-base64", "date-invalid", "date-with-time", "text-invalid-utf8",
                "unknown-type", "duplicate", "unbound-with-payload", "extra-field", "extra-parameter-field", "kind", "adapter" })
                data.Add(runtime, change);
        return data;
    }

    /// <summary>
    /// Method calls are allow-listed, not deny-listed: reflection reaches any .NET type through members such as
    /// GetType, Assembly and InvokeMember, none of which looks dangerous by name. Each attempt here would write a file.
    /// </summary>
    [Theory]
    [MemberData(nameof(ReflectionEscapes))]
    public async Task Reflection_cannot_reach_dotnet_from_a_body(string runtime, string escape)
    {
        var target = Path.Combine(Path.GetTempPath(), "bdit-escape-" + Guid.NewGuid().ToString("N") + ".txt");
        var body = "param()\n" + escape.Replace("TARGET", target, StringComparison.Ordinal) + "\n";
        var request = TypedRequest(body);
        request["parameters"] = new JsonArray();
        var (exit, output, result) = await Wrapper(runtime, request, body);

        Assert.False(File.Exists(target), "A body reached System.IO.File.");
        Assert.Equal(2, exit);
        Assert.Contains("BDIT:FAILED:BodyRefused", output, StringComparison.Ordinal);
        Assert.Null(result);
    }

    public static TheoryData<string, string> ReflectionEscapes()
    {
        var escapes = new[]
        {
            "$f = 'x'.GetType().Assembly.GetType('System.IO.File')\n$null = $f.InvokeMember('WriteAllText', 'InvokeMethod, Static, Public', $null, $null, @('TARGET', 'x'))",
            "$f = 'x'.GetType().Assembly.GetType('System.IO.File')\n$null = $f.GetMethod('WriteAllText', [type[]]@([string], [string])).Invoke($null, @('TARGET', 'x'))",
            "$m = 'x'.GetType().Assembly.GetType('System.IO.File').GetMethods() | Where-Object { $_.Name -eq 'WriteAllText' } | Select-Object -First 1",
            "class Escape { static [void] Run() { [IO.File]::WriteAllText('TARGET', 'x') } }\n[Escape]::Run()",
            // No denied member at all: the assembly is read through reflection, so only the method allow-list stops it.
            "$a = [string].GetType().GetProperty('Assembly').GetValue([string])\n$null = $a.GetType('System.IO.File').InvokeMember('WriteAllText', 'InvokeMethod, Static, Public', $null, $null, @('TARGET', 'x'))",
            "#requires -Modules Microsoft.PowerShell.Management\n$null = 1"
        };
        var data = new TheoryData<string, string>();
        foreach (var (runtime, _) in FindRuntimes()) foreach (var escape in escapes) data.Add(runtime, escape);
        return data;
    }

    /// <summary>A body changed after the parent pinned it is never parsed or run.</summary>
    [Theory, MemberData(nameof(Runtimes))]
    public async Task A_body_changed_after_staging_is_refused(string runtime)
    {
        var (exit, output, result) = await Wrapper(runtime, TypedRequest(TypedBody), TypedBody,
            afterStaging: directory => File.AppendAllText(Path.Combine(directory, "body.ps1"), "\nSet-Mailbox -Identity x\n"));

        Assert.Equal(2, exit);
        Assert.Contains("BDIT:FAILED:BodyChanged", output, StringComparison.Ordinal);
        Assert.Null(result);
    }

    // ---- checks before anything is created ---------------------------------------------------------------------------

    [Fact]
    public async Task Copy_only_items_and_unverified_contexts_are_refused_before_any_file_or_process()
    {
        using var run = new Run();
        var host = new ReadRunnerHost(_ => throw new InvalidOperationException("No runtime may be looked up."), null, null, Path.Combine(run.Root, "staging"));
        var runner = new ExchangeReportRunner(host, new SystemClock());
        var copyOnly = ScriptCatalogue.Shipped.Entries.First();
        await Assert.ThrowsAsync<ConfigurationException>(() => runner.RunAsync(copyOnly, Context, new Dictionary<string, string?>(), "7", null, CancellationToken.None));
        foreach (var context in new[]
        {
            Context with { TenantId = "{" + TestData.TenantA + "}" },
            Context with { TenantId = TestData.TenantB.Replace("-", "") },
            Context with { AccountObjectId = "" },
            Context with { AccountUpn = "Operator Display Name" },
            Context with { AccountUpn = " " + Account }
        })
            await Assert.ThrowsAsync<ConfigurationException>(() => runner.RunAsync(Entry(), context, NoArchive, "7", null, CancellationToken.None));
        await Assert.ThrowsAsync<ConfigurationException>(() => runner.RunAsync(Entry(), Context, new Dictionary<string, string?> { ["Script"] = "x" }, "7", null, CancellationToken.None));
        await Assert.ThrowsAsync<ConfigurationException>(() => runner.RunAsync(Entry(), Context, NoArchive, "6", null, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(run.Root, "staging")));
    }

    [Fact]
    public void A_schema_2_item_has_no_copy_form()
    {
        var entry = Entry();
        var binding = ScriptInputs.Bind(entry.Manifest, NoArchive, DateTimeOffset.UtcNow);
        Assert.Throws<ConfigurationException>(() => ScriptCopy.Generate(entry, binding, new ScriptCopyTarget(TestData.TenantA, "Synthetic", Account), DateTimeOffset.UtcNow));
    }

    // ---- the strict result reader ------------------------------------------------------------------------------------

    private static readonly DateTimeOffset Launched = DateTimeOffset.Parse("2026-10-10T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static (ScriptEntry Entry, IReadOnlyList<ReadRunParameter> Parameters, JsonObject Envelope) Envelope()
    {
        var entry = Entry();
        var parameters = ScriptReadRequest.Bind(entry, Context, NoArchive, Launched);
        var row = ExchangeReportEvidenceSchema.Row(new MailboxReportRow
        {
            Id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", Name = "Synthetic user", ReadStatus = ReportReadState.Collected,
            MailboxType = "UserMailbox", PrimarySizeReadStatus = ReportReadState.Collected, PrimarySizeRaw = "1 GB",
            QuotaReadStatus = ReportReadState.Collected, IssueWarningQuotaRaw = "Unlimited", ProhibitSendQuotaRaw = "Unlimited", ProhibitSendReceiveQuotaRaw = "Unlimited"
        });
        var envelope = new JsonObject
        {
            ["schemaVersion"] = 1, ["kind"] = "scriptReadResult", ["runId"] = "99999999-9999-4999-8999-999999999999", ["scriptId"] = entry.Manifest.Id,
            ["manifestSha256"] = entry.ManifestSha256, ["scriptSha256"] = entry.Manifest.ScriptSha256, ["runnerTemplateSha256"] = ReadRunnerTemplate.Sha256,
            ["adapter"] = "exchangeOnline", ["reportId"] = "exo-mailbox-inventory", ["reportSchemaVersion"] = 1, ["tenantId"] = TestData.TenantA,
            ["observedAccountUpn"] = "OPERATOR@synthetic.example", ["startedAt"] = "2026-10-10T01:00:01.000Z", ["endedAt"] = "2026-10-10T01:00:05.000Z",
            ["runtimeVersion"] = "5.1.26100.1", ["moduleVersion"] = "99.0.0",
            ["parameters"] = new JsonArray(parameters.Select(ScriptReadRequest.Parameter).ToArray()),
            ["sections"] = new JsonArray(new JsonObject { ["id"] = "mailboxes", ["status"] = "Collected", ["error"] = null, ["limitations"] = new JsonArray(), ["rows"] = new JsonArray(row) })
        };
        return (entry, parameters, envelope);
    }

    private static ScriptReadResult ReadEnvelope(JsonObject envelope, ScriptEntry entry, IReadOnlyList<ReadRunParameter> parameters, string? raw = null) =>
        ScriptReadResult.Read(Encoding.UTF8.GetBytes(raw ?? envelope.ToJsonString()), "99999999-9999-4999-8999-999999999999", entry, Context, parameters,
            Launched, Launched.AddMinutes(1));

    [Fact]
    public void A_matching_envelope_is_accepted_with_the_observed_account_case_preserved()
    {
        var (entry, parameters, envelope) = Envelope();
        var result = ReadEnvelope(envelope, entry, parameters);
        Assert.Equal("OPERATOR@synthetic.example", result.ObservedAccountUpn);
        Assert.Single(result.Sections.Single().Rows);
    }

    [Theory]
    [InlineData("runId", "88888888-8888-4888-8888-888888888888")]
    [InlineData("scriptId", "exo.other")]
    [InlineData("manifestSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("scriptSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("runnerTemplateSha256", "0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("adapter", "purview")]
    [InlineData("reportId", "exo-other")]
    [InlineData("tenantId", "22222222-2222-4222-8222-222222222222")]
    [InlineData("observedAccountUpn", "someone.else@synthetic.example")]
    [InlineData("observedAccountUpn", " operator@synthetic.example")]
    [InlineData("observedAccountUpn", null)]
    [InlineData("kind", "exchangeReportEvidence")]
    [InlineData("startedAt", "2026-10-10T00:50:00.000Z")]
    [InlineData("endedAt", "2026-10-10T01:10:00.000Z")]
    [InlineData("endedAt", "2026-10-10T01:00:00.500Z")]
    [InlineData("startedAt", "2026-10-10T01:00:01.000+00:00")]
    [InlineData("moduleVersion", "3.6.0")]
    [InlineData("moduleVersion", "")]
    [InlineData("runtimeVersion", "7\n4")]
    public void A_forged_or_mismatched_envelope_is_refused(string field, string? value)
    {
        var (entry, parameters, envelope) = Envelope();
        envelope[field] = value;
        Assert.Throws<ConfigurationException>(() => ReadEnvelope(envelope, entry, parameters));
    }

    [Fact]
    public void The_envelope_must_repeat_the_request_and_registration_exactly()
    {
        void Refused(Action<JsonObject> change)
        {
            var (entry, parameters, envelope) = Envelope();
            change(envelope);
            Assert.Throws<ConfigurationException>(() => ReadEnvelope(envelope, entry, parameters));
        }
        Refused(e => e["parameters"]![0]!["payload"] = true);
        Refused(e => e["parameters"]![0]!["bound"] = false);
        Refused(e => e["parameters"]!.AsArray().Add(new JsonObject { ["name"] = "Script", ["type"] = "Text", ["bound"] = true, ["payload"] = "eA==" }));
        Refused(e => e["sections"]![0]!["id"] = "Mailboxes");
        Refused(e => e["sections"]!.AsArray().Add(new JsonObject { ["id"] = "extra", ["status"] = "Collected", ["error"] = null, ["limitations"] = new JsonArray(), ["rows"] = new JsonArray() }));
        Refused(e => e["sections"] = new JsonArray());
        Refused(e => e["schemaVersion"] = 2);
        Refused(e => e["script"] = "Set-Mailbox");
        Refused(e => e.Remove("moduleVersion"));
        Refused(e => e["sections"]![0]!["rawOutput"] = "x");
    }

    /// <summary>
    /// AST-20261010-02: <c>[JsonRequired]</c> needs a member to be present, not non-null. An explicit null collection, a null
    /// element or a null required text member must be the same controlled refusal as any other malformed envelope, never an
    /// ordinary exception that escapes the runner's decision.
    /// </summary>
    public static TheoryData<string> NullMembers()
    {
        var data = new TheoryData<string>();
        foreach (var name in new[] { "kind", "runId", "scriptId", "manifestSha256", "scriptSha256", "runnerTemplateSha256", "adapter", "reportId",
            "tenantId", "startedAt", "endedAt", "runtimeVersion", "moduleVersion", "parameters", "sections" })
            data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(NullMembers))]
    public void An_explicit_null_member_is_a_controlled_refusal(string field)
    {
        var (entry, parameters, envelope) = Envelope();
        envelope[field] = null;
        Assert.Throws<ConfigurationException>(() => ReadEnvelope(envelope, entry, parameters));
    }

    [Theory]
    [InlineData("parameters: [null]")]
    [InlineData("parameters: [..., null]")]
    [InlineData("sections: [null]")]
    [InlineData("section id: null")]
    [InlineData("section status: null")]
    [InlineData("section limitations: null")]
    [InlineData("section limitations: [null]")]
    [InlineData("section rows: null")]
    [InlineData("section rows: [null]")]
    public void A_null_element_or_null_nested_member_is_a_controlled_refusal(string change)
    {
        var (entry, parameters, envelope) = Envelope();
        var section = envelope["sections"]![0]!;
        switch (change)
        {
            case "parameters: [null]": envelope["parameters"] = new JsonArray((JsonNode?)null); break;
            case "parameters: [..., null]": envelope["parameters"]!.AsArray().Add(null); break;
            case "sections: [null]": envelope["sections"] = new JsonArray((JsonNode?)null); break;
            case "section id: null": section["id"] = null; break;
            case "section status: null": section["status"] = null; break;
            case "section limitations: null": section["limitations"] = null; break;
            case "section limitations: [null]": section["limitations"] = new JsonArray((JsonNode?)null); break;
            case "section rows: null": section["rows"] = null; break;
            case "section rows: [null]": section["rows"] = new JsonArray((JsonNode?)null); break;
            default: throw new ArgumentOutOfRangeException(nameof(change));
        }
        Assert.Throws<ConfigurationException>(() => ReadEnvelope(envelope, entry, parameters));
    }

    [Fact]
    public void A_section_error_may_be_null_and_the_matching_envelope_still_seals()
    {
        var (entry, parameters, envelope) = Envelope();
        var result = ReadEnvelope(envelope, entry, parameters);
        Assert.Null(result.Sections.Single().Error);
        Assert.Equal(ReportReadState.Collected, result.Sections.Single().Status);
    }

    [Fact]
    public void Duplicate_properties_invalid_utf8_and_oversize_results_are_refused()
    {
        var (entry, parameters, envelope) = Envelope();
        var json = envelope.ToJsonString();
        Assert.Throws<ConfigurationException>(() => ReadEnvelope(envelope, entry, parameters, "{\"tenantId\":\"" + TestData.TenantB + "\"," + json[1..]));
        Assert.Throws<ConfigurationException>(() => ScriptReadResult.Read([0x7B, 0xC3, 0x28, 0x7D], "99999999-9999-4999-8999-999999999999", entry, Context, parameters, Launched, Launched.AddMinutes(1)));
        Assert.Throws<ConfigurationException>(() => ScriptReadResult.Read(new byte[ReadRunnerLimits.MaximumResultBytes + 1], "99999999-9999-4999-8999-999999999999", entry, Context, parameters, Launched, Launched.AddMinutes(1)));
    }
}
