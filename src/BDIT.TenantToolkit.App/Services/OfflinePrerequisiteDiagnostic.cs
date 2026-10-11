using System.Globalization;
using System.IO;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.Cli;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Engine.Diagnostics;

namespace BDIT.TenantToolkit.App.Services;

/// <summary>Desktop state for the shared offline checks. The caller owns workspace exclusivity; this owns cancellation.</summary>
public sealed class OfflinePrerequisiteDiagnostic : ObservableObject
{
    public const string UnexpectedFailure = "Offline checks failed unexpectedly. No readiness was certified. Ask the toolkit maintainer to review this offline failure; review any .bdit-doctor- temporary files in the portable folder.";
    private readonly Func<string, CancellationToken, OfflinePrerequisiteResult> _check;
    private readonly object _gate = new();
    private CancellationTokenSource? _active;
    private bool _cancellationRequested, _cancellationFailed;
    private OfflinePrerequisiteResult? _result;
    private string _output = "";
    private string _message = "No offline check has run. Choose Run offline checks to inspect this portable folder.";

    public OfflinePrerequisiteDiagnostic() : this(new OfflinePrerequisiteService().Check) { }

    // Faults and controlled completions in offline tests, never a configurable production host or collector.
    internal OfflinePrerequisiteDiagnostic(Func<string, CancellationToken, OfflinePrerequisiteResult> check) => _check = check;

    public bool IsRunning { get { lock (_gate) return _active is not null; } }
    public bool CancellationRequested { get { lock (_gate) return _cancellationRequested; } }
    public OfflinePrerequisiteResult? Result { get => _result; private set { if (SetProperty(ref _result, value)) OnPropertyChanged(nameof(HasResult)); } }
    public bool HasResult => Result is not null;
    public string Output { get => _output; private set => SetProperty(ref _output, value); }
    public string Message => IsRunning
        ? CancellationRequested
            ? "Cancellation requested. Waiting for the current local file check and owned-probe cleanup; no readiness has been certified."
            : "Checking local package files and prerequisites. No sign-in, tenant connection, host execution or repair is performed."
        : _message;

    public async Task RunAsync(string root, CancellationToken cancellationToken = default)
    {
        CancellationTokenSource owned;
        lock (_gate)
        {
            if (_active is not null) return;
            _active = owned = new CancellationTokenSource();
            _cancellationRequested = _cancellationFailed = false;
        }
        OfflinePrerequisiteResult? result = null;
        var output = "";
        var message = UnexpectedFailure;
        CancellationTokenRegistration registration = default;
        try
        {
            Result = null;
            Output = "";
            NotifyOperation();
            // Capture this operation, rather than forwarding cancellation to whatever workspace task comes next.
            registration = cancellationToken.Register(() => RequestCancellation(owned));
            result = await Task.Run(() => _check(root, owned.Token));
            if (result is null) throw new InvalidOperationException();
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            DoctorCommand.WriteText(result, text);
            output = text.ToString();
            message = result.Status switch
            {
                OfflinePrerequisiteStatus.Ready => "Ready: all required local checks passed. Optional limitations are listed below; publisher trust and tenant access remain unverified.",
                OfflinePrerequisiteStatus.NeedsAttention => "Needs attention: a required check failed or remains Unknown. Follow the next steps below, then run the local checks again.",
                OfflinePrerequisiteStatus.Cancelled => "Cancelled: local readiness was not certified. Review the completed observations, then run the checks again.",
                _ => throw new InvalidOperationException()
            };
        }
        catch (OfflineDiagnosticInputException)
        {
            result = null;
            message = "Offline checks were refused. Use an existing, ordinary extracted portable folder without links or reparse points. Ask the security owner to review the folder; no protections were changed.";
        }
        catch (OperationCanceledException) when (owned.IsCancellationRequested)
        {
            result = null;
            message = "Cancelled before a diagnostic result completed. No local readiness was certified. Run the checks again.";
        }
        catch (Exception) { result = null; message = UnexpectedFailure; }
        finally
        {
            // Unregister first: disposal may wait for an in-flight workspace cancellation callback.
            // Never hold _gate while waiting for that callback, which also takes _gate.
            var cleanupFailed = false;
            var cancelled = false;
            try { registration.Dispose(); }
            catch (Exception) { cleanupFailed = true; }
            lock (_gate)
            {
                cleanupFailed |= _cancellationFailed;
                cancelled = owned.IsCancellationRequested;
                _active = null;
                try { owned.Dispose(); }
                catch (Exception) { cleanupFailed = true; }
            }
            if (cleanupFailed) { result = null; message = UnexpectedFailure; }
            else if (cancelled && result is not null && result.Status != OfflinePrerequisiteStatus.Cancelled)
            {
                try
                {
                    result = result with { Status = OfflinePrerequisiteStatus.Cancelled };
                    using var text = new StringWriter(CultureInfo.InvariantCulture);
                    DoctorCommand.WriteText(result, text);
                    output = text.ToString();
                    message = "Cancelled: local readiness was not certified. Review the completed observations, then run the checks again.";
                }
                catch (Exception) { result = null; message = UnexpectedFailure; }
            }
            Result = result;
            Output = result is null ? "" : output;
            _message = message;
            NotifyOperation();
        }
    }

    internal void ShowUnexpectedFailure()
    {
        Result = null;
        Output = "";
        _message = UnexpectedFailure;
        OnPropertyChanged(nameof(Message));
    }

    public void Cancel()
    {
        CancellationTokenSource? owned;
        lock (_gate) owned = _active;
        if (owned is null) return;
        RequestCancellation(owned);
        OnPropertyChanged(nameof(CancellationRequested));
        OnPropertyChanged(nameof(Message));
    }

    private void RequestCancellation(CancellationTokenSource owned)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_active, owned) || _cancellationRequested) return;
            _cancellationRequested = true;
            try { owned.Cancel(); }
            catch (Exception) { _cancellationFailed = true; }
        }
    }

    private void NotifyOperation()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CancellationRequested));
        OnPropertyChanged(nameof(Message));
    }
}
