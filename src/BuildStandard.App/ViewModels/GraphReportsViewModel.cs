using System.Collections.ObjectModel;
using System.Data;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
using BuildStandard.Engine.Reports;
using BuildStandard.Graph;
using Envelope = BuildStandard.Engine.Reports.ReportEnvelopeDispatcher.Envelope;

namespace BuildStandard.App.ViewModels;

public sealed class GraphReportsViewModel : PageViewModel
{
    private readonly GraphReportsController _controller;
    private readonly GraphReportEvidenceBrowser _browser;
    private string _context = "";
    private ConnectedTenant? _connection;
    private TenantProfile? _profile;
    private string _resultContext = "";
    private GraphReportPresentation _selectedReport = GraphReportPresentation.Choices[0];
    private string _horizonDays = "30";
    private string _start = "", _end = "", _search = "", _section = "", _rowState = "All";
    private bool _acknowledged;
    private int _selectionVersion;
    private Envelope? _report;
    private IReadOnlyList<Sheet> _sheets = Array.Empty<Sheet>();
    private bool _supplied;
    private DataRowView? _selectedRow;
    private string _exportFile = "";
    private string _suppliedFile = "";
    private int _selectedTab;
    private bool _reading;
    private GraphReportEvidenceBrowser.Entry? _selectedStored;

    public GraphReportsViewModel(ShellViewModel shell) : base(shell, "Graph reports")
    {
        _controller = new(Workspace); _browser = new(Workspace.Evidence);
        Usage = new(shell, _controller);
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
        CopyDetailsCommand = Sync(() => { RequireResultContext(); Shell.CopyToClipboard(Details); }, () => _report is not null && CanExport());
        CopyRowCommand = Sync(() =>
        {
            RequireResultContext();
            if (SelectedRow is null) throw new ToolkitException("Select a report row before copying its details.");
            Shell.CopyToClipboard(SelectedRowDetails);
        }, () => SelectedRow is not null && CanExport());
        Refresh();
    }

    public UsageReportsViewModel Usage { get; }
    public IReadOnlyList<GraphReportPresentation> Reports => GraphReportPresentation.Choices;
    public int SelectedTab { get => _selectedTab; set => SetProperty(ref _selectedTab, value); }
    public GraphReportPresentation SelectedReport
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
    public bool RequiresHorizon => SelectedReport.Expiry;
    public string HorizonDays { get => _horizonDays; set { if (SetProperty(ref _horizonDays, value)) { SelectionChanged(); NotifyPage(); } } }
    private ApplicationExpiryParameters ExpiryParameters()
    {
        if (!int.TryParse(HorizonDays, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var days)
            || days is < 1 or > 365)
            throw new ConfigurationException("Enter an expiry horizon as a whole number from 1 to 365 days.");
        return new() { HorizonDays = days };
    }
    private void ValidateParameters()
    {
        if (SelectedReport.SchemaVersion == 1)
        {
            var p = Parameters(); ReportEvidenceSchema.ValidateParameters(GraphReportRegistry.Find(SelectedReport.Id), p);
            if (p.End is not null && Timestamps.TryParse(p.End, out var end) && end > DateTimeOffset.UtcNow)
                throw new ConfigurationException("The range cannot end in the future.");
        }
        else
        {
            if (StartUtc.Length != 0 || EndUtc.Length != 0) throw new ConfigurationException("Application reports have no date-range parameters. Clear the log dates.");
            if (RequiresHorizon) ApplicationReportEvidenceSchema.ValidateParameters(ExpiryParameters());
        }
    }
    public string ScopeExplanation => SelectedReport.Scopes(Workspace.Session)
        + "\nMissing permissions produce NotAttempted evidence for the affected sources with zero requests to those sources. Consent metadata can still be read without Directory.Read.All; delegated grants then remain NotAttempted. Use Connect/Application setup deliberately to review access and consent; this page grants neither.";
    public string Limitations => SelectedReport.Limitations + "\nExperimental, live tenant behaviour is not accepted by source tests. Read-only routes reuse the current session; at most five minutes and 5,000 rows per section. Report evidence cannot authorise deployment.";
    public string ContextSummary => Workspace.Profile is null ? "Select a client in Connect to read or open its report evidence."
        : Workspace.Session is not { } s ? Workspace.Profile.Company + " · offline review · no authenticated account"
        : Workspace.Profile.Company + " · " + s.PrimaryDomain + " · " + s.Account + " · " + (s.TenantVerified ? "verified tenant" : "UNVERIFIED tenant")
            + " · " + s.TokenCapabilities.Description;
    private ReportParameters Parameters() => new() { Start = StartUtc.Length == 0 ? null : StartUtc, End = EndUtc.Length == 0 ? null : EndUtc };
    public string ReadGuidance
    {
        get
        {
            if (_controller.ContextKey != _context || !ReferenceEquals(_connection, Workspace.Connection) || !ReferenceEquals(_profile, Workspace.Profile))
                return "Client or account changed. Refresh and review the new context.";
            if (_controller.ReadBlocker is { Length: > 0 } blocker) return blocker;
            try { ValidateParameters(); }
            catch (ToolkitException ex) { return ex.Message; }
            return ReadAcknowledged ? "" : "Acknowledge the experimental read and the displayed limitations first. This is not consent or a tenant-change opt-in.";
        }
    }
    public ObservableCollection<GraphReportEvidenceBrowser.Entry> StoredReports { get; } = new();
    public GraphReportEvidenceBrowser.Entry? SelectedStored
    {
        get => _selectedStored;
        set { if (SetProperty(ref _selectedStored, value)) CommandManager.InvalidateRequerySuggested(); }
    }
    public string StoredStatus { get; private set; } = "Refresh the offline list to browse saved report evidence.";
    public string SuppliedFile { get => _suppliedFile; set => SetProperty(ref _suppliedFile, value); }
    public string Outcome { get; private set; } = "No report opened or read. Navigation performs no tenant reads.";
    public string Details => _report is null ? "No accepted report evidence." : string.Join("\n", _sheets[0].Rows.Skip(1).Select(r => r[0] + ": " + r[1]));
    public string ProvenanceNotice => _supplied ? RegisteredReportDocuments.SuppliedFileNotice : "Stored and collected reports retain their original times and read states. Digests detect changes; unsigned source claims do not authenticate the source.";
    public string SourceSummary => _report is null ? "No accepted report. Read a report or open saved/supplied evidence."
        : (_supplied ? "Supplied file — source and live-mode claims are unverified." : "Original report evidence — unsigned source claims.")
            + " " + GraphReportPresentation.StartedAt(_report) + " to " + GraphReportPresentation.EndedAt(_report) + ".";
    public string SectionSummary
    {
        get
        {
            if (_report is null) return "Select a report section.";
            var section = GraphReportPresentation.Sections(_report).SingleOrDefault(s => s.Id == SelectedSection);
            if (section is null)
            {
                var sheet = _sheets.SingleOrDefault(s => s.Name == SelectedSection);
                if (sheet is null) return "Select a report table.";
                return $"{SelectedSection} · {sheet.Rows.Count - 1} recorded child rows · Report {GraphReportPresentation.Status(_report)}. "
                    + "Child rows retain parent/source limitations. An empty child table does not prove a successful check; inspect parent rows and Provenance and limits.";
            }
            return section.Status + " · " + section.Rows.Count + " recorded rows · "
                + (section.Rows.Count == 0 ? section.Status == ReportReadState.Collected ? "Checked successfully; no objects returned." : "No rows available; this is not an empty successful check." : "Returned rows; unknown values stay unknown.")
                + (section.Error is null ? "" : "\n" + ShortProblem(section.Error));
        }
    }
    public bool CanFilterReadState => _sheets.SingleOrDefault(s => s.Name == SelectedSection)?.Rows[0].Contains("Read status") == true;
    public string ReadStateFilterGuidance => CanFilterReadState ? "Filter the recorded row read status; exports retain all rows."
        : "This table has no row read-status column. All read states are shown; inspect parent/source states in Provenance and limits.";
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
    public DataRowView? SelectedRow { get => _selectedRow; set { if (SetProperty(ref _selectedRow, value)) { OnPropertyChanged(nameof(SelectedRowDetails)); CommandManager.InvalidateRequerySuggested(); } } }
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
        Sections.Clear(); _section = ""; _rowState = "All"; Rows = null; SelectedRow = null;
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
            bool StillCurrent() => version == _selectionVersion && ReadAcknowledged;
            var result = SelectedReport.SchemaVersion == 1
                ? await _controller.ReadAsync(SelectedReport.Id, Parameters(), ack, context, StillCurrent)
                : RequiresHorizon ? await _controller.ReadExpiryAsync(ExpiryParameters(), ack, context, StillCurrent)
                : await _controller.ReadConsentAsync(ack, context, StillCurrent);
            Accept(result.Evidence, false, context);
            Outcome = GraphReportPresentation.Status(result.Evidence) + " · " + GraphReportPresentation.For(result.Evidence).Name
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
    private void Accept(Envelope report, bool supplied, string context)
    {
        _controller.RequireContext(context); GraphReportPresentation.Validate(report, Workspace.Profile!.TenantId);
        _report = report; _supplied = supplied; _resultContext = context;
        _sheets = GraphReportPresentation.Sheets(report, supplied);
        SelectedTab = 1;
        Sections.Clear(); foreach (var sheet in _sheets.Skip(1)) Sections.Add(sheet.Name);
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
        if (statusColumn < 0) _rowState = "All";
        foreach (var row in sheet.Rows.Skip(1))
        {
            if (Search.Length > 0 && !row.Any(c => c.Contains(Search, StringComparison.OrdinalIgnoreCase))) continue;
            if (RowState != "All" && statusColumn >= 0 && row[statusColumn] != RowState) continue;
            table.Rows.Add(row.Cast<object>().ToArray());
        }
        Rows = table.DefaultView; NotifyPage();
    }
    public override void Refresh() { if (_controller is null) return; EnsureCurrent(); NotifyPage(); }
    private void NotifyPage()
    {
        foreach (var name in new[] { nameof(RequiresDates), nameof(RequiresHorizon), nameof(HorizonDays), nameof(RowState), nameof(CanFilterReadState), nameof(ReadStateFilterGuidance), nameof(ReadAcknowledged), nameof(ReadGuidance), nameof(ScopeExplanation), nameof(Limitations), nameof(ContextSummary), nameof(StoredStatus), nameof(Outcome), nameof(Details), nameof(ProvenanceNotice), nameof(SourceSummary), nameof(Sections), nameof(SelectedSection), nameof(Rows), nameof(SectionSummary), nameof(FilterSummary), nameof(SelectedRowDetails), nameof(LastExport), nameof(ExportGuidance), nameof(SuppliedFile), nameof(SelectedStored) }) OnPropertyChanged(name);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}
