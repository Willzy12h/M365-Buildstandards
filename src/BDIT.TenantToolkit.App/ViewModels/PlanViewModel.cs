using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class ControlSelection : ObservableObject
{
    private bool _isSelected;
    public string ControlId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public bool Eligible { get; init; }
    public string Status { get; init; } = "";
    public string Explanation { get; init; } = "";
    public string SafeState { get; init; } = "";
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

public sealed class PlanViewModel : PageViewModel
{
    private PlanRow? _selectedRow;
    private ControlSelection? _selectedControl;

    public PlanViewModel(ShellViewModel shell) : base(shell, "Plan changes")
    {
        SelectEligibleCommand = Sync(() => { foreach (var c in Controls) c.IsSelected = c.Eligible; });
        ClearSelectionCommand = Sync(() => { foreach (var c in Controls) c.IsSelected = false; });
        BuildPlanCommand = Sync(BuildPlan, () => Workspace.IsConnected && Workspace.SnapshotIsLive && Workspace.Idle);
        Refresh();
    }

    public ICommand SelectEligibleCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand BuildPlanCommand { get; }

    public ObservableCollection<ControlSelection> Controls { get; } = new();
    public ObservableCollection<PlanRow> Rows { get; } = new();
    public ControlSelection? SelectedControl
    {
        get => _selectedControl;
        set { SetProperty(ref _selectedControl, value); OnPropertyChanged(nameof(SelectionPrerequisites)); }
    }
    public IReadOnlyList<ControlPrerequisite>? SelectionPrerequisites => Workspace.Standard?.FindControl(SelectedControl?.ControlId ?? "")?.Prerequisites;
    public IReadOnlyList<ControlPrerequisite>? RowPrerequisites => Workspace.Standard?.FindControl(SelectedRow?.ControlId ?? "")?.Prerequisites;

    public PlanRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            SetProperty(ref _selectedRow, value);
            OnPropertyChanged(nameof(RowDetail));
            OnPropertyChanged(nameof(PayloadJson));
            OnPropertyChanged(nameof(BeforeJson));
            OnPropertyChanged(nameof(ExclusionsText));
            OnPropertyChanged(nameof(RowPrerequisites));
        }
    }

    public string RowDetail => SelectedRow is null ? "Select a plan row to see the exact proposed change." :
        (SelectedRow.UsesDefaultInputs ? "USES A SHIPPED DEFAULT — confirm the values below with the client before assigning or enabling this policy.\n\n" : "") +
        $"{SelectedRow.ControlId} {SelectedRow.Name} · {SelectedRow.Action}\n{SelectedRow.Reason}\nSafe state written: {SelectedRow.SafeState} · Expected production: {SelectedRow.ExpectedProductionState} / {SelectedRow.ExpectedProductionAssignment}" +
        (SelectedRow.OperatorExclusion is null ? "" : $"\nOperator excluded: {SelectedRow.OperatorExclusion.UserPrincipalName} [{SelectedRow.OperatorExclusion.ObjectId}]") +
        (SelectedRow.Warnings.Count == 0 ? "" : "\n" + string.Join("\n", SelectedRow.Warnings.Select(w => "Warning: " + w)));
    public string PayloadJson => SelectedRow?.Payload?.ToJsonString(ToolkitJson.Options) ?? "No automated change";
    public string BeforeJson => SelectedRow?.Before?.ToJsonString(ToolkitJson.Options) ?? "No existing object will be modified";
    public string ExclusionsText
    {
        get
        {
            if (SelectedRow?.Collection != "conditionalAccess") return "User exclusions apply to Conditional Access candidates. Intune candidates remain unassigned.";
            var users = SelectedRow.Payload?["conditions"]?["users"]?["excludeUsers"] as System.Text.Json.Nodes.JsonArray;
            if (users is null) return "No candidate user exclusions to display.";
            var lines = new List<string> { "These stored exclusions continue to apply if this policy is later enabled:" };
            foreach (var node in users)
            {
                var id = node?.GetValue<string>() ?? "";
                var metadata = Workspace.Profile?.ExclusionAccounts.FirstOrDefault(a => string.Equals(a.ObjectId, id, StringComparison.OrdinalIgnoreCase));
                var captured = Workspace.Snapshot?.Collections.GetValueOrDefault("users")?.Items.FirstOrDefault(u => u["id"]?.GetValue<string>() == id);
                var label = metadata?.UserPrincipalName ?? captured?["userPrincipalName"]?.GetValue<string>() ?? id;
                var reason = metadata?.Reason ?? (id == Workspace.Session?.OperatorObjectId ? "Delegated creator safeguard" : "Configured emergency or standard exclusion");
                lines.Add($"{label} [{id}] — {reason}");
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    public string PlanText
    {
        get
        {
            var p = Workspace.Plan;
            if (p is null) return Workspace.SnapshotIsLive ? "Select controls to prepare a plan" : "Capture the live tenant before planning";
            return $"{p.Rows.Count(r => r.Action == PlanAction.Create)} to create · {p.Rows.Count(r => r.Action == PlanAction.Update)} to update · {p.Rows.Count(r => !r.IsWrite)} without a write";
        }
    }

    public string ContextText
    {
        get
        {
            if (!Workspace.IsConnected) return "Not connected.";
            var mode = Workspace.IsDeploymentSession ? "deployment access" : "read-only assessment";
            return $"{Controls.Count(c => c.IsSelected)} control(s) selected · {mode}. Build the plan in the session used to deploy.";
        }
    }

    private void BuildPlan()
    {
        var ids = Controls.Where(c => c.IsSelected).Select(c => c.ControlId).ToList();
        if (ids.Count == 0) throw new ToolkitException("Select at least one control.");
        Workspace.BuildPlan(ids);
    }

    private void OnSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControlSelection.IsSelected)) OnPropertyChanged(nameof(ContextText));
    }

    public override void Refresh()
    {
        var selectedControlId = SelectedControl?.ControlId;
        var selected = Controls.Where(c => c.IsSelected).Select(c => c.ControlId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Controls.Clear();
        var standard = Workspace.Standard;
        var assessment = Workspace.Assessment;
        if (standard is not null)
        {
            foreach (var control in ControlInstances.All(standard, Workspace.Profile))
            {
                var def = standard.FindCollection(control.Collection);
                var finding = assessment?.Findings.FirstOrDefault(f => string.Equals(f.ControlId, control.Id, StringComparison.OrdinalIgnoreCase));
                string status, explanation;
                var eligible = false;
                if (!control.HasRecipe || def is null || !def.Writable)
                {
                    status = "Manual";
                    explanation = control.Assessment.ManualInstructions.Length > 0 ? control.Assessment.ManualInstructions : "No automated recipe; follow the build standard manually.";
                }
                else if (finding is null)
                {
                    status = "Assess first";
                    explanation = "Read the tenant configuration and review the assessment before planning.";
                }
                else if (finding.Status is FindingStatus.Missing || (finding.Owned && finding.Status is FindingStatus.SettingsMatchNotEnforced or FindingStatus.PartialMatch or FindingStatus.Compliant))
                {
                    status = "Candidate";
                    eligible = true;
                    explanation = finding.Status == FindingStatus.Missing
                        ? (control.Collection == "conditionalAccess" ? "Create a disabled candidate with exclusions; no activation." : "Create an unassigned candidate; no assignment.")
                        : "Toolkit-created object; an inactive update or no change will be proposed.";
                }
                else
                {
                    status = StatusLabels.For(finding.Status);
                    explanation = finding.Reason;
                }
                var selection = new ControlSelection
                {
                    ControlId = control.Id,
                    Name = control.Name,
                    Category = control.Category,
                    Eligible = eligible,
                    Status = status,
                    Explanation = explanation,
                    SafeState = control.SafeDeployment.State,
                    IsSelected = eligible && selected.Contains(control.Id)
                };
                // The selected count in ContextText is derived from these rows, so it must follow every tick and untick.
                selection.PropertyChanged += OnSelectionChanged;
                Controls.Add(selection);
            }
        }
        Rows.Clear();
        if (Workspace.Plan is not null) foreach (var r in Workspace.Plan.Rows) Rows.Add(r);
        SelectedRow = null;
        SelectedControl = Controls.FirstOrDefault(c => c.ControlId == selectedControlId);
        OnPropertyChanged(nameof(PlanText));
        OnPropertyChanged(nameof(ContextText));
    }
}
