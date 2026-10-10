using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Reporting;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Graph;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class GraphReportsViewModel : PageViewModel
{
    private readonly GraphReportsController _controller;
    private readonly GraphReportEvidenceBrowser _browser;
    private string _context = "";
    private ConnectedTenant? _connection;
    private TenantProfile? _profile;
    private string _resultContext = "";
    private GraphReportRegistry.Definition _selectedReport = GraphReportRegistry.Definitions[0];
    private string _start = "", _end = "", _search = "", _section = "", _rowState = "All";
    private bool _acknowledged;
    private int _selectionVersion;
    private ReportEvidence? _report;
    private IReadOnlyList<Sheet> _sheets = Array.Empty<Sheet>();
    private bool _supplied;
    private DataRowView? _selectedRow;
    private string _exportFile = "";
    private string _suppliedFile = "";
    private int _selectedTab;
    private bool _reading;

    public GraphReportsViewModel(ShellViewModel shell) : base(shell, "Graph reports")
    {
        _controller = new(Workspace); _browser = new(Workspace.Evidence);
        ReadCommand = Command(ReadAsync, () => ReadGuidance.Length == 0);
        StopReadCommand = Sync(() =>
        {
            if (!_reading || Workspace.Idle) throw new ToolkitException("There is no active report read to stop.");
            Workspace.CancelOperation();
        }, () => _reading && Workspace.Busy);
        RefreshStoredCommand = Sync(RefreshStored, () => Workspace.Profile is not null && Workspace.Idle);
        OpenStoredCommand = Sync(OpenStored, () => SelectedStored?.CanOpen == true && Workspace.Idle);
        ChooseSuppliedCommand = Sync(() =>
        {
            RequireIdleContext();
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Graph report evidence (*.json)|*.json", Title = "Open supplied Graph report evidence (32 MiB maximum)" };
            if (dialog.ShowDialog() == true) SuppliedFile = dialog.FileName;
        }, () => Workspace.Profile is not null && Workspace.Idle);
        OpenSuppliedCommand = Sync(() => OpenSupplied(SuppliedFile), () => Workspace.Profile is not null && Workspace.Idle && SuppliedFile.Length > 0);
        ExportHtmlCommand = Command(() => ExportAsync(ExportFormat.Html), CanExport);
        ExportJsonCommand = Command(() => ExportAsync(ExportFormat.Json), CanExport);
        ExportCsvCommand = Command(() => ExportAsync(ExportFormat.Csv), CanExport);
        ExportXlsxCommand = Command(() => ExportAsync(ExportFormat.Xlsx), CanExport);
        OpenExportCommand = Sync(() => { RequireResultContext(); Infrastructure.ShellFolders.RevealFile(_exportFile); }, () => _exportFile.Length > 0 && CanExport());
        CopyDetailsCommand = CopyText(() => Details);
        CopyRowCommand = CopyText(() => SelectedRowDetails);
        Refresh();
    }

    public IReadOnlyList<GraphReportRegistry.Definition> Reports => GraphReportRegistry.Definitions;
    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }
    public GraphReportRegistry.Definition SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (value is null || !Reports.Contains(value)) throw new ConfigurationException("Choose a registered report.");
            if (SetProperty(ref _selectedReport, value))
            {
                if (!value.DateRange) { _start = ""; _end = ""; OnPropertyChanged(nameof(StartUtc)); OnPropertyChanged(nameof(EndUtc)); }
                SelectionChanged(); NotifyPage();
            }
        }
    }
    public string StartUtc { get => _start; set { if (SetProperty(ref _start, value)) { SelectionChanged(); NotifyPage(); } } }
    public string EndUtc { get => _end; set { if (SetProperty(ref _end, value)) { SelectionChanged(); NotifyPage(); } } }
    public bool RequiresDates => SelectedReport.DateRange;
    public bool ReadAcknowledged
    {
        get => _acknowledged;
        set { EnsureCurrent(); if (SetProperty(ref _acknowledged, value)) NotifyPage(); }
    }
    public string ScopeExplanation => string.Join("\n", SelectedReport.Routes.Select(r => r.Path + " requires " + r.Scope
        + (Workspace.Session is { } s && GraphReportRegistry.HasAccess(s, r) ? " — available in this session." : " — unavailable in this session.")))
        + "\nMissing permissions produce NotAttempted evidence with zero report requests. Use Connect/Application setup deliberately to review access and consent; this page grants neither.";
    public string Limitations => SelectedReport.Limitations + "\nExperimental, live tenant behaviour is not accepted by source tests. Read-only routes reuse the current session; at most five minutes and 5,000 rows per section. Report evidence cannot authorise deployment.";
    public string ContextSummary => Workspace.Profile is null ? "Select a client in Connect to read or open its report evidence."
        : Workspace.Session is not { } s ? Workspace.Profile.Company + " · offline review · no authenticated account"
        : Workspace.Profile.Company + " · " + s.PrimaryDomain + " · " + s.Account + " · " + (s.TenantVerified ? "verified tenant" : "UNVERIFIED tenant")
            + " · " + (s.Mode == SessionMode.Deployment || s.HasWriteScopes ? "WRITE-CAPABLE TOKEN — report routes remain read-only" : "read-only assessment access");
    private ReportParameters Parameters() => new() { Start = StartUtc.Length == 0 ? null : StartUtc, End = EndUtc.Length == 0 ? null : EndUtc };
    public string ReadGuidance
    {
        get
        {
            if (_controller.ContextKey != _context || !ReferenceEquals(_connection, Workspace.Connection) || !ReferenceEquals(_profile, Workspace.Profile))
                return "Client or account changed. Refresh and review the new context.";
            if (_controller.ReadBlocker is { Length: > 0 } blocker) return blocker;
            try
            {
                var p = Parameters(); ReportEvidenceSchema.ValidateParameters(SelectedReport, p);
                if (p.End is not null && Timestamps.TryParse(p.End, out var end) && end > DateTimeOffset.UtcNow) return "The range cannot end in the future.";
            }
            catch (ToolkitException ex) { return ex.Message; }
            return ReadAcknowledged ? "" : "Acknowledge the experimental read and the displayed limitations first. This is not consent or a tenant-change opt-in.";
        }
    }
    public ObservableCollection<GraphReportEvidenceBrowser.Entry> StoredReports { get; } = new();
    public GraphReportEvidenceBrowser.Entry? SelectedStored { get; set; }
    public string StoredStatus { get; private set; } = "Refresh the offline list to browse saved report evidence.";
    public string SuppliedFile { get => _suppliedFile; set => SetProperty(ref _suppliedFile, value); }
    public string Outcome { get; private set; } = "No report opened or read. Navigation performs no tenant reads.";
    public string Details => _report is null ? "No accepted report evidence." : string.Join("\n", _sheets[0].Rows.Skip(1).Select(r => r[0] + ": " + r[1]));
    public string ProvenanceNotice => _supplied ? RegisteredReportDocuments.SuppliedFileNotice : "Stored and collected reports retain their original times and read states. Digests detect changes; unsigned source claims do not authenticate the source.";
    public string SourceSummary => _report is null ? "No accepted report. Read a report or open saved/supplied evidence."
        : (_supplied ? "Supplied file — source and live-mode claims are unverified." : "Original report evidence — unsigned source claims.")
            + " " + _report.StartedAt + " to " + _report.EndedAt + ".";
    public string SectionSummary => _report?.Sections.SingleOrDefault(s => s.Id == SelectedSection) is not { } section ? "Select a report section."
        : section.Status + " · " + section.Rows.Count + " recorded rows · "
            + (section.Rows.Count == 0 ? section.Status == ReportReadState.Collected ? "Checked successfully; no objects returned." : "No rows available; this is not an empty successful check." : "Returned rows; unknown values stay unknown.")
            + (section.Error is null ? "" : "\n" + ShortProblem(section.Error));
    private static string ShortProblem(string error)
    {
        var singleLine = string.Join(" ", error.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return singleLine.Length <= 180 ? singleLine : singleLine[..180] + "… Review the full read detail in Provenance and limits.";
    }
    public ObservableCollection<string> Sections { get; } = new();
    public string SelectedSection { get => _section; set { if (SetProperty(ref _section, value)) ApplyFilter(); } }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }
    public IReadOnlyList<string> RowStates { get; } = new[] { "All" }.Concat(ReportReadState.All).ToArray();
    public string RowState { get => _rowState; set { if (SetProperty(ref _rowState, value)) ApplyFilter(); } }
    public DataView? Rows { get; private set; }
    public string FilterSummary => $"{Rows?.Count ?? 0} rows visible · display filters only. All exports include the complete original report, provenance and every read state.";
    public DataRowView? SelectedRow { get => _selectedRow; set { if (SetProperty(ref _selectedRow, value)) OnPropertyChanged(nameof(SelectedRowDetails)); } }
    public string SelectedRowDetails => SelectedRow is null ? "Select a row to inspect and copy its exact IDs, unknown fields and read detail."
        : string.Join("\n", SelectedRow.DataView.Table!.Columns.Cast<DataColumn>().Select(c => c.ColumnName + ": " + SelectedRow[c.ColumnName]));
    public string LastExport { get; private set; } = "";
    public string ExportGuidance => _report is null ? "Open or read evidence before exporting." : Workspace.Idle ? "Exports include the complete report; JSON preserves the original record without a new authenticity claim." : "Wait for the current operation before exporting.";
    public ICommand ReadCommand { get; }
    public ICommand StopReadCommand { get; }
    public ICommand RefreshStoredCommand { get; }
    public ICommand OpenStoredCommand { get; }
    public ICommand ChooseSuppliedCommand { get; }
    public ICommand OpenSuppliedCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportXlsxCommand { get; }
    public ICommand OpenExportCommand { get; }
    public ICommand CopyDetailsCommand { get; }
    public ICommand CopyRowCommand { get; }

    private void SelectionChanged() { _selectionVersion++; _acknowledged = false; ClearResult(); }
    private void ClearResult()
    {
        _report = null; _sheets = Array.Empty<Sheet>(); _resultContext = ""; _supplied = false; _exportFile = ""; LastExport = "";
        Sections.Clear(); _section = ""; Rows = null; SelectedRow = null;
        Outcome = "No report accepted for this selection. Older evidence remains stored separately.";
    }
    private void EnsureCurrent()
    {
        if (_context == _controller.ContextKey && ReferenceEquals(_connection, Workspace.Connection) && ReferenceEquals(_profile, Workspace.Profile)) return;
        _context = _controller.ContextKey; _connection = Workspace.Connection; _profile = Workspace.Profile;
        SelectionChanged(); StoredReports.Clear(); SelectedStored = null; SuppliedFile = "";
        StoredStatus = "Client or account changed; refresh the offline list for the selected tenant.";
    }
    private void RequireIdleContext()
    {
        _controller.RequireContext(_context);
        if (!ReferenceEquals(_connection, Workspace.Connection) || !ReferenceEquals(_profile, Workspace.Profile)) throw new TenantMismatchException("Client or connection changed. Refresh first.");
        if (!Workspace.Idle) throw new ToolkitException("Wait for the current operation to finish.");
    }
    private void RequireResultContext()
    {
        RequireIdleContext();
        if (_report is null || _resultContext != _controller.ContextKey) throw new ToolkitException("Open or read report evidence for the current context first.");
    }
    private bool CanExport() => _report is not null && _resultContext == _controller.ContextKey && Workspace.Idle
        && ReferenceEquals(_connection, Workspace.Connection) && ReferenceEquals(_profile, Workspace.Profile);

    public async Task ReadAsync()
    {
        RequireIdleContext();
        if (ReadGuidance.Length > 0) throw new ToolkitException(ReadGuidance);
        var context = _context; var version = _selectionVersion; var ack = ReadAcknowledged;
        ClearResult(); _reading = true; Outcome = "Reading the selected report; no result accepted yet."; NotifyPage();
        try
        {
            var result = await _controller.ReadAsync(SelectedReport.Id, Parameters(), ack, context,
                () => version == _selectionVersion && ReadAcknowledged);
            Accept(result.Evidence, false, context);
            Outcome = result.Evidence.Status + " · " + GraphReportRegistry.Find(result.Evidence.ReportId).Name
                + (result.NotSavedReason is null ? " · saved locally." : " · NOT SAVED: " + result.NotSavedReason);
        }
        catch { ClearResult(); Outcome = "Read refused or failed; no new report accepted or relabelled. Review the error and try again deliberately."; throw; }
        finally { _acknowledged = false; _reading = false; NotifyPage(); }
    }
    public void RefreshStored()
    {
        RequireIdleContext(); var listing = _browser.List(Workspace.Profile!.TenantId);
        StoredReports.Clear(); SelectedStored = null;
        foreach (var entry in listing.Entries) StoredReports.Add(entry);
        StoredStatus = (listing.Complete ? "" : "INCOMPLETE — ") + listing.Detail; NotifyPage();
    }
    public void OpenStored()
    {
        RequireIdleContext();
        var entry = SelectedStored ?? throw new ToolkitException("Select a saved report first.");
        if (!entry.CanOpen || !StoredReports.Contains(entry)) throw new ToolkitException("This record could not be read. Review the listing finding and refresh.");
        ClearResult();
        try { Accept(_browser.OpenStored(Workspace.Profile!.TenantId, entry.Id), false, _context); Outcome = "Opened saved evidence offline; original read state and time retained."; }
        finally { _acknowledged = false; NotifyPage(); }
    }
    public void OpenSupplied(string file)
    {
        RequireIdleContext(); ClearResult();
        try { Accept(_browser.OpenSupplied(file, Workspace.Profile!.TenantId), true, _context); Outcome = "Opened supplied evidence offline; file not persisted or altered."; }
        finally { _acknowledged = false; NotifyPage(); }
    }
    private void Accept(ReportEvidence report, bool supplied, string context)
    {
        _controller.RequireContext(context); ReportEvidenceSchema.Validate(report, Workspace.Profile!.TenantId);
        _report = report; _supplied = supplied; _resultContext = context;
        _sheets = RegisteredReportDocuments.Sheets(report, supplied);
        SelectedTab = 1;
        Sections.Clear(); foreach (var section in report.Sections) Sections.Add(section.Id);
        _section = Sections[0]; ApplyFilter(); NotifyPage();
    }
    public async Task ExportAsync(ExportFormat format)
    {
        RequireResultContext(); var context = _resultContext; var version = _selectionVersion; var report = _report!;
        var file = await _controller.ExportAsync(report, format, _supplied, context);
        _controller.RequireContext(context);
        if (_selectionVersion != version || !ReferenceEquals(_report, report))
            throw new ToolkitException("The report selection changed during export. An output may exist for the original report; no export was attributed to the current selection.");
        _exportFile = file;
        LastExport = "Complete original report exported: " + _exportFile; NotifyPage();
    }
    private void ApplyFilter()
    {
        SelectedRow = null;
        if (_report is null || !Sections.Contains(SelectedSection)) { Rows = null; NotifyPage(); return; }
        var sheet = _sheets.Single(s => s.Name == SelectedSection);
        var table = new DataTable();
        foreach (var header in sheet.Rows[0]) table.Columns.Add(header, typeof(string));
        var statusColumn = table.Columns.IndexOf("Read status");
        foreach (var row in sheet.Rows.Skip(1))
        {
            if (Search.Length > 0 && !row.Any(c => c.Contains(Search, StringComparison.OrdinalIgnoreCase))) continue;
            if (RowState != "All" && (statusColumn < 0 || row[statusColumn] != RowState)) continue;
            table.Rows.Add(row.Cast<object>().ToArray());
        }
        Rows = table.DefaultView; NotifyPage();
    }
    public override void Refresh() { if (_controller is null) return; EnsureCurrent(); NotifyPage(); }
    private void NotifyPage()
    {
        foreach (var name in new[] { nameof(RequiresDates), nameof(ReadAcknowledged), nameof(ReadGuidance), nameof(ScopeExplanation), nameof(Limitations), nameof(ContextSummary), nameof(StoredStatus), nameof(Outcome), nameof(Details), nameof(ProvenanceNotice), nameof(SourceSummary), nameof(Sections), nameof(SelectedSection), nameof(Rows), nameof(SectionSummary), nameof(FilterSummary), nameof(SelectedRowDetails), nameof(LastExport), nameof(ExportGuidance), nameof(SuppliedFile), nameof(SelectedStored) }) OnPropertyChanged(name);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
