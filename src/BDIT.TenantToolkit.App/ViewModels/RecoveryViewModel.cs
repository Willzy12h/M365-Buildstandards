using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.ViewModels;

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
    public RecoveryViewModel(ShellViewModel shell) : base(shell, "Undo and recovery")
    {
        LoadRegisterCommand = Command(LoadRegisterAsync, () => Workspace.Profile is not null && Workspace.Idle);
        PreviewDeleteCommand = Command(() => Preview(RecoveryAction.DeleteCreatedObject), CanPreview);
        PreviewRestoreCommand = Command(() => Preview(RecoveryAction.RestoreUpdate), CanPreview);
        PreviewDisableCommand = Command(() => Preview(RecoveryAction.DisableConditionalAccess), CanPreview);
        ExecuteCommand = Command(Execute, () => Workspace.IsDeploymentSession && Workspace.Idle && Plan is not null && Approved);
        ReverifyDeploymentCommand = Command(() => Reverify(false), () => Workspace.IsConnected && Workspace.Idle && Selected is not null
            && (Selected.Acceptance == WriteAcceptance.Accepted || (Selected.Historical && HistoricalAcknowledged)));
        ReverifyRecoveryCommand = Command(() => Reverify(true), () => Workspace.IsConnected && Workspace.Idle && SelectedRecovery is not null);
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
    public RecoveryAttemptChoice? SelectedRecovery { get => _selectedRecovery; set => SetProperty(ref _selectedRecovery, value); }
    public bool HistoricalAcknowledged { get => _historicalAcknowledged; set => SetProperty(ref _historicalAcknowledged, value); }
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
        }
    }
    public RecoveryPlan? Plan { get => _plan; private set { SetProperty(ref _plan, value); OnPropertyChanged(nameof(PreviewText)); OnPropertyChanged(nameof(CurrentSettings)); OnPropertyChanged(nameof(RestorationSettings)); } }
    public bool Approved { get => _approved; set => SetProperty(ref _approved, value); }
    public bool ReviewedDrift { get => _reviewedDrift; set => SetProperty(ref _reviewedDrift, value); }
    public string TypedTenant { get => _typedTenant; set => SetProperty(ref _typedTenant, value); }
    public string Result { get => _result; private set => SetProperty(ref _result, value); }
    public string PreviewText => Plan is null ? "Select a recorded change and preview a recovery action. A complete fresh snapshot and current object read are required."
        : $"{Plan.Action} · {Plan.Name}\nObject {Plan.ObjectId} · tenant {Plan.TenantId}\n{Plan.Consequence}\nDrift: {(Plan.DriftDetected ? "REVIEW REQUIRED — current state differs or historical readback is unavailable" : "no change detected since recorded readback")}\nPreview expires five minutes after {Plan.CreatedAt}.";
    public string CurrentSettings => Plan?.CurrentObject.ToJsonString(ToolkitJson.Options) ?? "No recovery preview.";
    public string RestorationSettings => Plan?.Payload?.ToJsonString(ToolkitJson.Options) ?? "Deletion sends no settings payload. Inspect the current object and consequence.";
    private bool CanPreview() => Selected is not null && Workspace.IsDeploymentSession && Workspace.Idle;
    private void ClearApproval() { Plan = null; Approved = false; ReviewedDrift = false; TypedTenant = ""; }
    private async Task LoadRegisterAsync()
    {
        var tenant = Workspace.Profile?.TenantId ?? throw new ToolkitException("Select a tenant profile first.");
        await Workspace.RunExclusiveAsync("Reading the durable change register", async _ =>
        {
            var rows = await Task.Run(() => Workspace.Recovery.Register(tenant));
            _recoveryRuns = await Task.Run(() => Workspace.Evidence.LoadRecoveryRuns(tenant));
            Changes.Clear(); foreach (var row in rows) Changes.Add(row);
            Selected = null;
        });
    }
    private async Task Reverify(bool recovery)
    {
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
        var selected = Selected ?? throw new ToolkitException("Select a recorded change.");
        ClearApproval(); Result = "";
        Plan = await Workspace.PreviewRecoveryAsync(selected.RunId, selected.ControlId, action);
    }
    private async Task Execute()
    {
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
        var tenant = Workspace.Session?.TenantId ?? Workspace.Profile?.TenantId ?? "";
        if (_tenant != tenant || !Workspace.IsConnected) { _tenant = tenant; _recoveryRuns = Array.Empty<RecoveryRun>(); Changes.Clear(); Selected = null; RecoveryAttempts.Clear(); SelectedRecovery = null; ClearApproval(); Result = ""; }
    }
}
