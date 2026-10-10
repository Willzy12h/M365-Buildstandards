using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.Engine.Scripts.Runner;

/// <summary>How a run ended. Only <see cref="Completed"/> and a cooperative <see cref="Cancelled"/> can carry evidence.</summary>
public enum ReadRunOutcome
{
    /// <summary>The wrapper returned a strict envelope; the evidence states its own Collected/Partial/Failed sections.</summary>
    Completed,
    /// <summary>No start marker: PowerShell or a security control did not run the wrapper. Nothing was read.</summary>
    HostRefused,
    /// <summary>The approved PowerShell runtime is not installed where the toolkit looks for it.</summary>
    RuntimeUnavailable,
    /// <summary>The required module is not installed at the required version. Nothing was signed in or read.</summary>
    ModuleMissing,
    /// <summary>The wrapper refused the request, the body or the connection before or during reading. No rows are kept.</summary>
    Refused,
    /// <summary>The run failed or returned output that was refused. No rows are kept.</summary>
    Failed,
    /// <summary>The engineer stopped the run.</summary>
    Cancelled,
    /// <summary>The run exceeded its time limit and was ended.</summary>
    TimedOut
}

/// <summary>The outcome of one run. <see cref="Reason"/> is a fixed, sanitised description, never raw module output.</summary>
public sealed record ReadRunResult(string RunId, ReadRunOutcome Outcome, string Reason, ExchangeReportEvidence? Evidence, bool DiagnosticsTruncated);

/// <summary>Where the runner finds PowerShell and which file's download origin the materialised files inherit.</summary>
internal sealed record ReadRunnerHost(Func<string, string?> RuntimePath, IReadOnlyDictionary<string, string>? Environment, string? OriginSource, string StagingRoot)
{
    /// <summary>The fixed system locations only: Windows PowerShell 5.1 and machine-wide PowerShell 7. No PATH lookup.</summary>
    public static ReadRunnerHost Default { get; } = new(DefaultRuntime, null, System.Environment.ProcessPath, Path.GetTempPath());

    private static string? DefaultRuntime(string runtime)
    {
        if (!OperatingSystem.IsWindows()) return null;
        return runtime switch
        {
            "5.1" => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            "7" => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
            _ => null
        };
    }
}

/// <summary>
/// INT-088 owned read-only runner. Runs one registered schema 2 library item in a fresh PowerShell process through the
/// engine's own pinned wrapper, then seals the strict result as <c>exchangeReportEvidence</c>. Nothing here writes to a
/// tenant, installs a module, changes script policy, retries, or keeps output that failed any check. Whether Run is offered
/// at all is the caller's experimental opt-in; this class adds no gate of its own and is not that permission.
/// </summary>
public sealed class ExchangeReportRunner
{
    private readonly ReadRunnerHost _host;
    private readonly IClock _clock;

    public ExchangeReportRunner(IClock clock) : this(ReadRunnerHost.Default, clock) { }
    internal ExchangeReportRunner(ReadRunnerHost host, IClock clock) { _host = host; _clock = clock; }

    public async Task<ReadRunResult> RunAsync(ScriptEntry entry, ReadRunContext context, IReadOnlyDictionary<string, string?> inputs,
        string runtime, IProgress<string>? progress, CancellationToken ct)
    {
        // Everything is checked before a file is created or a process started.
        var m = entry.Manifest;
        if (m.SchemaVersion != 2 || m.Mode != ScriptMode.ReadOnly || m.Execution is null)
            throw new ConfigurationException($"{m.Name} is a Copy-only item. Only reviewed schema 2 read-only items can run.");
        var report = ScriptCatalogue.ReportFor(m);
        if (!m.SupportedRuntimes.Contains(runtime, StringComparer.Ordinal)) throw new ConfigurationException($"{m.Name} is not reviewed for PowerShell {runtime}.");
        var parameters = ScriptRequestOrThrow(entry, context, inputs);
        var runId = Guid.NewGuid().ToString("D");
        var runtimePath = _host.RuntimePath(runtime);
        if (runtimePath is null || !File.Exists(runtimePath))
            return new(runId, ReadRunOutcome.RuntimeUnavailable, $"PowerShell {runtime} was not found at its standard location. Install it through your organisation's approved process.", null, false);

        var wrapper = ReadRunnerTemplate.Bytes();
        var body = Encoding.ASCII.GetBytes(entry.Script);
        if (Sha256(body) != m.ScriptSha256) throw new IntegrityException("The library script no longer matches its pinned SHA-256.");
        var request = ScriptReadRequest.Create(runId, entry, context, parameters);

        var staging = Path.Combine(_host.StagingRoot, "BDIT-Run-" + Guid.Parse(runId).ToString("N"));
        if (Directory.Exists(staging) || File.Exists(staging)) throw new ConfigurationException("The run's staging folder already exists.");
        Directory.CreateDirectory(staging);
        try
        {
            var files = new
            {
                Wrapper = Path.Combine(staging, "ReadRunner.ps1"), Body = Path.Combine(staging, "body.ps1"),
                Request = Path.Combine(staging, "request.json"), Result = Path.Combine(staging, "result.json"),
                Stop = Path.Combine(staging, "stop-" + runId + ".flag")
            };
            CreateNew(files.Wrapper, wrapper); CreateNew(files.Body, body); CreateNew(files.Request, request);
            OriginMetadata.Inherit(_host.OriginSource, files.Wrapper, files.Body);
            // Re-read immediately before launch: a file changed after it was written is never executed.
            if (Sha256(File.ReadAllBytes(files.Wrapper)) != ReadRunnerTemplate.Sha256 || Sha256(File.ReadAllBytes(files.Body)) != m.ScriptSha256
                || !File.ReadAllBytes(files.Request).AsSpan().SequenceEqual(request))
                throw new IntegrityException("A staged run file changed before launch. Nothing was run.");

            var start = new ProcessStartInfo(runtimePath)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = false };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", files.Wrapper, "-Request", files.Request,
                "-RequestSha256", Sha256(request), "-Body", files.Body, "-Result", files.Result, "-Stop", files.Stop })
                start.ArgumentList.Add(argument);
            if (_host.Environment is not null) foreach (var (key, value) in _host.Environment) start.Environment[key] = value;

            var markers = new MarkerReader(progress, report.SourceCommands);
            long diagnosticBytes = 0;
            var launchedAt = _clock.UtcNow;
            var outcome = await OwnedProcess.RunAsync(start, markers.ReadAsync,
                async reader =>
                {
                    var buffer = new char[4096]; int count;
                    while ((count = await reader.ReadAsync(buffer)) != 0) Interlocked.Add(ref diagnosticBytes, count);
                },
                TimeSpan.FromSeconds(Math.Min(m.Limits.TimeoutSeconds, ReadRunnerLimits.MaximumTimeoutSeconds)),
                () => markers.Overflowed,
                () => { try { using var _ = new FileStream(files.Stop, FileMode.CreateNew, FileAccess.Write); } catch (IOException) { } },
                ReadRunnerLimits.StopGrace, ct);
            var finishedAt = _clock.UtcNow;
            var truncated = Interlocked.Read(ref diagnosticBytes) > ReadRunnerLimits.MaximumDiagnosticBytes;
            return Decide(runId, entry, report, context, parameters, outcome, markers, files.Result, launchedAt, finishedAt, truncated);
        }
        finally
        {
            // Only the folder this run created is removed; a leftover is never read by a later run.
            try { Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private IReadOnlyList<ReadRunParameter> ScriptRequestOrThrow(ScriptEntry entry, ReadRunContext context, IReadOnlyDictionary<string, string?> inputs) =>
        ScriptReadRequest.Bind(entry, context, inputs, _clock.UtcNow);

    private static ReadRunResult Decide(string runId, ScriptEntry entry, ExchangeReportRegistry.Definition report, ReadRunContext context,
        IReadOnlyList<ReadRunParameter> parameters, OwnedProcessOutcome outcome, MarkerReader markers, string resultFile,
        DateTimeOffset launchedAt, DateTimeOffset finishedAt, bool truncated)
    {
        ReadRunResult Result(ReadRunOutcome o, string reason, ExchangeReportEvidence? evidence = null) => new(runId, o, reason, evidence, truncated);

        if (outcome.End == OwnedProcessEnd.NotStarted)
            return Result(ReadRunOutcome.HostRefused, HostRefusedReason);
        if (!markers.Started)
        {
            // Without the start marker no admitted runner start is established, whatever the exit code says.
            return outcome.End switch
            {
                OwnedProcessEnd.Cancelled => Result(ReadRunOutcome.Cancelled, "The run was stopped before the runner started. Nothing was read."),
                OwnedProcessEnd.TimedOut => Result(ReadRunOutcome.TimedOut, "PowerShell did not start the runner within the time limit. Nothing was read."),
                _ => Result(ReadRunOutcome.HostRefused, HostRefusedReason)
            };
        }
        if (outcome.End == OwnedProcessEnd.LimitExceeded)
            return Result(ReadRunOutcome.Failed, "The run wrote more progress output than the 1 MiB limit and was ended. No rows were kept.");
        if (outcome.End == OwnedProcessEnd.OutputHeldOpen)
            return Result(ReadRunOutcome.Failed, "PowerShell ended but another process kept its output open, so the run could not be confirmed. No rows were kept.");

        // A complete strict envelope returned before the process ended is the only source of rows.
        ExchangeReportEvidence? Evidence(string? downgradeTo, string? reason)
        {
            if (!File.Exists(resultFile)) return null;
            var bytes = ReadBounded(resultFile);
            var envelope = ScriptReadResult.Read(bytes, runId, entry, context, parameters, launchedAt, finishedAt);
            var sections = envelope.Sections;
            if (downgradeTo is not null)
                foreach (var section in sections.Where(s => s.Status is ReportReadState.Collected or ReportReadState.Partial))
                { section.Status = downgradeTo; section.Error = reason; }
            var evidence = new ExchangeReportEvidence
            {
                Id = Guid.NewGuid().ToString("D"), RunId = runId, ReportId = report.Id, TenantId = context.TenantId,
                InitiatingAccountObjectId = context.AccountObjectId, ObservedAccountUpn = envelope.ObservedAccountUpn, Resource = report.Resource,
                SourceMode = "live", StartedAt = envelope.StartedAt, EndedAt = envelope.EndedAt, ToolkitVersion = ToolkitVersion.Current,
                AdapterVersion = ExchangeReportRegistry.AdapterVersion, ModuleVersion = envelope.ModuleVersion, RuntimeVersion = envelope.RuntimeVersion,
                ScriptSha256 = entry.Manifest.ScriptSha256, ManifestSha256 = entry.ManifestSha256, RunnerTemplateSha256 = ReadRunnerTemplate.Sha256,
                Parameters = parameters.Select(p => new ExchangeReportParameter { Name = p.Name, Type = p.Type, Value = p.Value }).ToList(),
                SourceCommands = [.. report.SourceCommands], Limitations = [report.Limitations], Sections = sections,
                Status = ReportEvidenceSchema.Overall(sections.Select(s => s.Status))
            };
            ExchangeReportEvidenceSchema.Seal(evidence);
            return evidence;
        }

        try
        {
            switch (outcome.End)
            {
                case OwnedProcessEnd.TimedOut:
                    return Result(ReadRunOutcome.TimedOut, "The run exceeded its time limit and was ended. Rows returned before then are kept only as partial.",
                        Evidence(ReportReadState.Partial, "The run exceeded its time limit; this section may be incomplete."));
                case OwnedProcessEnd.Cancelled:
                    return Result(ReadRunOutcome.Cancelled, "The run was stopped. Rows read before the stop are kept only as cancelled.",
                        Evidence(ReportReadState.Cancelled, "The engineer stopped the run before the read finished; later rows were not read."));
            }
            if (outcome.ExitCode == 3 || markers.ModuleMissing)
                return Result(ReadRunOutcome.ModuleMissing, ModuleGuidance);
            // A non-zero exit is never promoted to success, even if a plausible result file exists.
            if (outcome.ExitCode != 0 || markers.Failure is not null || !markers.Done)
                return Result(markers.Failure is "RequestInvalid" or "BodyChanged" or "BodyRefused" or "IdentityMismatch" ? ReadRunOutcome.Refused : ReadRunOutcome.Failed,
                    FailureReason(markers.Failure));
            var evidence = Evidence(null, null);
            return evidence is null
                ? Result(ReadRunOutcome.Failed, "The runner finished without a result. No rows were kept.")
                : Result(ReadRunOutcome.Completed, "The run finished. The report states which sections were read in full.", evidence);
        }
        catch (ToolkitException)
        {
            // An envelope cut short by a stop or a time limit is not a forgery, but its rows are still not kept.
            return outcome.End switch
            {
                OwnedProcessEnd.Cancelled => Result(ReadRunOutcome.Cancelled, "The run was stopped before it returned a complete result. No rows were kept."),
                OwnedProcessEnd.TimedOut => Result(ReadRunOutcome.TimedOut, "The run exceeded its time limit before it returned a complete result. No rows were kept."),
                _ => Result(ReadRunOutcome.Failed, "The runner's result did not match the registered report, run, tenant or account. No rows were kept.")
            };
        }
    }

    internal const string HostRefusedReason = "PowerShell did not start the toolkit's runner. A script execution policy or application control may have refused it. "
        + "Ask your security owner whether this read-only runner may be approved. The toolkit does not change policy, remove download markings or try another host.";
    internal const string ModuleGuidance = "The ExchangeOnlineManagement module is not installed at the required version. Install it through your organisation's "
        + "approved PowerShell process; the toolkit does not install modules. Nothing was signed in or read.";

    private static string FailureReason(string? code) => code switch
    {
        "RequestInvalid" or "BodyChanged" => "The run request or script changed after it was checked. Nothing was read.",
        "BodyRefused" => "The script did not pass the runner's read-only check, so it was stopped and none of its output was kept.",
        "ModuleUnavailable" => "The ExchangeOnlineManagement module could not be loaded, already had a connection, or did not provide the expected commands. Nothing was read.",
        "ConnectFailed" => "Exchange Online sign-in did not complete. Nothing was read.",
        "IdentityMismatch" => "Exchange Online connected to another tenant or account, or more than one connection was open. Nothing was read.",
        "BodyFailed" => "The script stopped with an error before it returned a result. No rows were kept.",
        "OutputInvalid" => "The script's result did not match the registered report. No rows were kept.",
        _ => "The runner did not finish. No rows were kept."
    };

    private static byte[] ReadBounded(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ReadRunnerLimits.MaximumResultBytes) throw new ConfigurationException("The runner's result is larger than 32 MiB.");
        using var reader = new BinaryReader(stream);
        var bytes = reader.ReadBytes(ReadRunnerLimits.MaximumResultBytes + 1);
        if (bytes.Length > ReadRunnerLimits.MaximumResultBytes) throw new ConfigurationException("The runner's result is larger than 32 MiB.");
        return bytes;
    }

    private static void CreateNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Reads fixed marker lines from standard output and keeps nothing else. The first line must be the start marker. More
    /// than 1 MiB of output in total marks the run as overflowed; the owned process is then ended.
    /// </summary>
    private sealed class MarkerReader(IProgress<string>? progress, IReadOnlyList<string> commands)
    {
        private long _bytes;
        private int _lines;
        public bool Started { get; private set; }
        public bool Done { get; private set; }
        public bool ModuleMissing { get; private set; }
        public string? Failure { get; private set; }
        public bool Overflowed => Interlocked.Read(ref _bytes) > ReadRunnerLimits.MaximumProgressBytes;

        public async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[4096]; var line = new StringBuilder(); var oversized = false; int count;
            while ((count = await reader.ReadAsync(buffer)) != 0)
            {
                Interlocked.Add(ref _bytes, count);
                for (var i = 0; i < count; i++)
                {
                    var c = buffer[i];
                    if (c == '\n')
                    {
                        if (!oversized) Line(line.ToString().TrimEnd('\r'));
                        line.Clear(); oversized = false; _lines++;
                    }
                    else if (!oversized && line.Length < 256) line.Append(c);
                    else oversized = true;
                }
            }
        }

        private void Line(string text)
        {
            if (_lines == 0) { Started = text.TrimStart('\uFEFF') == "BDIT:STARTED"; return; }
            if (!Started) return;
            if (text == "BDIT:DONE") Done = true;
            else if (text == "BDIT:MODULE_MISSING") { ModuleMissing = true; progress?.Report(ModuleGuidance); }
            else if (text == "BDIT:CONNECTING") progress?.Report("Opening Microsoft sign-in for Exchange Online in a separate PowerShell process. The tenant and account must match before anything is read.");
            else if (text.StartsWith("BDIT:FAILED:", StringComparison.Ordinal)) Failure ??= text[12..];
            else if (text.StartsWith("BDIT:READ:", StringComparison.Ordinal) && commands.Contains(text[10..], StringComparer.Ordinal))
                progress?.Report("Reading " + text[10..] + " (read-only).");
        }
    }
}
