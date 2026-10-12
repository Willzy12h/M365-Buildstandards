using System.Collections.ObjectModel;
using System.Text;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Evidence;
using BuildStandard.Engine.Reports;
using BuildStandard.Engine.Scripts;
using BuildStandard.Engine.Scripts.Runner;
using BuildStandard.Graph;

namespace BuildStandard.App.Services;

/// <summary>The visible review. Only the workflow that issued this single-use instance can consume it.</summary>
public sealed record ReadOnlyRunReview(string Text);

/// <summary>
/// Desktop coordination without WPF or a second runner/authentication implementation. The provider supplies the actual
/// current connection, never historical evidence. The production transport is the owned ExchangeReportRunner.
/// </summary>
public sealed class ReadOnlyRunWorkflow
{
    public const string ClosedReason = "Enable experimental read-only runs for this verified connection first. This grants no tenant-write access or Microsoft consent.";
    public const string ChangedReason = "The connection, item or reviewed values changed. Review again before running. Any original saved record is retained.";
    public const string FailedReason = "The run could not be confirmed. No unverified rows were accepted. Review the runtime/module requirements and try a newly reviewed run.";
    public const string SaveFailedReason = "Saving could not be confirmed. A disk copy may already exist. Inspect saved evidence and local access before explicitly choosing Save evidence. No record is overwritten and no run is repeated.";

    private readonly Func<(ConnectedTenant? Connection, TenantProfile? Profile)> _current;
    private readonly ScriptCatalogue _catalogue;
    private readonly IClock _clock;
    private readonly Func<ScriptEntry, ReadRunContext, IReadOnlyDictionary<string, string?>, string, IProgress<string>?, CancellationToken, Task<ReadRunResult>> _run;
    private readonly Func<ExchangeReportEvidence, string> _save;
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private bool _cancelRequested, _cancelFailed;
    private ConnectedTenant? _enabledConnection;
    private string? _enabledContext;
    private string? _selection;
    private string _runtime = "7";
    private IReadOnlyDictionary<string, string?> _inputs = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());
    private ReadOnlyRunReview? _review;
    private string? _reviewKey;
    private string _evidenceJson = "";

    public ReadOnlyRunWorkflow(Func<(ConnectedTenant? Connection, TenantProfile? Profile)> current, EvidenceStore store)
        : this(current, ScriptCatalogue.Shipped, SystemClock.Instance, new ExchangeReportRunner(SystemClock.Instance).RunAsync, store.SaveExchangeReport) { }

    internal ReadOnlyRunWorkflow(Func<(ConnectedTenant? Connection, TenantProfile? Profile)> current, ScriptCatalogue catalogue, IClock clock,
        Func<ScriptEntry, ReadRunContext, IReadOnlyDictionary<string, string?>, string, IProgress<string>?, CancellationToken, Task<ReadRunResult>> run,
        Func<ExchangeReportEvidence, string> save)
    { _current = current; _catalogue = catalogue; _clock = clock; _run = run; _save = save; }

    public IReadOnlyList<ScriptEntry> Items => _catalogue.Entries.Where(e => e.Manifest.SchemaVersion == 2 && e.Manifest.Mode == ScriptMode.ReadOnly).ToList();
    public bool IsRunning { get { lock (_gate) return _active is not null; } }
    public bool CancellationRequested { get { lock (_gate) return _cancelRequested; } }
    public ReadOnlyRunReview? Review { get { RefreshContext(); return _review; } }
    public ReadRunResult? Result { get; private set; }
    public string Output { get; private set; } = "";
    public string EvidenceJson => _evidenceJson;
    public string SavedFile { get; private set; } = "";
    public string Message { get; private set; } = ClosedReason;
    public bool CanSave => !IsRunning && _evidenceJson.Length > 0 && SavedFile.Length == 0;
    public bool Enabled { get { RefreshContext(); return _enabledConnection is not null; } }
    public bool CanEnable { get { try { Live(); return !IsRunning; } catch (ToolkitException) { return false; } } }
    public string ContextText
    {
        get
        {
            try
            {
                var (_, _, s) = Live();
                return $"Verified Graph tenant: {s.TenantName} ({s.TenantId})\nSigned-in account: {s.Account}\nGraph-verified object ID: {s.AccountObjectId}\nConfirmed UPN for Exchange: {s.OperatorUpn}\nGraph application: {s.ClientLabel} ({s.ClientId})\nGraph permission mode: {s.Mode} · token has write scopes: {(s.HasWriteScopes ? "yes" : "no")}. Run uses separate ExchangeOnline authentication; it receives no Graph token/cache.";
            }
            catch (ToolkitException) { return "Connect deliberately with a verified tenant, account object ID and confirmed UPN. A saved profile or historical report cannot enable Run."; }
        }
    }
    public string Blocker
    {
        get
        {
            if (IsRunning) return "A read-only run is active. Cancel requests a cooperative stop; the owned runner ends its own process if needed. Closing waits for completion.";
            try { Live(); }
            catch (ToolkitException) { return ContextText; }
            if (!Enabled) return ClosedReason;
            try { Selection(); }
            catch (ToolkitException) { return "Select a registered read-only report, a reviewed runtime and valid typed inputs before reviewing."; }
            return _review is null ? "Review the exact tenant, account, purpose, runtime and values, then tick the separate approval before Run." : "Tick approval for this exact review, then explicitly choose Run. Exchange sign-in may require Microsoft CA/MFA/consent.";
        }
    }

    private (ConnectedTenant Connection, TenantProfile Profile, TenantSession Session) Live()
    {
        var (connection, profile) = _current();
        var s = connection?.Session;
        if (connection is null || profile is null || s is null || !s.TenantVerified || !s.OperatorVerified
            || !ReportValues.IsCanonicalGuid(s.TenantId) || !ReportValues.IsCanonicalGuid(s.AccountObjectId)
            || !string.Equals(s.OperatorObjectId, s.AccountObjectId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(profile.TenantId, s.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(connection.Graph.TenantId, s.TenantId, StringComparison.OrdinalIgnoreCase) || connection.Graph.Mode != s.Mode
            || !ScriptCopy.IsAcceptableAccount(s.Account) || !ScriptCopy.IsAcceptableAccount(s.OperatorUpn) || s.OperatorUpn != s.OperatorUpn.Trim()
            || !Timestamps.TryParse(s.TokenExpiresAt, out _))
            throw new TenantMismatchException(ChangedReason);
        // Connect-time expiry is provenance, not the lifetime of this read context. Provider invalidation clears verified
        // facts; the separate Exchange connection is checked by the runner before/after each read. No token is acquired here.
        return (connection, profile, s);
    }

    private string ContextKey()
    {
        var (_, profile, session) = Live();
        return ToolkitJson.Serialize(new { profile, session });
    }

    public void RefreshContext()
    {
        if (_enabledConnection is null) return;
        try
        {
            if (ReferenceEquals(_current().Connection, _enabledConnection) && ContextKey() == _enabledContext) return;
        }
        catch (ToolkitException) { }
        _enabledConnection = null; _enabledContext = null; ClearReviewAndResult(); Message = ChangedReason;
        if (IsRunning) Cancel();
    }

    public void SetEnabled(bool approved)
    {
        if (IsRunning) throw new SafetyViolationException("Wait for the current run before changing its experimental opt-in.");
        _enabledConnection = null; _enabledContext = null; ClearReviewAndResult();
        if (approved)
        {
            var (connection, _, _) = Live();
            _enabledContext = ContextKey(); _enabledConnection = connection;
        }
        Message = approved ? "Experimental read-only Run enabled for this connection only. Each run still needs a new exact review and approval. Live tenant behaviour remains unverified." : ClosedReason;
    }

    public void SetSelection(string? id, string runtime, IReadOnlyDictionary<string, string?> inputs)
    {
        var frozen = new ReadOnlyDictionary<string, string?>(inputs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase));
        if (_selection == id && _runtime == runtime && ToolkitJson.Serialize(_inputs) == ToolkitJson.Serialize(frozen)) return;
        _selection = id; _runtime = runtime; _inputs = frozen; ClearReviewAndResult();
        Message = "Selection changed. Review its current requirements and exact values before Run.";
        if (IsRunning) Cancel();
    }

    private (ScriptEntry Entry, ReadRunContext Context, IReadOnlyList<ReadRunParameter> Parameters) Selection()
    {
        var (_, _, s) = Live();
        var entry = _catalogue.Find(_selection ?? "");
        if (entry.Manifest.SchemaVersion != 2 || entry.Manifest.Mode != ScriptMode.ReadOnly || entry.Manifest.Execution is null
            || !entry.Manifest.SupportedRuntimes.Contains(_runtime, StringComparer.Ordinal)
            || ScriptCatalogue.Sha256(Encoding.ASCII.GetBytes(entry.Script)) != entry.Manifest.ScriptSha256)
            throw new SafetyViolationException("Only a pinned registered schema 2 read-only report and its reviewed runtimes can run.");
        var context = new ReadRunContext(s.TenantId, s.AccountObjectId, s.OperatorUpn);
        return (entry, context, ScriptReadRequest.Bind(entry, context, _inputs, _clock.UtcNow));
    }

    private string SelectionKey(ScriptEntry entry) => ToolkitJson.Serialize(new { Context = ContextKey(), Selection = _selection, entry, Runtime = _runtime, Inputs = _inputs });

    public ReadOnlyRunReview PrepareReview()
    {
        if (IsRunning) throw new SafetyViolationException("Wait for the current run.");
        if (!Enabled) throw new SafetyViolationException(ClosedReason);
        var (entry, _, parameters) = Selection();
        var m = entry.Manifest; var report = ScriptCatalogue.ReportFor(m);
        var text = new StringBuilder(ContextText).Append("\n\nREAD ONLY — no tenant changes. Live behaviour is unverified.\n")
            .Append(m.Name).Append(" (").Append(m.Id).Append(")\nPurpose: ").Append(m.Description)
            .Append("\nResource: ").Append(report.Resource).Append(" · adapter ").Append(m.Execution!.Adapter)
            .Append("\nPowerShell ").Append(_runtime).Append(" at its standard location · timeout ").Append(m.Limits.TimeoutSeconds)
            .Append(" seconds · at most ").Append(m.Limits.MaximumRows).Append(" rows per section\nModules: ")
            .AppendJoin("; ", m.Modules.Select(x => x.Name + " " + x.MinimumVersion + " or later"))
            .Append("\nRole requirements: ").AppendJoin(" or ", m.Roles).Append("\nRole names do not establish effective Exchange permission.\nRegistered reads: ").AppendJoin(", ", report.SourceCommands).Append("\n").Append(m.Prerequisites)
            .Append("\nSeparate Microsoft Exchange sign-in may request CA/MFA/consent. Graph sign-in does not establish Exchange access.\n")
            .Append("No module installation, policy change, alternate host, automatic retry or tenant write is offered.\n\nExact typed values:\n");
        foreach (var parameter in parameters) text.Append(parameter.Name).Append(" (").Append(parameter.Type).Append("): ").Append(parameter.Value.Length == 0 ? "(not set)" : parameter.Value).Append('\n');
        text.Append("\nManifest SHA-256: ").Append(entry.ManifestSha256).Append("\nBody SHA-256: ").Append(m.ScriptSha256)
            .Append("\nOwned wrapper SHA-256: ").Append(m.Execution.RunnerTemplateSha256).Append("\nLimitations:\n");
        foreach (var limitation in m.Limitations) text.Append("• ").Append(limitation).Append('\n');
        _reviewKey = SelectionKey(entry); _review = new(text.ToString());
        return _review;
    }

    public async Task RunAsync(ReadOnlyRunReview review, bool approved, IProgress<string>? progress, CancellationToken cancellation)
    {
        if (IsRunning) throw new SafetyViolationException("A run is already active.");
        if (!Enabled) throw new SafetyViolationException(ClosedReason);
        var (entry, context, parameters) = Selection();
        if (!approved || !ReferenceEquals(review, _review) || _reviewKey != SelectionKey(entry)) throw new SafetyViolationException(ChangedReason);
        var key = _reviewKey; var connection = _enabledConnection;
        // Consume review before invoking any transport, even if the transport refuses or throws.
        ClearReviewAndResult();
        using var owned = new CancellationTokenSource();
        lock (_gate) { _active = owned; _cancelRequested = false; _cancelFailed = false; }
        // Capture this run's source. A cancellation callback cannot target a subsequent run or leak runner exceptions.
        using var registration = cancellation.Register(() => RequestCancel(owned));
        Message = "Running the reviewed read-only report. Exchange authentication is separate.";
        ReadRunResult? result = null;
        try
        {
            owned.Token.ThrowIfCancellationRequested();
            var frozenEntry = new ScriptEntry(ToolkitJson.Deserialize<ScriptManifest>(ToolkitJson.Serialize(entry.Manifest)), entry.Script, entry.ManifestSha256);
            result = await _run(frozenEntry, context, _inputs, _runtime, progress, owned.Token);
            RefreshContext();
            if (!ReferenceEquals(_current().Connection, connection) || key != SelectionKey(entry)) throw new TenantMismatchException(ChangedReason);
            if (Volatile.Read(ref _cancelFailed)) throw new SafetyViolationException(FailedReason);
            if (owned.IsCancellationRequested && result.Outcome is not (ReadRunOutcome.Cancelled or ReadRunOutcome.TimedOut))
                result = new(result.RunId, ReadRunOutcome.Cancelled, "Cancellation was requested before completion was confirmed. No rows were accepted.", null, result.DiagnosticsTruncated);
            if (result.Outcome == ReadRunOutcome.Completed && result.Evidence is null) throw new IntegrityException(FailedReason);
            if (result.Evidence is not null)
            {
                var evidence = ExchangeReportEvidenceSchema.Read(ExchangeReportEvidenceSchema.Serialize(result.Evidence), context.TenantId);
                var report = ScriptCatalogue.ReportFor(entry.Manifest);
                if (result.Outcome is not (ReadRunOutcome.Completed or ReadRunOutcome.Cancelled or ReadRunOutcome.TimedOut)
                    || evidence.SourceMode != "live" || evidence.RunId != result.RunId || evidence.ReportId != report.Id || evidence.Resource != report.Resource
                    || evidence.InitiatingAccountObjectId != context.AccountObjectId || !string.Equals(evidence.ObservedAccountUpn, context.AccountUpn, StringComparison.OrdinalIgnoreCase)
                    || evidence.ManifestSha256 != entry.ManifestSha256 || evidence.ScriptSha256 != entry.Manifest.ScriptSha256
                    || evidence.RunnerTemplateSha256 != entry.Manifest.Execution!.RunnerTemplateSha256
                    || !evidence.Parameters.Select(p => (p.Name, p.Type, p.Value)).SequenceEqual(parameters.Select(p => (p.Name, p.Type, p.Value)))
                    || result.Outcome == ReadRunOutcome.Cancelled && evidence.Status == ReportReadState.Collected
                    || result.Outcome == ReadRunOutcome.TimedOut && evidence.Status == ReportReadState.Collected)
                    throw new IntegrityException(FailedReason);
                _evidenceJson = ExchangeReportEvidenceSchema.Serialize(evidence);
                result = result with { Evidence = evidence };
            }
            Result = result;
            Output = OutcomeText(result);
            Message = result.Reason;
            if (_evidenceJson.Length > 0) SaveEvidenceCore();
        }
        catch (OperationCanceledException) when (owned.IsCancellationRequested)
        { Message = "The run was cancelled before a verified result returned. No rows were accepted."; }
        catch (Exception)
        { ClearReviewAndResult(); Message = FailedReason; }
        finally
        {
            // The linked registration targets this owned source, never a later Workspace operation.
            lock (_gate) _active = null;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? owned;
        lock (_gate) owned = _active;
        if (owned is not null) RequestCancel(owned);
    }
    private void RequestCancel(CancellationTokenSource owned)
    {
        lock (_gate) { if (!ReferenceEquals(_active, owned) || _cancelRequested) return; _cancelRequested = true; }
        Message = "Cancellation requested. Waiting for the owned runner to stop; no new run can start yet.";
        try { owned.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (Exception) { Volatile.Write(ref _cancelFailed, true); Message = FailedReason; }
    }

    public void SaveEvidence()
    {
        RefreshContext();
        if (!CanSave) throw new SafetyViolationException("A verified unsaved report is required. A saved record is never overwritten.");
        SaveEvidenceCore();
    }
    private void SaveEvidenceCore()
    {
        try
        {
            var report = ExchangeReportEvidenceSchema.Read(_evidenceJson, Result!.Evidence!.TenantId);
            SavedFile = _save(report);
            Message = "Verified evidence saved once in this tenant's local exchange-report-evidence folder. Its read states and limitations remain authoritative.";
        }
        catch (Exception) { Message = SaveFailedReason; }
    }
    private void ClearReviewAndResult()
    { _review = null; _reviewKey = null; Result = null; Output = ""; _evidenceJson = ""; SavedFile = ""; }

    private static string OutcomeText(ReadRunResult result)
    {
        var text = new StringBuilder().Append("Run ID: ").Append(result.RunId).Append("\nOutcome: ").Append(result.Outcome)
            .Append("\n").Append(result.Reason).Append("\nDiagnostics truncated: ").Append(result.DiagnosticsTruncated ? "yes" : "no");
        if (result.Evidence is not { } report) return text.Append("\nNo verified report evidence or rows were accepted.").ToString();
        text.Append("\nReport read status: ").Append(report.Status).Append("\nReport ID: ").Append(report.ReportId)
            .Append("\nEvidence ID: ").Append(report.Id).Append("\nTenant: ").Append(report.TenantId)
            .Append("\nInitiating Graph account object ID: ").Append(report.InitiatingAccountObjectId).Append("\nObserved Exchange UPN: ").Append(report.ObservedAccountUpn)
            .Append("\nResource: ").Append(report.Resource).Append("\nSource: ").Append(report.SourceMode)
            .Append("\nRead interval: ").Append(report.StartedAt).Append(" to ").Append(report.EndedAt)
            .Append("\nModule/runtime: ").Append(report.ModuleVersion).Append(" / ").Append(report.RuntimeVersion)
            .Append("\nIntegrity digest: ").Append(report.IntegrityDigest).Append("\nDigest verifies integrity, not Microsoft acceptance. This is report evidence, never deployment before-evidence.\n");
        foreach (var section in report.Sections) text.Append(section.Id).Append(": ").Append(section.Status).Append(" · ").Append(section.Rows.Count).Append(" returned rows\n").Append(section.Error).Append('\n');
        foreach (var limitation in report.Limitations.Concat(report.Sections.SelectMany(s => s.Limitations))) text.Append("• ").Append(limitation).Append('\n');
        return text.ToString();
    }
}
