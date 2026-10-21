using System.IO;
using System.Windows;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
namespace BuildStandard.App.ViewModels;

/// <summary>Local, untrusted claims and explicit references. Never assessment, jobs or execution authority.</summary>
public sealed class ExternalCheckResultsViewModel : PageViewModel
{
    private readonly GraphReportsController _controller;
    private string _context = "", _resultContext = "", _search = "";
    private ExternalCheckImport? _source, _pending, _referenceSource;
    private IReadOnlyList<ExternalControlReference> _links = [];
    private bool _opening, _referencesDirty;
    public ExternalCheckResultsViewModel(ShellViewModel shell, GraphReportsController controller) : base(shell, "External results")
    {
        _controller = controller;
        OpenCommand = Command(async () =>
        {
            RequireOpen(); var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Neutral external results (*.json)|*.json", Title = "Open external-check-results v1 (8 MiB maximum)" };
            if (dialog.ShowDialog() == true) await OpenSuppliedAsync(dialog.FileName);
        }, CanOpen);
        HtmlCommand = Command(() => ExportAsync(ExportFormat.Html), CanExport);
        JsonCommand = Command(() => ExportAsync(ExportFormat.Json), CanExport);
        CsvCommand = Command(() => ExportAsync(ExportFormat.Csv), CanExport);
        ExcelCommand = Command(() => ExportAsync(ExportFormat.Xlsx), CanExport);
        ExportOriginalCommand = Command(ExportPendingOriginalAsync, () => Workspace.Idle && !_opening && HasPendingOriginal);
        DiscardCommand = Sync(() =>
        {
            if (!Workspace.Idle || _opening) throw new ToolkitException("Wait for the local file operation.");
            if (MessageBox.Show("Discard pending external original and unsaved references? Saved files are retained.", "Discard local pending claims", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _pending = null; _links = []; _referenceSource = null; _referencesDirty = false; ClearCurrent(); Notify();
        }, () => Workspace.Idle && !_opening && (HasPendingOriginal || _referencesDirty));
        AddReferenceCommand = Sync(AddReference, () => CanExport() && SelectedTestId.Length > 0 && SelectedControlId.Length > 0);
        SaveReferencesCommand = Command(ExportReferencesAsync, () => Workspace.Idle && !_opening && _referenceSource is not null && _links.Count > 0);
        OpenReferencesCommand = Command(async () =>
        {
            RequireCurrent(); if (_referencesDirty) throw new ToolkitException("Save or explicitly discard unsaved references before replacing them.");
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Exact external references (*.json)|*.json", Title = "Open source-bound control references" };
            if (dialog.ShowDialog() == true) await OpenReferencesAsync(dialog.FileName);
        }, CanExport);
        Refresh();
    }
    internal Func<string, string, Task<ExternalCheckImport>> ReadOriginal { get; set; } = (file, tenant) => Task.Run(() => ExternalCheckResultsSchema.ReadFile(file, tenant));
    public string Outcome { get; private set; } = "No current external results. Choose a client, then deliberately open a neutral results file. Navigation performs no reads.";
    public string LastExport { get; private set; } = "";
    public bool HasPendingOriginal => _pending is not null;
    public string PendingSummary => _pending is null ? "No original awaiting export." : $"Pending untrusted original · claimed tenant {_pending.Evidence.TenantId} · SHA-256 {_pending.OriginalSha256}. Export original JSON or explicitly discard before opening another file. Forced termination can lose pending data.";
    public string Guidance => Workspace.Profile is null ? "Select the claimed client in Connect first. No sign-in is required for a local file."
        : _opening || !Workspace.Idle ? "Wait for the current operation." : HasPendingOriginal || _referencesDirty ? "Export pending original JSON and save references, or explicitly discard them before opening another file." : "Open only external-check-results version 1. Arbitrary vendor/Pester JSON requires deliberate conversion; it is never guessed.";
    public string Provenance => _source is null ? "No current source claims." : string.Join("\n", ExternalCheckDocuments.Sheets(_source)[0].Rows.Skip(1).Select(r => r[0] + ": " + r[1]));
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) OnPropertyChanged(nameof(Rows)); } }
    public IReadOnlyList<ExternalCheckResultRow> Rows => _source?.Evidence.Rows.Where(r => Search.Length == 0 || new[] { r.TestId, r.Title, r.Outcome, r.Reason ?? "" }.Any(v => v.Contains(Search, StringComparison.OrdinalIgnoreCase))).ToArray() ?? [];
    public IReadOnlyList<string> SourceTests => _source?.Evidence.Rows.Select(r => r.TestId).ToArray() ?? [];
    public sealed record ControlChoice(string Id, string Label);
    public IReadOnlyList<ControlChoice> Controls => Workspace.Standard?.Controls.Select(c => new ControlChoice(c.Id, c.Name + " · " + c.Id)).ToArray() ?? [];
    public string SelectedTestId { get; set; } = "";
    public string SelectedControlId { get; set; } = "";
    public int ReferenceCount => _links.Count;
    public string ReferenceSummary { get; private set; } = "References are engineer-entered context, never compliance, ownership or deployment evidence.";
    public ICommand OpenCommand { get; }
    public ICommand HtmlCommand { get; }
    public ICommand JsonCommand { get; }
    public ICommand CsvCommand { get; }
    public ICommand ExcelCommand { get; }
    public ICommand ExportOriginalCommand { get; }
    public ICommand DiscardCommand { get; }
    public ICommand AddReferenceCommand { get; }
    public ICommand SaveReferencesCommand { get; }
    public ICommand OpenReferencesCommand { get; }
    private bool CanOpen() => Workspace.Idle && !_opening && Workspace.Profile is not null && !HasPendingOriginal && !_referencesDirty;
    private bool CanExport() => Workspace.Idle && !_opening && _source is not null && _resultContext == _controller.ContextKey;
    private void RequireOpen() { Refresh(); if (!CanOpen()) throw new ToolkitException(Guidance); _controller.RequireContext(_context); }
    private ExternalCheckImport RequireCurrent()
    { _controller.RequireContext(_resultContext); if (!CanExport()) throw new ToolkitException("Open results for the current client and wait for the local operation."); return _source!; }
    private void ClearCurrent()
    { _source = null; _resultContext = ""; _search = ""; SelectedTestId = ""; SelectedControlId = ""; LastExport = ""; Outcome = "No current external results. Older source-bound originals and references are retained separately."; }
    public async Task OpenSuppliedAsync(string file)
    {
        RequireOpen(); var context = _context; var tenant = Workspace.Profile!.TenantId; _opening = true; ClearCurrent(); Notify();
        try
        {
            var admitted = await ReadOriginal(file, tenant);
            // Keep a validated original even when a late context change prevents presentation admission.
            _pending = admitted;
            _controller.RequireContext(context);
            _source = admitted; _referenceSource = admitted; _links = []; _referencesDirty = false; _resultContext = context;
            Outcome = "Untrusted external claims · " + admitted.Evidence.SourceState + " · " + admitted.Evidence.Rows.Count + " source rows. No native assessment or managed evidence was changed.";
        }
        finally { _opening = false; Notify(); }
    }
    public async Task ExportAsync(ExportFormat format)
    {
        var source = RequireCurrent(); var context = _resultContext;
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportExternalCheckResults(source, source.Evidence.TenantId, format));
        if (format == ExportFormat.Json && ReferenceEquals(_pending, source)) _pending = null;
        _controller.RequireContext(context); LastExport = file; Notify();
    }
    public async Task ExportPendingOriginalAsync()
    {
        if (!Workspace.Idle || _opening || _pending is null) throw new ToolkitException("No original is available, or a local operation is active.");
        var source = _pending;
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportExternalCheckResults(source, source.Evidence.TenantId, ExportFormat.Json), "Retaining untrusted external original", "Writing exact original source-client bytes without network access or managed evidence insertion.");
        if (ReferenceEquals(_pending, source)) _pending = null; LastExport = file; Notify();
    }
    public void AddReference()
    {
        var source = RequireCurrent(); var standard = Workspace.Standard ?? throw new ToolkitException("Load a verified standard first.");
        var link = ExternalControlReferences.Create(Workspace.Paths, source, SelectedTestId, standard, SelectedControlId);
        var proposed = _links.Append(link).ToArray(); _ = ExternalControlReferenceDocument.Write(source, proposed);
        _links = proposed; _referenceSource = source; _referencesDirty = true;
        ReferenceSummary = $"{_links.Count} explicit reference(s); unsaved. Source {source.OriginalSha256}. No assessment verdict was changed."; Notify();
    }
    public async Task ExportReferencesAsync()
    {
        if (!Workspace.Idle || _opening || _referenceSource is null || _links.Count == 0) throw new ToolkitException("No source-bound references are available to save.");
        var source = _referenceSource; var links = _links.ToArray();
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportExternalControlReferences(source, links, source.Evidence.TenantId), "Saving explicit control references", "Keeping original source and exact catalogue identities; this grants no execution authority.");
        _referencesDirty = false; LastExport = file; ReferenceSummary = $"{links.Length} references saved for source {source.OriginalSha256}. Historical references are not rebound by name."; Notify();
    }
    public async Task OpenReferencesAsync(string file)
    {
        var source = RequireCurrent(); if (_referencesDirty) throw new ToolkitException("Save or explicitly discard unsaved references first.");
        var context = _resultContext; var standard = Workspace.Standard; _opening = true; Notify();
        try
        {
            var result = await Task.Run(() =>
            {
                var links = ExternalControlReferenceDocument.ReadFile(file, source);
                var resolved = links.Count(link => ExternalControlReferences.Resolve(Workspace.Paths, link, source, standard).Resolved);
                return (links, resolved);
            });
            _controller.RequireContext(context); _links = result.links; _referenceSource = source;
            ReferenceSummary = $"{_links.Count} exact references · {result.resolved} resolved · {_links.Count - result.resolved} unresolved. Unavailable historical catalogues remain unresolved; no compliance or ownership is inferred.";
        }
        finally { _opening = false; Notify(); }
    }
    public override void Refresh()
    {
        if (_controller is null) return;
        var context = _controller.ContextKey;
        if (_context != context) { _context = context; ClearCurrent(); }
        Notify();
    }
    private void Notify() { RaiseAll(); CommandManager.InvalidateRequerySuggested(); }
}
