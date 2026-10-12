using System.Collections.ObjectModel;
using System.Windows.Input;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

public sealed record RecoveryAttemptChoice(string Id, string Summary);

public sealed class RecoveryViewModel : PageViewModel
{
    private ChangeRegisterRow? _selected;
    private RecoveryPlan? _plan;
    private string _tenant = "", _typedTenant = "", _result = "";
    private bool _approved, _reviewedDrift;
    private bool _historicalAcknowledged;
    private IReadOnlyList<RecoveryRun> _recoveryRuns = Array.Empty<RecoveryRun>();
    private RecoveryAttemptChoice? _selectedRecovery;
    private object? _connection;
    private string _context = "", _authority = "";
    public RecoveryViewModel(ShellViewModel shell) : base(shell, "Undo and recovery")
    {
        LoadRegisterCommand = Command(LoadRegisterAsync, () => Workspace.Profile is not null && Workspace.Idle);
        PreviewDeleteCommand = Command(() => Preview(RecoveryAction.DeleteCreatedObject), CanPreview);
        PreviewRestoreCommand = Command(() => Preview(RecoveryAction.RestoreUpdate), CanPreview);
        PreviewDisableCommand = Command(() => Preview(RecoveryAction.DisableConditionalAccess), CanPreview);
        ExecuteCommand = Command(Execute, () => CanApply);
        ReverifyDeploymentCommand = Command(() => Reverify(false), () => CanReverify(false));
        ReverifyRecoveryCommand = Command(() => Reverify(true), () => CanReverify(true));
    }
    public ObservableCollection<ChangeRegisterRow> Changes { get; } = new();
    public ICommand LoadRegisterCommand { get; }
    public ICommand PreviewDeleteCommand { get; }
    public ICommand PreviewRestoreCommand { get; }
    public ICommand PreviewDisableCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand ReverifyDeploymentCommand { get; }
    public ICommand ReverifyRecoveryCommand { get; }
    public ObservableCollection<RecoveryAttemptChoice> RecoveryAttempts { get; } = new();
    public RecoveryAttemptChoice? SelectedRecovery { get => _selectedRecovery; set { if (SetProperty(ref _selectedRecovery, value)) OnPropertyChanged(nameof(RecoveryReverifyGuidance)); } }
    public bool HistoricalAcknowledged { get => _historicalAcknowledged; set { if (SetProperty(ref _historicalAcknowledged, value)) OnPropertyChanged(nameof(DeploymentReverifyGuidance)); } }
    public ChangeRegisterRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            ClearApproval(); HistoricalAcknowledged = false; RecoveryAttempts.Clear();
            foreach (var run in _recoveryRuns.Where(r => r.SourceRunId == value?.RunId && r.ControlId == value?.ControlId && r.WriteAcceptance == WriteAcceptance.Accepted))
                RecoveryAttempts.Add(new(run.Id, $"{run.Action} · {run.Status} / {run.Verification} · {run.StartedAt} · {run.Id}"));
            SelectedRecovery = RecoveryAttempts.FirstOrDefault();
            RaiseAll();
        }
    }
    public RecoveryPlan? Plan { get => _plan; private set { SetProperty(ref _plan, value); OnPropertyChanged(nameof(PreviewText)); OnPropertyChanged(nameof(CurrentSettings)); OnPropertyChanged(nameof(RestorationSettings)); OnPropertyChanged(nameof(ExecuteGuidance)); } }
    public bool Approved { get => _approved; set { if (SetProperty(ref _approved, value)) OnPropertyChanged(nameof(ExecuteGuidance)); } }
    public bool ReviewedDrift { get => _reviewedDrift; set => SetProperty(ref _reviewedDrift, value); }
    public string TypedTenant { get => _typedTenant; set => SetProperty(ref _typedTenant, value); }
    public string Result { get => _result; private set => SetProperty(ref _result, value); }
    public string PreviewText => Plan is null ? "Select a recorded change and preview a recovery action. A complete fresh snapshot and current object read are required."
        : $"{Plan.Action} · {Plan.Name}\nObject {Plan.ObjectId} · tenant {Plan.TenantId}\n{Plan.Consequence}\nDrift: {(Plan.DriftDetected ? "REVIEW REQUIRED — current state differs or historical readback is unavailable" : "no change detected since recorded readback")}\nPreview expires five minutes after {Plan.CreatedAt}.";
    public string CurrentSettings => Plan?.CurrentObject.ToJsonString(ToolkitJson.Options) ?? "No recovery preview.";
    public string RestorationSettings => Plan?.Payload?.ToJsonString(ToolkitJson.Options) ?? "Deletion sends no settings payload. Inspect the current object and consequence.";
    private bool VerifiedConnection => Workspace.Session is { TenantVerified: true, OperatorVerified: true } session
        && ProfileValidator.IsGuid(session.OperatorObjectId ?? "") && ProfileValidator.IsGuid(session.ClientId)
        && string.Equals(session.TenantId, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase);
    private bool CurrentSelection => _context.Length > 0 && _context == EvidenceReviewContext.For(Workspace)
        && Selected is not null && Changes.Any(row => ReferenceEquals(row, Selected));
    private bool CanPreview() => CurrentSelection && VerifiedConnection && Workspace.IsDeploymentSession && Workspace.Idle;
    private bool CanApply => CurrentSelection && VerifiedConnection && Workspace.IsDeploymentSession
        && Workspace.ExperimentalChangesEnabled && Workspace.Idle && Plan is not null && Approved;
    private bool CanReverify(bool recovery) => VerifiedConnection && Workspace.Idle && CurrentSelection
        && (recovery ? SelectedRecovery is not null && RecoveryAttempts.Any(r => ReferenceEquals(r, SelectedRecovery))
            : Selected!.Acceptance == WriteAcceptance.Accepted || (Selected.Historical && HistoricalAcknowledged));
    public string PreviewGuidance => !Workspace.Idle ? "Wait for the current operation to finish."
        : Workspace.Profile is null ? "Select a saved client on Connect to load its local change register."
        : !Workspace.IsDeploymentSession ? "Local register review is available offline. Connect with verified deployment access to preview recovery using fresh evidence."
        : !VerifiedConnection ? "Reconnect and verify the tenant, operator and application before previewing recovery."
        : Selected is null ? "Load the change register and select the exact recorded change to preview."
        : "Preview reads a fresh complete capture and the current object. Review its exact consequences before applying recovery.";
    public string DeploymentReverifyGuidance => !Workspace.Idle ? "Wait for the current operation to finish."
        : !Workspace.IsConnected ? "Connect read-only to re-verify an accepted recorded write with fresh GET requests."
        : !VerifiedConnection ? "Reconnect read-only and verify the tenant, operator and application before re-verification."
        : Selected is null ? "Select an accepted recorded deployment, or acknowledge the selected historical completion."
        : Selected.Acceptance != WriteAcceptance.Accepted && !(Selected.Historical && HistoricalAcknowledged)
            ? "Unknown modern writes cannot be re-verified as accepted. Retain the record and follow unresolved-write guidance; historical completion needs its explicit acknowledgement."
        : "Ready for fresh read-only verification. This sends no tenant writes and needs no experimental opt-in.";
    public string RecoveryReverifyGuidance => !Workspace.Idle ? "Wait for the current operation to finish."
        : !Workspace.IsConnected ? "Connect read-only to re-verify an accepted recovery attempt with fresh GET requests."
        : !VerifiedConnection ? "Reconnect read-only and verify the tenant, operator and application before re-verification."
        : SelectedRecovery is null ? "Select a recorded change and an accepted recovery attempt."
        : "Ready for fresh read-only recovery verification. This sends no tenant writes.";
    public string ExecuteGuidance => !Workspace.Idle ? "Wait for the current operation to finish."
        : !Workspace.IsDeploymentSession ? "Connect with verified deployment access before applying recovery."
        : !VerifiedConnection ? "Reconnect and verify the tenant, operator and application, then preview the action again."
        : !Workspace.ExperimentalChangesEnabled ? Workspace.ExperimentalChangeGuidance
        : Plan is null ? "Preview the exact recovery action using fresh evidence first."
        : !Approved ? "Review the displayed object and consequences, then tick the exact-action approval."
        : "Type the tenant ID and explicitly review any drift. The engine repeats identity, evidence and expiry checks before writing.";
    private void ClearApproval() { Plan = null; Approved = false; ReviewedDrift = false; TypedTenant = ""; }
    private async Task LoadRegisterAsync()
    {
        var tenant = Workspace.Profile?.TenantId ?? throw new ToolkitException("Select a tenant profile first.");
        await Workspace.RunExclusiveAsync("Reading the durable change register", async _ =>
        {
            var rows = await Task.Run(() => Workspace.Recovery.Register(tenant));
            var runs = await Task.Run(() => Workspace.Evidence.LoadRecoveryRuns(tenant));
            if (!string.Equals(tenant, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase))
                throw new ToolkitException("The selected client changed while reading the register. Load the current client's register again.");
            _recoveryRuns = runs;
            Changes.Clear(); foreach (var row in rows) Changes.Add(row);
            Selected = null;
        });
    }
    private async Task Reverify(bool recovery)
    {
        if (!CanReverify(recovery))
            throw new ToolkitException(recovery ? RecoveryReverifyGuidance : DeploymentReverifyGuidance);
        RequireCurrentSelection();
        var selected = Selected ?? throw new ToolkitException("Select a recorded change.");
        var id = recovery ? SelectedRecovery?.Id ?? throw new ToolkitException("Select an accepted recovery attempt.") : selected.RunId;
        var historical = HistoricalAcknowledged;
        ClearApproval();
        var record = await Workspace.ReverifyAsync(id, recovery ? null : selected.ControlId, historical);
        Result = record is null ? "Re-verification stopped before completion. No tenant write was sent."
            : $"Re-verification {(record.Verified ? "passed" : "incomplete")} · {record.Detail}\nEvidence: {record.Id}";
        await LoadRegisterAsync();
    }
    private async Task Preview(RecoveryAction action)
    {
        if (!CanPreview()) throw new ToolkitException(PreviewGuidance);
        RequireCurrentSelection();
        var selected = Selected ?? throw new ToolkitException("Select a recorded change.");
        ClearApproval(); Result = "";
        Plan = await Workspace.PreviewRecoveryAsync(selected.RunId, selected.ControlId, action);
    }
    private async Task Execute()
    {
        if (!CanApply)
            throw new ToolkitException(ExecuteGuidance);
        RequireCurrentSelection();
        var plan = Plan ?? throw new ToolkitException("Preview recovery first.");
        var tenant = TypedTenant; var approved = Approved; var drift = ReviewedDrift;
        ClearApproval();
        var run = await Workspace.ExecuteRecoveryAsync(plan, tenant, approved, drift);
        // Stopping cancels the operation, so the service may return nothing. Whether a write reached the tenant is then
        // unknown here; the register reloaded below is the record, and the recovery must not be repeated until it is read.
        Result = run is null
            ? "Recovery was stopped before it returned a result. Review this change in the register below before doing anything else; do not repeat the recovery until you have."
            : $"{run.Status} · write {run.WriteAcceptance} · verification {run.Verification}\n{run.Reason}\nRecovery record: {run.Id}";
        await LoadRegisterAsync();
    }
    public override void Refresh()
    {
        var tenant = (Workspace.Profile?.TenantId ?? "").Trim().ToLowerInvariant();
        var context = EvidenceReviewContext.For(Workspace);
        var session = Workspace.Session;
        var authority = session is null ? "" : string.Join("|", session.TenantId, session.AccountObjectId, session.OperatorObjectId,
            session.ClientId, session.Mode, session.TenantVerified, session.OperatorVerified, session.TokenExpiresAt,
            string.Join(",", session.Scopes.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)));
        if (_tenant != tenant)
        {
            _tenant = tenant; _recoveryRuns = Array.Empty<RecoveryRun>(); Changes.Clear(); Selected = null;
            RecoveryAttempts.Clear(); SelectedRecovery = null; Result = "";
        }
        if (!Workspace.IsConnected || !ReferenceEquals(_connection, Workspace.Connection) || _authority != authority || _context != context)
        { ClearApproval(); HistoricalAcknowledged = false; }
        _connection = Workspace.Connection; _authority = authority; _context = context;
        OnPropertyChanged(nameof(PreviewGuidance)); OnPropertyChanged(nameof(DeploymentReverifyGuidance));
        OnPropertyChanged(nameof(RecoveryReverifyGuidance)); OnPropertyChanged(nameof(ExecuteGuidance));
    }

    private void RequireCurrentSelection()
    {
        if (!CurrentSelection)
            throw new ToolkitException("Load and select the recorded change again for the current client and standard.");
    }
}
