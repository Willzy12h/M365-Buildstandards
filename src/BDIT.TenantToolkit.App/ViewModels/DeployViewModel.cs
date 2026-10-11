using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Views;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class PrerequisiteRow
{
    public string Step { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
    public string PageKey { get; init; } = "";
}

public sealed class DeployViewModel : PageViewModel
{
    private string _lastExport = "";
    private RunResult? _selectedResult;

    public DeployViewModel(ShellViewModel shell) : base(shell, "Deploy")
    {
        EnableDeploymentCommand = Command(EnableDeploymentAsync, () => Workspace.Profile is not null && Workspace.Idle && !Workspace.IsDeploymentSession);
        CaptureCommand = Command(Workspace.CaptureAsync, () => Workspace.IsConnected && Workspace.Idle);
        ExportBeforeCommand = Command(ExportBefore, () => Workspace.Snapshot is not null && Workspace.SnapshotIsLive);
        AcknowledgeCommand = Sync(Workspace.AcknowledgeSnapshot, () => Workspace.Snapshot?.Complete == true && Workspace.SnapshotIsLive && Workspace.Idle);
        DeployCommand = Command(DeployAsync, () => CanDeploy);
        PauseCommand = Sync(Workspace.PauseDeployment, () => IsRunning && !(Workspace.Control?.Paused ?? false));
        ResumeCommand = Sync(Workspace.ResumeDeployment, () => IsRunning && (Workspace.Control?.Paused ?? false));
        StopCommand = Sync(Workspace.StopDeployment, () => IsRunning);
        ExportRunHtmlCommand = Command(() => ExportRun(ExportFormat.Html), () => Workspace.LastRun is not null);
        ExportRunJsonCommand = Command(() => ExportRun(ExportFormat.Json), () => Workspace.LastRun is not null);
        ExportRunXlsxCommand = Command(() => ExportRun(ExportFormat.Xlsx), () => Workspace.LastRun is not null);
        Refresh();
    }

    public ICommand EnableDeploymentCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ExportBeforeCommand { get; }
    public ICommand AcknowledgeCommand { get; }
    public ICommand DeployCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ExportRunHtmlCommand { get; }
    public ICommand ExportRunJsonCommand { get; }
    public ICommand ExportRunXlsxCommand { get; }
    public RunResult? SelectedResult
    {
        get => _selectedResult;
        set { if (SetProperty(ref _selectedResult, value)) OnPropertyChanged(nameof(SelectedEvidence)); }
    }
    public ResultEvidence SelectedEvidence => new(SelectedResult, Shell.CopyCommand);

    public ObservableCollection<PrerequisiteRow> Prerequisites { get; } = new();
    public ObservableCollection<RunResult> Results { get; } = new();
    public string LastExport { get => _lastExport; private set => SetProperty(ref _lastExport, value); }
    public string DeploymentButtonText => Workspace.Settings.ResolveClient(SessionMode.Deployment, Workspace.Profile) is null
        ? "Configure deployment application" : "Enable deployment access";
    public string DeploymentApplicationText => Workspace.Settings.ResolveClient(SessionMode.Deployment, Workspace.Profile) is { } app
        ? $"Deployment application: {app.Label} · Application (client) ID: {app.ClientId}. Enable deployment access to verify sign-in and permissions; a configured ID alone does not establish access."
        : "No deployment application client ID is recorded for this connection. Select Configure deployment application. If it already exists in Entra, enter its Application (client) ID, approve permissions and check your account assignment. Use Continue to deployment when ready; do not create a duplicate or use the assessment application's ID.";

    public bool IsRunning => Workspace.Executor.IsRunning;
    public bool CanDeploy => Workspace.IsDeploymentSession && Workspace.ExperimentalChangesEnabled && Workspace.Plan is not null && Workspace.SnapshotIsLive
                             && Workspace.Snapshot?.Complete == true && Workspace.AcknowledgedSnapshotId == Workspace.Snapshot?.Id && Workspace.Idle && Workspace.Plan.WriteRows.Any();

    public string RunText
    {
        get
        {
            var run = Workspace.LastRun;
            if (IsRunning) return "Deployment running. " + Workspace.StopGuidance;
            if (run is null) return "No deployment has been started in this session.";
            return $"Run {run.Id} · {run.Status} · started {run.StartedAt} · ended {run.EndedAt} · before {run.BeforeSnapshotId} · after {run.AfterSnapshotId ?? "not captured"}{(run.AfterComplete == false ? " (INCOMPLETE)" : "")}{(run.Error is null ? "" : " · " + run.Error)}";
        }
    }

    private async Task EnableDeploymentAsync()
    {
        var profile = Workspace.Profile ?? throw new ToolkitException("Select a client first.");
        if (Workspace.Settings.ResolveClient(SessionMode.Deployment, profile) is null)
        {
            Shell.Page<ConnectViewModel>().OpenSetupForProfile(profile);
            return;
        }
        var confirm = System.Windows.MessageBox.Show(
            "Deployment access uses the M365 BuildStandard Deployment Tool application and requests write permissions for this tenant. A known, verified account may reconnect silently; Microsoft can still require sign-in, MFA or consent.\n\n" +
            "Any capture, assessment and plan from the read-only session are discarded and must be repeated in the deployment session.\n\nNothing is written until you confirm a reviewed plan. Continue?",
            "Enable deployment access", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;
        await Workspace.ConnectAsync(profile, SessionMode.Deployment);
    }

    private async Task ExportBefore()
    {
        var snapshot = Workspace.Snapshot ?? throw new ToolkitException("Capture the tenant first.");
        var standard = Workspace.Standard;
        LastExport = "Exported before-change capture: " + await Workspace.ExportAsync(() => Workspace.Exporter.ExportSnapshot(snapshot, standard, ExportFormat.Json));
    }

    private async Task DeployAsync()
    {
        Workspace.RequireExperimentalChanges(BDIT.TenantToolkit.App.Services.ExperimentalOperation.Deploy);
        Workspace.ValidatePlanForExecution();
        var plan = Workspace.Plan!;
        var reviewedDigest = plan.PlanDigest;
        var dialog = new ConfirmTenantDialog(Workspace.Profile!, plan, Workspace.Session) { Owner = System.Windows.Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;
        if (Workspace.Plan?.PlanDigest != reviewedDigest) throw new PlanValidationException("The plan changed during review. Review it again.");
        await Workspace.DeployAsync(dialog.ConfirmedTenantId);
        Refresh();
    }

    private async Task ExportRun(ExportFormat format)
    {
        var run = Workspace.LastRun ?? throw new ToolkitException("No run to export.");
        var journal = Workspace.Evidence.ReadJournal(run.TenantId, run.Id);
        LastExport = "Exported: " + await Workspace.ExportAsync(() => Workspace.Exporter.ExportRun(run, journal, format));
    }

    private string CaptureReadinessDetail()
    {
        var snapshot = Workspace.Snapshot;
        if (snapshot is null) return "Open 2 · Configuration and capture the tenant in this session.";
        var failures = snapshot.Collections.Where(c => c.Value.Status != CaptureStatus.Collected)
            .Select(c => $"{c.Key}: {c.Value.Status}. {c.Value.Error ?? "Review Collection status for missing details."}");
        var detail = string.Join("\n", failures);
        return $"{snapshot.Id} captured {snapshot.CapturedAt}\n" + (snapshot.Complete
            ? "Complete. Freshness and route checks are repeated before approval."
            : "Incomplete: open 2 · Configuration → Collection status. Resolve permissions or unsupported requests, then re-capture.\n" + detail);
    }

    public override void Refresh()
    {
        Prerequisites.Clear();
        var session = Workspace.Session;
        Prerequisites.Add(new PrerequisiteRow
        {
            Step = "1 · Connect: deployment access", PageKey = "connect",
            Status = session is null ? "Not connected" : session.Mode == SessionMode.Deployment ? (Workspace.ExperimentalChangesEnabled ? "Ready for exact review" : "Experimental changes off") : "Read-only session",
            Detail = session is null ? "Connect on the Connect page first." : session.Mode == SessionMode.Deployment ? $"{session.Account} via {session.ClientLabel}. {Workspace.ExperimentalChangeGuidance}" : DeploymentApplicationText
        });
        var missingScopes = Workspace.Access?.Writes.Where(w => w.Status == "Missing scope").Select(w => w.Label).ToList() ?? new List<string>();
        Prerequisites.Add(new PrerequisiteRow
        {
            Step = "1 · Connect: permissions", PageKey = "connect",
            Status = session?.Mode != SessionMode.Deployment ? "Pending" : Workspace.Access is null ? "Unknown" : missingScopes.Count == 0 ? "Observed" : "Missing",
            Detail = Workspace.Access is null ? "Run the access check before relying on scope observations." : missingScopes.Count == 0 ? "Scope observation does not prove API acceptance." : "Missing: " + string.Join(", ", missingScopes)
        });
        Prerequisites.Add(new PrerequisiteRow
        {
            Step = "2 · Configuration: live capture", PageKey = "configuration",
            Status = Workspace.Snapshot is null ? "Pending" : Workspace.SnapshotIsLive ? (Workspace.Snapshot.Complete ? "Ready" : "Incomplete") : "Stored capture",
            Detail = CaptureReadinessDetail()
        });
        Prerequisites.Add(new PrerequisiteRow
        {
            Step = "5 · Plan changes: reviewed plan", PageKey = "plan",
            Status = Workspace.Plan is null ? "Pending" : Workspace.Plan.WriteRows.Any() ? "Ready" : "No changes",
            Detail = Workspace.Plan is null ? "Build a plan on the Plan changes page." : $"{Workspace.Plan.WriteRows.Count()} write(s) · digest {Workspace.Plan.PlanDigest[..12]}…"
        });
        Prerequisites.Add(new PrerequisiteRow
        {
            Step = "6 · Deploy: preserve evidence", PageKey = "deploy",
            Status = Workspace.Snapshot is not null && Workspace.AcknowledgedSnapshotId == Workspace.Snapshot.Id ? "Ready" : "Pending",
            Detail = "Export the before-change capture and acknowledge it. Plans expire with their snapshot after " + Workspace.Settings.SnapshotMaxAgeMinutes + " minutes."
        });
        var selectedId = SelectedResult?.ControlId;
        Results.Clear();
        if (Workspace.LastRun is not null) foreach (var r in Workspace.LastRun.Results) Results.Add(r);
        SelectedResult = Results.FirstOrDefault(r => r.ControlId == selectedId);
        OnPropertyChanged(nameof(RunText));
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(DeploymentButtonText));
        OnPropertyChanged(nameof(DeploymentApplicationText));
        OnPropertyChanged(nameof(IsRunning));
    }
}
