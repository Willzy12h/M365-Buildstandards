using System.Collections.ObjectModel;
using System.Windows.Input;
using BuildStandard.Core;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

public sealed class ManualCheckRow
{
    public string ControlId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Status { get; init; } = "Pending";
    public string Note { get; init; } = "";
    public string RecordedAt { get; init; } = "";
    public string RecordedBy { get; init; } = "";
    public string Instructions { get; init; } = "";
}

public sealed class ManualChecksViewModel : PageViewModel
{
    private ManualCheckRow? _selected;
    private string _editStatus = "Pending";
    private string _editNote = "";
    private string _context = "";
    private bool _refreshing;

    public ManualChecksViewModel(ShellViewModel shell) : base(shell, "Manual checks")
    {
        SaveCommand = Sync(Save, () => Selected is not null && CurrentContext && Workspace.Idle && Rows.Any(r => ReferenceEquals(r, Selected)));
        Refresh();
    }

    public ICommand SaveCommand { get; }
    public ObservableCollection<ManualCheckRow> Rows { get; } = new();
    public ObservableCollection<string> Statuses { get; } = new() { "Pending", "Pass", "Fail", "Unknown" };

    public ManualCheckRow? Selected
    {
        get => _selected;
        set
        {
            if (_refreshing) return;
            if (value is not null && (!CurrentContext || !Rows.Any(r => ReferenceEquals(r, value)))) value = null;
            if (ReferenceEquals(_selected, value)) return;
            SetProperty(ref _selected, value);
            EditStatus = value?.Status ?? "Pending";
            EditNote = value?.Note ?? "";
            OnPropertyChanged(nameof(SelectedText));
        }
    }

    public string EditStatus { get => _editStatus; set => SetProperty(ref _editStatus, value); }
    public string EditNote { get => _editNote; set => SetProperty(ref _editNote, value); }
    public string SelectedText => Selected is null ? "Select a control to record what was checked and where the evidence is held. Never record passwords or recovery keys." : $"{Selected.ControlId} {Selected.Name}\n{Selected.Instructions}";
    public string ContextText => Workspace.Profile is null ? "Select a client on the Connect page." : $"Manual checks for {Workspace.Profile.Company}. Outcomes other than Pending require an evidence note.";

    private void Save()
    {
        if (!Workspace.Idle || !CurrentContext) throw new ToolkitException("Select the current client and standard, and wait for the active operation before saving a check.");
        var row = Selected ?? throw new ToolkitException("Select a control first.");
        if (!Rows.Any(r => ReferenceEquals(r, row))) throw new ToolkitException("Select this control again in the current client's register.");
        Workspace.SaveManualCheck(row.ControlId, EditStatus, EditNote);
        Selected = Rows.FirstOrDefault(r => r.ControlId == row.ControlId);
    }

    public override void Refresh()
    {
        var context = EvidenceReviewContext.For(Workspace);
        var changed = context != _context;
        var selectedId = changed ? null : Selected?.ControlId;
        _context = context;
        if (changed) { _selected = null; EditStatus = "Pending"; EditNote = ""; }
        _refreshing = true;
        try
        {
            Rows.Clear();
            if (Workspace.Standard is null) { _selected = null; return; }
            // A register that cannot be read - damaged, or recorded for another tenant - is reported, not thrown: this runs
            // on every workspace change, so throwing would surface as a failure of whatever the engineer just did.
            ManualCheckRegister register;
            try { register = Workspace.LoadManualChecks(); }
            catch (ToolkitException ex) { Shell.ShowError(ex); register = new ManualCheckRegister(); }
            foreach (var c in ControlInstances.All(Workspace.Standard, Workspace.Profile))
            {
                register.Checks.TryGetValue(c.Id, out var check);
                Rows.Add(new ManualCheckRow
                {
                    ControlId = c.Id,
                    Name = c.Name,
                    Category = c.Category,
                    Status = check?.Status ?? "Pending",
                    Note = check?.Note ?? "",
                    RecordedAt = check?.RecordedAt ?? "",
                    RecordedBy = check?.RecordedBy ?? "",
                    Instructions = c.Assessment.ManualInstructions.Length > 0 ? c.Assessment.ManualInstructions : c.DesiredState
                });
            }
            _selected = selectedId is null ? null : Rows.FirstOrDefault(r => r.ControlId == selectedId);
        }
        finally
        {
            _refreshing = false;
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(ContextText));
        }
    }

    private bool CurrentContext => _context.Length > 0 && _context == EvidenceReviewContext.For(Workspace);
}
