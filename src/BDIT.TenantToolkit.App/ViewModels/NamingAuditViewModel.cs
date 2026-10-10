using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Naming;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>Reviews the exact workspace capture and existing ownership evidence; never reads or renames tenant objects.</summary>
public sealed class NamingAuditViewModel : PageViewModel
{
    public sealed record Row(NamingAudit.ObjectResult Source)
    {
        public string Name => Source.Name.Length == 0 ? "Unknown / not returned" : Source.Name;
        public string Collection => Source.Collection;
        public string ObjectId => Source.ObjectId;
        public string Ownership => Source.Ownership;
        public string OwnershipReason => Source.OwnershipReason;
        public string NamingState => Source.Naming.State;
        public string NamingReason => Source.Naming.Reason;
        public string Details => "Name: " + Name + "\nObject ID: " + ObjectId + "\nCollection: " + Collection
            + "\nOwnership: " + Ownership + "\n" + OwnershipReason + "\nNaming: " + NamingState + "\n" + NamingReason;
    }
    private readonly GraphReportsController _contextGuard;
    private string _context = "", _source = "", _search = "", _ownership = "All", _collection = "All";
    private NamingAudit.Result? _result;
    private Row? _selected;
    private string SourceKey => ToolkitJson.Serialize(Workspace.Snapshot);

    public NamingAuditViewModel(ShellViewModel shell) : base(shell, "Naming audit")
    {
        _contextGuard = new(Workspace);
        ReviewCommand = Sync(Review, () => Workspace.Idle && Workspace.Snapshot is not null && Workspace.Profile is not null);
        CopyDetailsCommand = CopyText(() => SelectedDetails);
        CopyProvenanceCommand = CopyText(() => Provenance);
        Refresh();
    }
    public ICommand ReviewCommand { get; }
    public ICommand CopyDetailsCommand { get; }
    public ICommand CopyProvenanceCommand { get; }
    public ObservableCollection<Row> Rows { get; } = new();
    public ObservableCollection<NamingAudit.CollectionResult> Collections { get; } = new();
    public ObservableCollection<string> CollectionFilters { get; } = new() { "All" };
    public IReadOnlyList<string> OwnershipFilters { get; } = ["All", NamingAudit.Managed, NamingAudit.Unmapped, NamingAudit.OwnershipUnknown];
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Filter(); } }
    public string OwnershipFilter { get => _ownership; set { if (SetProperty(ref _ownership, value)) Filter(); } }
    public string CollectionFilter { get => _collection; set { if (SetProperty(ref _collection, value)) Filter(); } }
    public Row? SelectedRow { get => _selected; set { if (SetProperty(ref _selected, value)) OnPropertyChanged(nameof(SelectedDetails)); } }
    public string SelectedDetails => SelectedRow?.Details ?? "Select an object to inspect and copy its exact ID, ownership proof and naming reason.";
    public string Guidance => Workspace.Profile is null ? "Select a client in Connect first."
        : Workspace.Snapshot is null ? "Open a stored capture in Configuration, or deliberately capture there first. Naming audit performs no collection."
        : "Review the selected capture and its stored mappings/runs. Names alone cannot prove toolkit ownership. No objects are renamed.";
    public string Summary => _result is null ? "No naming audit accepted for the current capture."
        : $"{Rows.Count} of {_result.Objects.Count} objects visible · capture {_result.CapturedAt} · integrity {_result.SourceIntegrity}. Historical naming and ownership only; no live state inferred.";
    public string Provenance => _result is null ? "No reviewed capture." : "Tenant ID: " + _result.TenantId + "\nCapture ID: " + _result.CaptureId
        + "\nCaptured at: " + _result.CapturedAt + "\nSource integrity: " + _result.SourceIntegrity
        + "\nReview uses the exact supplied workspace snapshot, local mappings and stored runs. Unreadable run records are omitted by the existing evidence loader; absence of corroboration cannot prove ownership. No live reads, renames or history edits.";

    public void Review()
    {
        _contextGuard.RequireContext(_context);
        if (!Workspace.Idle) throw new ToolkitException("Wait for the current operation to finish.");
        var snapshot = Workspace.Snapshot ?? throw new ToolkitException(Guidance);
        if (_source != SourceKey) throw new ToolkitException("The selected capture changed. Refresh before reviewing it.");
        if (!string.Equals(snapshot.TenantId, Workspace.Profile!.TenantId, StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("The selected capture belongs to another client.");
        Clear();
        var result = NamingAudit.Review(snapshot, Workspace.Evidence.LoadMappings(snapshot.TenantId), Workspace.Evidence.LoadRuns(snapshot.TenantId));
        _contextGuard.RequireContext(_context);
        if (_source != SourceKey) throw new ToolkitException("The capture changed during review; no audit accepted.");
        _result = result;
        foreach (var collection in result.Collections) { Collections.Add(collection); CollectionFilters.Add(collection.Collection); }
        Filter(); NotifyPage();
    }
    private void Clear()
    {
        _result = null; Rows.Clear(); Collections.Clear(); CollectionFilters.Clear(); CollectionFilters.Add("All"); SelectedRow = null;
    }
    private void Filter()
    {
        Rows.Clear(); SelectedRow = null;
        if (_result is not null)
            foreach (var item in _result.Objects.Where(o => (OwnershipFilter == "All" || o.Ownership == OwnershipFilter)
                && (CollectionFilter == "All" || o.Collection == CollectionFilter)
                && (Search.Length == 0 || new[] { o.Name, o.ObjectId, o.Collection, o.Ownership, o.OwnershipReason, o.Naming.Reason }.Any(t => t.Contains(Search, StringComparison.OrdinalIgnoreCase))))) Rows.Add(new(item));
        OnPropertyChanged(nameof(Summary));
    }
    public override void Refresh()
    {
        if (_contextGuard is null) return;
        if (_context != _contextGuard.ContextKey || _source != SourceKey)
        { _context = _contextGuard.ContextKey; _source = SourceKey; Clear(); }
        NotifyPage();
    }
    private void NotifyPage()
    {
        OnPropertyChanged(nameof(Guidance)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Provenance)); OnPropertyChanged(nameof(SelectedDetails));
        CommandManager.InvalidateRequerySuggested();
    }
}
