using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Exchange;

namespace BDIT.TenantToolkit.Engine.Scripts;

/// <summary>
/// The PowerShell a run uses. The desktop resolves it with <see cref="ScriptRunner.ResolveHost"/>: Windows PowerShell 5.1
/// or PowerShell 7 at their fixed install locations, never a PATH lookup. <see cref="Environment"/> is only for the
/// synthetic tests and harness (a stand-in module path); the desktop never sets it.
/// </summary>
public sealed record ScriptRunHost(string Executable, string Runtime, IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>
/// What to run: a registered read-only item, the engine's binding of its form, and the tenant and account it is pinned to.
/// Run always pins both: the wrapper refuses any other tenant or signed-in account before it reads anything.
/// </summary>
public sealed record ScriptRunRequest(ScriptEntry Entry, ScriptBinding Binding, ScriptCopyTarget Target, DateTimeOffset Now);

/// <summary>Bounds for one run. A timeout here can only shorten the manifest's own limit; byte limits can only be lowered.</summary>
public sealed record ScriptRunLimits(TimeSpan? Timeout = null, long MaximumCsvBytes = ScriptRunLimits.DefaultCsvBytes, long MaximumOutputBytes = ScriptRunLimits.DefaultOutputBytes)
{
    public const long DefaultCsvBytes = 8L * 1024 * 1024;
    public const long DefaultOutputBytes = 1L * 1024 * 1024;
}

/// <summary>How a run ended. Only <see cref="Completed"/> keeps rows.</summary>
public enum ScriptRunEnd { Completed, ScriptFailed, ModuleMissing, HostRefused, TimedOut, Cancelled, OutputLimit, InvalidOutput }

/// <summary>What a run produced. <see cref="Status"/> uses the INT-071 read states: Collected, Partial, Failed or Cancelled.</summary>
public sealed record ScriptRunResult(
    string Runtime,
    string WrapperSha256,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    ScriptRunEnd End,
    int? ExitCode,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    bool RowsTruncated,
    bool PartialWarning,
    bool UnknownValues,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Messages,
    string? Failure)
{
    public bool Partial => PartialWarning || RowsTruncated;

    public string Status => End switch
    {
        ScriptRunEnd.Completed => Partial ? ReportReadState.Partial : ReportReadState.Collected,
        ScriptRunEnd.Cancelled => ReportReadState.Cancelled,
        _ => ReportReadState.Failed
    };
}

/// <summary>Runs a read-only library item. The desktop's page uses this seam so its tests never start a process.</summary>
public interface IScriptRunner
{
    Task<ScriptRunResult> RunAsync(ScriptRunRequest request, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>
/// Run for read-only library items (INT-072 bounded execution; INT-081 proposed). It generates the same verified Copy
/// wrapper (<see cref="ScriptCopy.Generate"/>) and runs it in an owned child PowerShell process through
/// <see cref="OwnedPowerShellProcess"/>, the runner the Exchange capture uses: the manifest's timeout, a CSV and output
/// size bound, cancellation and a process-tree kill on any stop. The wrapper signs in to Exchange Online, refuses another
/// tenant or account before reading, runs the pinned body unchanged and disconnects. Change items are refused outright.
/// It never installs a module, never changes execution policy and never chooses a host outside the fixed locations.
/// </summary>
public sealed class ScriptRunner : IScriptRunner
{
    public const string LauncherFileName = "Run-LibraryScript.ps1";
    public const string WrapperFileName = "Library-Item.ps1";
    public const string ModuleGuidance = "Install the required module through your organisation's approved PowerShell process, then run again. The toolkit does not install modules or change script policy.";
    public const string HostGuidance = "PowerShell did not run the launcher. Script policy or application control may block local scripts; ask your security owner. The toolkit does not change or bypass either.";
    private const int MaximumLine = 512;
    private const int MaximumWarnings = 64;
    private const int MaximumMessages = 32;

    private readonly Func<ScriptManifest, ScriptRunHost> _resolveHost;
    private readonly ScriptRunLimits _limits;

    public ScriptRunner(Func<ScriptManifest, ScriptRunHost>? resolveHost = null, ScriptRunLimits? limits = null)
    {
        _resolveHost = resolveHost ?? ResolveHost;
        _limits = limits ?? new ScriptRunLimits();
    }

    /// <summary>
    /// Windows PowerShell 5.1 where the item supports it (the same host the Exchange capture uses), otherwise PowerShell 7
    /// at its standard install location. If neither supported host is installed, the run is refused with the reason.
    /// </summary>
    public static ScriptRunHost ResolveHost(ScriptManifest manifest)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Running library items requires Windows.");
        if (manifest.SupportedRuntimes.Contains("5.1") && File.Exists(OwnedPowerShellProcess.WindowsPowerShellPath()))
            return new ScriptRunHost(OwnedPowerShellProcess.WindowsPowerShellPath(), "5.1");
        if (manifest.SupportedRuntimes.Contains("7") && File.Exists(OwnedPowerShellProcess.PowerShell7Path()))
            return new ScriptRunHost(OwnedPowerShellProcess.PowerShell7Path(), "7");
        throw new ConfigurationException($"{manifest.Name} needs PowerShell {string.Join(" or ", manifest.SupportedRuntimes)}, and no supported PowerShell was found at its standard location. Nothing was run. Install it through your organisation's approved process.");
    }

    /// <summary>The fixed launcher, exactly as embedded in the engine.</summary>
    public static string Launcher()
    {
        using var stream = typeof(ScriptRunner).Assembly.GetManifestResourceStream("ScriptRun.Launcher.ps1")
            ?? throw new ConfigurationException("The library run launcher is missing from this build.");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Every check made before anything is written or started. Returns the wrapper text that will run, which is exactly
    /// what Copy would produce for the same item, values, tenant, account and time.
    /// </summary>
    public static string Prepare(ScriptRunRequest request)
    {
        var entry = request.Entry;
        var m = entry.Manifest;
        // INT-072: change items are copy-only whatever the form or caller says.
        if (m.Mode != ScriptMode.ReadOnly) throw new SafetyViolationException($"{m.Name} is a change item. Changes are copy only and are never run by this tool.");
        // The bytes must still be the reviewed, pinned body, and still pass the library's read-only checks.
        if (ScriptCatalogue.Sha256(Encoding.ASCII.GetBytes(entry.Script)) != m.ScriptSha256 || entry.Script.Any(c => c > 0x7E))
            throw new IntegrityException($"{m.Name} no longer matches its pinned SHA-256. Nothing was run.");
        ScriptCatalogue.ValidateManifest(m);
        ScriptCatalogue.ValidateScript(m, entry.Script);
        if (!request.Binding.IsValid) throw new ConfigurationException("The form is not complete: " + string.Join(" ", request.Binding.Problems));
        foreach (var argument in request.Binding.Arguments)
            if (!m.Parameters.Any(p => string.Equals(p.Name, argument.Name, StringComparison.Ordinal)))
                throw new SafetyViolationException($"{argument.Name} is not a parameter of {m.Name}. Nothing was run.");
        if (!Guid.TryParse(request.Target.TenantId, out _)) throw new TenantMismatchException("Run needs the selected client's tenant ID. Nothing was run.");
        if (!ScriptCopy.IsAcceptableAccount(request.Target.Account))
            throw new TenantMismatchException("Run needs the connected account for this tenant, so the script can refuse any other. Connect to this client first.");
        return ScriptCopy.Generate(entry, request.Binding, request.Target, request.Now);
    }

    public async Task<ScriptRunResult> RunAsync(ScriptRunRequest request, IProgress<string>? progress, CancellationToken ct)
    {
        var m = request.Entry.Manifest;
        var wrapper = Prepare(request);
        var host = _resolveHost(m);
        if (!m.SupportedRuntimes.Contains(host.Runtime, StringComparer.Ordinal))
            throw new ConfigurationException($"{m.Name} does not support PowerShell {host.Runtime}. Nothing was run.");
        if (!File.Exists(host.Executable)) throw new ConfigurationException($"PowerShell {host.Runtime} was not found. Nothing was run.");

        var wrapperBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(wrapper)).ToArray();
        var wrapperSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wrapper))).ToLowerInvariant();
        var timeout = TimeSpan.FromSeconds(m.Limits.TimeoutSeconds);
        if (_limits.Timeout is { } shorter && shorter < timeout && shorter > TimeSpan.Zero) timeout = shorter;
        var maximumCsv = Math.Min(_limits.MaximumCsvBytes, ScriptRunLimits.DefaultCsvBytes);
        var maximumOutput = Math.Min(_limits.MaximumOutputBytes, ScriptRunLimits.DefaultOutputBytes);

        var directory = Path.Combine(Path.GetTempPath(), "BDIT-Run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var started = DateTimeOffset.UtcNow;
        try
        {
            var launcherFile = Path.Combine(directory, LauncherFileName);
            var wrapperFile = Path.Combine(directory, WrapperFileName);
            var csvFile = Path.Combine(directory, "output.csv");
            await File.WriteAllTextAsync(launcherFile, Launcher(), Encoding.ASCII, ct);
            // UTF-8 with a byte order mark: Windows PowerShell 5.1 reads a quoted non-ASCII value correctly only with one.
            await File.WriteAllBytesAsync(wrapperFile, wrapperBytes, ct);

            var start = CreateStartInfo(host, launcherFile, wrapperFile, csvFile, m.Modules);
            var markers = new MarkerReader(maximumOutput, progress);
            var errors = new StringBuilder();
            progress?.Report($"Starting PowerShell {host.Runtime} for {m.Name}. It signs in to Exchange Online and checks the tenant and account before reading. Read only.");
            var outcome = await OwnedPowerShellProcess.RunAsync(start, markers.ReadAsync, reader => KeepStartAsync(reader, errors), timeout,
                () => markers.OverLimit || CsvTooLarge(csvFile, maximumCsv), ct);
            var ended = DateTimeOffset.UtcNow;

            ScriptRunResult Stopped(ScriptRunEnd end, string failure) =>
                new(host.Runtime, wrapperSha, started, ended, end, outcome.ExitCode, m.OutputSchema.Columns, Array.Empty<IReadOnlyList<string>>(),
                    false, markers.Partial, markers.Unknown, markers.Warnings, markers.Messages, failure);

            switch (outcome.End)
            {
                case OwnedProcessEnd.Cancelled:
                    return Stopped(ScriptRunEnd.Cancelled, "Cancelled by the engineer. The PowerShell process was stopped; no rows were kept.");
                case OwnedProcessEnd.TimedOut:
                    return Stopped(ScriptRunEnd.TimedOut, $"Stopped after the item's {FormatTime(timeout)} limit. The PowerShell process was stopped; no rows were kept.");
                case OwnedProcessEnd.LimitExceeded:
                    return Stopped(ScriptRunEnd.OutputLimit, markers.OverLimit
                        ? $"PowerShell wrote more than {maximumOutput / 1024} KiB of console output. The process was stopped; no rows were kept."
                        : $"The result grew beyond {maximumCsv / (1024 * 1024)} MiB. The process was stopped; no rows were kept. Narrow the form and run again.");
            }

            if (!markers.Started)
                return Stopped(ScriptRunEnd.HostRefused, HostGuidance + (errors.Length > 0 ? " PowerShell said: " + errors : ""));
            if (markers.ModuleMissing is { } module)
                return Stopped(ScriptRunEnd.ModuleMissing, $"{module} is not installed for PowerShell {host.Runtime}. Nothing was signed in or read. {ModuleGuidance}");
            if (outcome.ExitCode != 0 || markers.Failure is not null)
                return Stopped(ScriptRunEnd.ScriptFailed, markers.Failure is { } failed
                    ? "The script stopped: " + failed
                    : $"PowerShell ended with exit code {outcome.ExitCode} without saying why. No rows were kept.");
            if (!markers.Done) return Stopped(ScriptRunEnd.InvalidOutput, "PowerShell ended without reporting that the script finished. No rows were kept.");
            // The wrapper reports how many rows it exported as its last step. A body that leaves early (an exit statement,
            // for example) never reaches that line, so a finished launcher alone is not proof the result is complete.
            if (markers.SavedRows is not { } saved)
                return Stopped(ScriptRunEnd.InvalidOutput, "The script ended without saving its result, so the run cannot show it is complete. No rows were kept.");

            IReadOnlyList<IReadOnlyList<string>> rows;
            var truncated = false;
            string text;
            try { text = File.Exists(csvFile) ? await ReadBoundedAsync(csvFile, maximumCsv, ct) : ""; }
            catch (ConfigurationException ex) { return Stopped(ScriptRunEnd.InvalidOutput, ex.Message + " No rows were kept."); }
            catch (DecoderFallbackException) { return Stopped(ScriptRunEnd.InvalidOutput, "The result is not valid UTF-8 text. No rows were kept."); }
            if (text.Trim().Length == 0)
            {
                // Export-Csv writes an empty file (PowerShell 7) or none (5.1) for an empty result.
                if (saved != 0) return Stopped(ScriptRunEnd.InvalidOutput, $"The script reported {saved} row(s) but the result file is empty. No rows were kept.");
                rows = Array.Empty<IReadOnlyList<string>>();
            }
            else
            {
                List<string[]> table;
                try { table = ScriptRunCsv.Parse(text); }
                catch (ConfigurationException ex) { return Stopped(ScriptRunEnd.InvalidOutput, ex.Message + " No rows were kept."); }
                if (table.Count == 0 || !table[0].SequenceEqual(m.OutputSchema.Columns, StringComparer.Ordinal))
                    return Stopped(ScriptRunEnd.InvalidOutput, "The result's columns do not match the item's declared columns. No rows were kept.");
                var body = table.Skip(1).ToList();
                if (body.Count != saved)
                    return Stopped(ScriptRunEnd.InvalidOutput, $"The script reported {saved} row(s) but the result file holds {body.Count}. No rows were kept.");
                if (body.Count > m.Limits.MaximumRows) { body = body.Take(m.Limits.MaximumRows).ToList(); truncated = true; }
                rows = body;
            }
            var warnings = markers.Warnings.ToList();
            if (truncated) warnings.Add($"BDIT:PARTIAL The result had more than {m.Limits.MaximumRows} rows; only the first {m.Limits.MaximumRows} were kept.");
            return new ScriptRunResult(host.Runtime, wrapperSha, started, ended, ScriptRunEnd.Completed, outcome.ExitCode, m.OutputSchema.Columns,
                rows, truncated, markers.Partial, markers.Unknown, warnings, markers.Messages, null);
        }
        finally
        {
            // Only the GUID folder this run created is removed; the kept result is the run record in the evidence store.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The exact process: the fixed launcher with -File, three plain path/requirement arguments, no profile, and nothing
    /// that changes execution policy or runs inline code.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(ScriptRunHost host, string launcherFile, string wrapperFile, string csvFile, IReadOnlyList<ScriptModule> modules)
    {
        var start = new ProcessStartInfo(host.Executable)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = false, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", launcherFile, "-Wrapper", wrapperFile, "-OutputCsv", csvFile,
                     "-Modules", string.Join(";", modules.Select(x => x.Name + ":" + x.MinimumVersion)) })
            start.ArgumentList.Add(argument);
        if (host.Environment is not null)
            foreach (var (key, value) in host.Environment) start.Environment[key] = value;
        return start;
    }

    private static bool CsvTooLarge(string file, long maximum)
    {
        try { return File.Exists(file) && new FileInfo(file).Length > maximum; }
        catch (IOException) { return false; }
    }

    private static async Task<string> ReadBoundedAsync(string file, long maximum, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new ConfigurationException("The result is larger than the run limit.");
        var bytes = new byte[stream.Length];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(length), ct);
            if (count == 0) break;
            length += count;
        }
        var text = new UTF8Encoding(false, true).GetString(bytes, 0, length);
        return text.TrimStart('﻿');
    }

    /// <summary>Keeps the first 1 KiB of standard error, used only to explain a host that refused to start the launcher.</summary>
    private static async Task KeepStartAsync(StreamReader reader, StringBuilder kept)
    {
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
            for (var i = 0; i < count && kept.Length < 1024; i++)
                kept.Append(char.IsControl(buffer[i]) ? ' ' : buffer[i]);
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalMinutes >= 1 && time.Seconds == 0 ? $"{(int)time.TotalMinutes}-minute" : $"{(int)time.TotalSeconds}-second";

    /// <summary>Reads the launcher's marker lines. Anything else on standard output is counted against the bound and dropped.</summary>
    private sealed class MarkerReader
    {
        private readonly long _maximum;
        private readonly IProgress<string>? _progress;
        private readonly List<string> _warnings = new();
        private readonly List<string> _messages = new();
        private long _read;
        private static readonly System.Text.RegularExpressions.Regex SavedPattern =
            new("^Saved ([0-9]{1,9}) row\\(s\\) to ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public MarkerReader(long maximum, IProgress<string>? progress) { _maximum = maximum; _progress = progress; }

        public volatile bool OverLimit;
        public bool Started { get; private set; }
        public bool Done { get; private set; }
        public bool Partial { get; private set; }
        public bool Unknown { get; private set; }
        public string? ModuleMissing { get; private set; }
        public string? Failure { get; private set; }
        /// <summary>The row count from the wrapper's last "Saved N row(s) to" line; the last one wins.</summary>
        public int? SavedRows { get; private set; }
        public IReadOnlyList<string> Warnings => _warnings;
        public IReadOnlyList<string> Messages => _messages;

        public async Task ReadAsync(StreamReader reader)
        {
            var buffer = new char[4096];
            var line = new StringBuilder();
            var oversized = false;
            int count;
            while ((count = await reader.ReadAsync(buffer)) != 0)
            {
                _read += count;
                if (_read > _maximum) OverLimit = true;
                for (var i = 0; i < count; i++)
                {
                    var c = buffer[i];
                    if (c == '\n')
                    {
                        if (!oversized) Handle(line.ToString().TrimEnd('\r'));
                        line.Clear();
                        oversized = false;
                    }
                    else if (!oversized && line.Length < MaximumLine) line.Append(c);
                    else oversized = true;
                }
            }
            if (!oversized && line.Length > 0) Handle(line.ToString().TrimEnd('\r'));
        }

        private void Handle(string line)
        {
            if (!line.StartsWith("BDIT:", StringComparison.Ordinal)) return;
            var rest = line[5..];
            var colon = rest.IndexOf(':');
            if (colon < 0) return;
            var kind = rest[..colon];
            var text = rest[(colon + 1)..].Trim();
            switch (kind)
            {
                case "STARTED":
                    Started = true;
                    _progress?.Report("PowerShell started. Microsoft sign-in may open; the tenant and account are checked before anything is read.");
                    break;
                case "MODULE_MISSING": ModuleMissing ??= text; break;
                case "FAILED": Failure ??= text; break;
                case "DONE": Done = true; break;
                case "WARNING":
                    if (text.StartsWith("BDIT:PARTIAL", StringComparison.Ordinal)) Partial = true;
                    if (text.StartsWith("BDIT:UNKNOWN", StringComparison.Ordinal)) Unknown = true;
                    if (_warnings.Count < MaximumWarnings) _warnings.Add(text);
                    _progress?.Report("Warning: " + text);
                    break;
                case "INFO":
                    var savedLine = SavedPattern.Match(text);
                    if (savedLine.Success && int.TryParse(savedLine.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var savedCount))
                        SavedRows = savedCount;
                    if (_messages.Count < MaximumMessages) _messages.Add(text);
                    _progress?.Report(text);
                    break;
            }
        }
    }
}

/// <summary>A strict reader for the CSV Export-Csv writes: quoted or plain fields, CRLF or LF, nothing malformed.</summary>
public static class ScriptRunCsv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var i = 0;
        var atFieldStart = true;
        var quoted = false;
        var wasQuoted = false;
        while (i < text.Length)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    quoted = false; i++;
                    if (i < text.Length && text[i] is not (',' or '\r' or '\n')) throw new ConfigurationException("The result is not valid CSV.");
                    continue;
                }
                field.Append(c); i++;
                continue;
            }
            switch (c)
            {
                case '"' when atFieldStart:
                    quoted = true; wasQuoted = true; atFieldStart = false; i++;
                    break;
                case '"':
                    throw new ConfigurationException("The result is not valid CSV.");
                case ',':
                    row.Add(field.ToString()); field.Clear(); atFieldStart = true; wasQuoted = false; i++;
                    break;
                case '\r' or '\n':
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    i++;
                    // A blank line (nothing, not even an empty quoted field) is not a row.
                    if (row.Count > 0 || field.Length > 0 || wasQuoted) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
                    row.Clear(); field.Clear(); atFieldStart = true; wasQuoted = false;
                    break;
                default:
                    field.Append(c); atFieldStart = false; i++;
                    break;
            }
        }
        if (quoted) throw new ConfigurationException("The result is not valid CSV.");
        if (field.Length > 0 || row.Count > 0 || wasQuoted) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        if (rows.Count > 0 && rows.Any(r => r.Length != rows[0].Length)) throw new ConfigurationException("A row in the result has the wrong number of fields.");
        return rows;
    }
}
