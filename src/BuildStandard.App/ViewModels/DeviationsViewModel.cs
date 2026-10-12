using System.Collections.ObjectModel;
using System.Windows.Input;
using BuildStandard.Core;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

public sealed class DeviationsViewModel : PageViewModel
{
    private Deviation? _selected;
    private string _controlId = "", _kind = "ApprovedDeviation", _reason = "", _approvedState = "", _owner = "", _approvedBy = "", _reviewBy = "", _reference = "", _notes = "";
    private string _context = "", _draftContext = "";
    private bool _refreshing;

    public DeviationsViewModel(ShellViewModel shell) : base(shell, "Deviations")
    {
        NewCommand = Sync(ResetForm);
        SaveCommand = Sync(Save, () => CurrentContext && _draftContext == _context && Workspace.Idle);
        DeleteCommand = Sync(Delete, () => CurrentContext && Selected is not null && Workspace.Idle && Items.Any(d => ReferenceEquals(d, Selected)));
        ReviewBy = DateOnly.FromDateTime(DateTime.Today).AddMonths(6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Refresh();
    }

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }

    public ObservableCollection<Deviation> Items { get; } = new();
    public ObservableCollection<string> ControlIds { get; } = new();
    public IReadOnlyList<FilterOption> Kinds { get; } = new[]
    {
        new FilterOption(nameof(DeviationKind.ApprovedDeviation), "Approved deviation"),
        new FilterOption(nameof(DeviationKind.NotApplicable), "Not applicable")
    };

    public Deviation? Selected
    {
        get => _selected;
        set
        {
            if (_refreshing) return;
            if (value is not null && (!CurrentContext || !Items.Any(d => ReferenceEquals(d, value))
                || !string.Equals(value.TenantId, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase)))
            { ResetForm(); return; }
            if (!SetProperty(ref _selected, value) || value is null) return;
            _draftContext = _context;
            ControlId = value.ControlId; Kind = value.Kind.ToString(); Reason = value.Reason; ApprovedState = value.ApprovedState; Owner = value.Owner;
            ApprovedBy = value.ApprovedBy; ReviewBy = value.ReviewBy; Reference = value.Reference; Notes = value.Notes;
        }
    }

    public string ControlId { get => _controlId; set { if (!_refreshing) SetProperty(ref _controlId, value); } }
    public string Kind { get => _kind; set => SetProperty(ref _kind, value); }
    public string Reason { get => _reason; set => SetProperty(ref _reason, value); }
    public string ApprovedState { get => _approvedState; set => SetProperty(ref _approvedState, value); }
    public string Owner { get => _owner; set => SetProperty(ref _owner, value); }
    public string ApprovedBy { get => _approvedBy; set => SetProperty(ref _approvedBy, value); }
    public string ReviewBy { get => _reviewBy; set => SetProperty(ref _reviewBy, value); }
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }

    public string StatusText => Workspace.Profile is null ? "Select a client on the Connect page to manage its deviation register."
        : Items.Count == 0 ? $"No deviations recorded for {Workspace.Profile.Company}."
        : $"{Items.Count} deviation(s) recorded for {Workspace.Profile.Company}; {Items.Count(i => i.IsReviewOverdue(DateTimeOffset.UtcNow))} overdue for review.";

    private void ResetForm()
    {
        _draftContext = EvidenceReviewContext.For(Workspace);
        _selected = null;
        OnPropertyChanged(nameof(Selected));
        ControlId = ""; Kind = "ApprovedDeviation"; Reason = ""; ApprovedState = ""; Owner = ""; ApprovedBy = Workspace.Session?.Account ?? ""; Reference = ""; Notes = "";
        ReviewBy = DateOnly.FromDateTime(DateTime.Today).AddMonths(6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void Save()
    {
        RequireCurrentDraft();
        if (string.IsNullOrWhiteSpace(ControlId)) throw new ToolkitException("Select a control.");
        Workspace.SaveDeviation(new Deviation
        {
            Id = Selected?.Id ?? "",
            ControlId = ControlId.Trim(),
            Kind = Kind == "NotApplicable" ? DeviationKind.NotApplicable : DeviationKind.ApprovedDeviation,
            Reason = Reason.Trim(),
            ApprovedState = ApprovedState.Trim(),
            Owner = Owner.Trim(),
            ApprovedBy = ApprovedBy.Trim(),
            ReviewBy = ReviewBy.Trim(),
            Reference = Reference.Trim(),
            Notes = Notes.Trim()
        });
        ResetForm();
    }

    private void Delete()
    {
        RequireCurrentDraft();
        var d = Selected ?? throw new ToolkitException("Select a deviation first.");
        var confirm = System.Windows.MessageBox.Show($"Remove the deviation for {d.ControlId}?", "Deviation register", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;
        Workspace.DeleteDeviation(d.ControlId);
        ResetForm();
    }

    public override void Refresh()
    {
        var context = EvidenceReviewContext.For(Workspace);
        var changed = context != _context;
        var selectedId = changed ? null : Selected?.Id;
        _context = context;
        if (changed) ResetForm();
        _refreshing = true;
        try
        {
            Items.Clear();
            ControlIds.Clear();
            if (Workspace.Standard is not null) foreach (var c in Workspace.Standard.Controls) ControlIds.Add(c.Id);
            try { foreach (var d in Workspace.LoadDeviations().OrderBy(d => d.ControlId, StringComparer.OrdinalIgnoreCase)) Items.Add(d); }
            catch (ToolkitException ex) { Shell.ShowError(ex); }
            _selected = selectedId is null ? null : Items.FirstOrDefault(d => d.Id == selectedId);
            if (ApprovedBy.Length == 0 && Workspace.Session is not null) ApprovedBy = Workspace.Session.Account;
        }
        finally { _refreshing = false; OnPropertyChanged(nameof(Selected)); OnPropertyChanged(nameof(ControlId)); }
        OnPropertyChanged(nameof(StatusText));
    }

    private bool CurrentContext => _context.Length > 0 && _context == EvidenceReviewContext.For(Workspace);
    private void RequireCurrentDraft()
    {
        if (!Workspace.Idle || !CurrentContext || _draftContext != _context)
            throw new ToolkitException("Start this exception again for the current client and standard after the active operation finishes.");
        if (Selected is { } selected && (!Items.Any(d => ReferenceEquals(d, selected))
            || !string.Equals(selected.TenantId, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase)))
            throw new ToolkitException("Select this exception again in the current client's register.");
    }
}
