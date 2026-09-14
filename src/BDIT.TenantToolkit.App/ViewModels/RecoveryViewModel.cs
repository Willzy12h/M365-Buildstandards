using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class RecoveryViewModel : PageViewModel
{
    private ChangeRegisterRow? _selected;
    private RecoveryPlan? _plan;
    private string _tenant = "", _typedTenant = "", _result = "";
    private bool _approved, _reviewedDrift;
    public RecoveryViewModel(ShellViewModel shell) : base(shell, "Undo and recovery")
    {
        LoadRegisterCommand = Command(LoadRegisterAsync, () => Workspace.Profile is not null && Workspace.Idle);
        PreviewDeleteCommand = Command(() => Preview(RecoveryAction.DeleteCreatedObject), CanPreview);
        PreviewRestoreCommand = Command(() => Preview(RecoveryAction.RestoreUpdate), CanPreview);
        PreviewDisableCommand = Command(() => Preview(RecoveryAction.DisableConditionalAccess), CanPreview);
        ExecuteCommand = Command(Execute, () => Workspace.IsDeploymentSession && Workspace.Idle && Plan is not null && Approved);
    }
    public ObservableCollection<ChangeRegisterRow> Changes { get; } = new();
    public ICommand LoadRegisterCommand { get; }
    public ICommand PreviewDeleteCommand { get; }
    public ICommand PreviewRestoreCommand { get; }
    public ICommand PreviewDisableCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ChangeRegisterRow? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) ClearApproval(); } }
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
            Changes.Clear(); foreach (var row in rows) Changes.Add(row);
            Selected = null;
        });
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
        Result = $"{run.Status} · write {run.WriteAcceptance} · verification {run.Verification}\n{run.Reason}\nRecovery record: {run.Id}";
        await LoadRegisterAsync();
    }
    public override void Refresh()
    {
        var tenant = Workspace.Session?.TenantId ?? Workspace.Profile?.TenantId ?? "";
        if (_tenant != tenant || !Workspace.IsConnected) { _tenant = tenant; Changes.Clear(); Selected = null; ClearApproval(); Result = ""; }
    }
}
