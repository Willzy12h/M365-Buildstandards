using System.Windows;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Engine.Reports;
namespace BuildStandard.App.ViewModels;

/// <summary>Exact incident/catalogue references only. No response, execution, assessment or completion authority.</summary>
public sealed class IncidentReferencesViewModel : PageViewModel
{
    private readonly GraphReportsController _controller;
    private OperationalContextFile? _current, _referenceSource;
    private string _context = "";
    private bool _dirty, _opening;
    private IReadOnlyList<IncidentControlReference> _links = [];
    public sealed record IncidentChoice(string Id, string Label);
    public sealed record ReferenceDisplay(string IncidentId, string ControlId, string StandardRelease, string StandardSha256, string Resolution);
    public IncidentReferencesViewModel(ShellViewModel shell, GraphReportsController controller) : base(shell,"Incident control references")
    {
        _controller = controller;
        AddCommand = Sync(AddReference, () => CanAdd && SelectedIncidentId.Length > 0 && SelectedControlId.Length > 0);
        SaveCommand = Command(ExportReferencesAsync, () => Workspace.Idle && !_opening && _referenceSource is not null && _links.Count > 0);
        OpenCommand = Command(async () =>
        {
            RequireCurrent(); if (_dirty) throw new ToolkitException("Save or explicitly discard unsaved references before replacing them.");
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Incident control references (*.json)|*.json", Title = "Open exact source-bound incident references" };
            if (dialog.ShowDialog() == true) await OpenReferencesAsync(dialog.FileName);
        }, () => CanAdd && !_dirty);
        DiscardCommand = Sync(() =>
        {
            if (!Workspace.Idle || _opening) throw new ToolkitException("Wait for the local reference operation.");
            if (MessageBox.Show("Discard unsaved incident references? Original incident evidence and saved files are retained.","Discard unsaved references",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _links = []; Rows = []; _dirty = false; Notify();
        }, () => Workspace.Idle && !_opening && _dirty);
    }
    public bool RequiresCloseReview => _dirty || _opening;
    public bool CanAdd => Workspace.Idle && !_opening && _current is not null && _context == _controller.ContextKey;
    public string Guidance => _opening || !Workspace.Idle ? "Wait for the local operation."
        : _current is null || _context != _controller.ContextKey ? "Open incident evidence for the current client first. Older unsaved links retain their original source and can still be saved."
        : "Select one unambiguous incident and an exact control in the loaded verified catalogue. References do not establish remediation, compliance, ownership or job completion.";
    public string Summary => _referenceSource is null ? "No incident references."
        : $"{_links.Count} reference(s) · {(_dirty ? "UNSAVED" : "saved or opened")} · source tenant {_referenceSource.Report.TenantId} · original SHA-256 {(_links.FirstOrDefault()?.SourceSha256 ?? "no links")}. Historical targets are never rebound by name.";
    public string LastExport { get; private set; } = "";
    public IReadOnlyList<IncidentChoice> Incidents => _current?.Report.Sections.Single().Rows.GroupBy(row => row["id"]!.GetValue<string>(),StringComparer.Ordinal)
        .Where(group => group.Count() == 1).Select(group => new IncidentChoice(group.Key,(group.First()["name"]?.GetValue<string>() ?? "Unknown incident name") + " · " + group.Key)).ToArray() ?? [];
    public IReadOnlyList<ExternalCheckResultsViewModel.ControlChoice> Controls => Workspace.Standard?.Controls.Select(control => new ExternalCheckResultsViewModel.ControlChoice(control.Id,control.Name + " · " + control.Id)).ToArray() ?? [];
    public string SelectedIncidentId { get; set; } = "";
    public string SelectedControlId { get; set; } = "";
    public int ReferenceCount => _links.Count;
    public IReadOnlyList<ReferenceDisplay> Rows { get; private set; } = [];
    public ICommand AddCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand DiscardCommand { get; }
    internal void Admit(OperationalContextFile source, string context)
    {
        _controller.RequireContext(context);
        if (_dirty) throw new ToolkitException("Save or explicitly discard unsaved incident references before replacing their source.");
        if (source.Report.ReportId != "security-incidents") throw new ConfigurationException("Choose incident context evidence first.");
        _current = source; _referenceSource = null; _context = context; _links = []; Rows = []; LastExport = "";
        SelectedIncidentId = ""; SelectedControlId = ""; Notify();
    }
    internal void ClearCurrent() { _current = null; SelectedIncidentId = ""; SelectedControlId = ""; Notify(); }
    private OperationalContextFile RequireCurrent()
    {
        if (!CanAdd) throw new ToolkitException(Guidance);
        _controller.RequireContext(_context); return _current!;
    }
    public void AddReference()
    {
        var source = RequireCurrent(); var standard = Workspace.Standard ?? throw new ToolkitException("Load a verified catalogue first.");
        var link = IncidentControlReferences.Create(Workspace.Paths,source,SelectedIncidentId,standard,SelectedControlId);
        var proposed = _links.Append(link).ToArray(); _ = IncidentControlReferenceDocument.Write(source,proposed);
        _links = proposed; _referenceSource = source; _dirty = true;
        Rows = Rows.Append(new ReferenceDisplay(link.IncidentId,link.ControlId,link.StandardRelease,link.StandardSha256,"Resolved against exact verified catalogue")).ToArray(); Notify();
    }
    public async Task ExportReferencesAsync()
    {
        if (!Workspace.Idle || _opening || _referenceSource is null || _links.Count == 0) throw new ToolkitException("No source-bound incident references are available, or an operation is active.");
        var source = _referenceSource; var links = _links.ToArray();
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportIncidentControlReferences(source,links,source.Report.TenantId),"Saving incident control references","Keeping exact original source/catalogue identities. This changes no assessment or tenant state.");
        _dirty = false; LastExport = file; Notify();
    }
    public async Task OpenReferencesAsync(string file)
    {
        var source = RequireCurrent(); if (_dirty) throw new ToolkitException("Save or explicitly discard unsaved references first.");
        var context = _context; var standard = Workspace.Standard; _opening = true; Notify();
        try
        {
            var result = await Task.Run(() =>
            {
                var links = IncidentControlReferenceDocument.ReadFile(file,source);
                var rows = links.Select(link =>
                {
                    var resolved = IncidentControlReferences.Resolve(Workspace.Paths,link,source,standard);
                    return new ReferenceDisplay(link.IncidentId,link.ControlId,link.StandardRelease,link.StandardSha256,(resolved.Resolved ? "Resolved: " : "Unresolved: ") + resolved.Reason);
                }).ToArray(); return (links,rows);
            });
            _controller.RequireContext(context); _links = result.links; Rows = result.rows; _referenceSource = source;
        }
        finally { _opening = false; Notify(); }
    }
    public override void Refresh()
    { if (_controller is not null && _context != _controller.ContextKey) ClearCurrent(); else Notify(); }
    private void Notify() { RaiseAll(); CommandManager.InvalidateRequerySuggested(); }
}
