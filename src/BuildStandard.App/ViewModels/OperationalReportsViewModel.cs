using System.Data;
using System.IO;
using System.Text;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Reports;
namespace BuildStandard.App.ViewModels;

/// <summary>Native visibility and original recovery over the shared bounded engine; no sign-in or tenant writes.</summary>
public sealed class OperationalReportsViewModel : PageViewModel
{
    private readonly GraphReportsController _controller;
    private string _context = "", _section = "", _search = "", _message = "No operational report is open.", _export = "";
    private bool _acknowledged, _reading, _opening, _supplied, _partnerDeclared;
    private string _selectedReportId="service-health", _customerTenantId="";
    private int _generation;
    private OperationalContextReport? _report;
    private byte[] _original = [], _pending = [];
    private string _pendingTenant = "", _pendingId = "";
    private IReadOnlyList<Sheet> _sheets = [];
    private DataView? _rows;
    public OperationalReportsViewModel(ShellViewModel shell, GraphReportsController controller) : base(shell, "Operational context")
    {
        _controller = controller;
        ReadCommand = Command(ReadAsync, () => ReadGuidance.Length == 0);
        StopCommand = Sync(() => { if (!_reading || Workspace.Idle) throw new ToolkitException("No operational read is active."); Workspace.CancelOperation(); }, () => _reading && Workspace.Busy);
        OpenCommand = Command(ChooseAndOpenAsync, () => CanEdit && Workspace.Profile is not null);
        HtmlCommand = Command(() => ExportAsync(ExportFormat.Html), CanExport);
        JsonCommand = Command(() => ExportAsync(ExportFormat.Json), CanExport);
        CsvCommand = Command(() => ExportAsync(ExportFormat.Csv), CanExport);
        ExcelCommand = Command(() => ExportAsync(ExportFormat.Xlsx), CanExport);
        SaveOriginalCommand = Command(SavePendingOriginalAsync, () => Workspace.Idle && HasPendingOriginal);
        ExportOriginalCommand = Command(ExportPendingOriginalAsync, () => Workspace.Idle && HasPendingOriginal);
        DiscardOriginalCommand = Sync(DiscardPending, () => Workspace.Idle && HasPendingOriginal);
        OpenConnectionCommand = Sync(() => Shell.Navigate("connect"), () => Workspace.Idle);
        Refresh();
    }
    public string SelectedReportId { get=>_selectedReportId; set { if(SetProperty(ref _selectedReportId,value)) SelectionChanged(); } }
    public string CustomerTenantId { get=>_customerTenantId; set { if(SetProperty(ref _customerTenantId,value)) SelectionChanged(); } }
    public bool PartnerDeclared { get=>_partnerDeclared; set { if(SetProperty(ref _partnerDeclared,value)) SelectionChanged(); } }
    private void SelectionChanged() { _generation++;_acknowledged=false;ClearResult();Notify(); }
    public bool CanEdit => Workspace.Idle && !_reading && !_opening && !HasPendingOriginal;
    public bool Acknowledged { get => _acknowledged; set { EnsureContext(); if (SetProperty(ref _acknowledged, value)) Notify(); } }
    public string Notice => RegisteredReportDocuments.OperationalNotice;
    public string AccessPreview => "Experimental service-health context · fixed overview and issue GETs using actually returned ServiceHealth.Read.All. No sign-in, consent, customer switch or write occurs here. Five minutes total,100requests,5,000rows and16MiB. Original mode/read purpose are not proof that a token has no additional rights. Partner collection is not implemented in this slice.";
    public string ReadGuidance
    {
        get
        {
            EnsureContext();
            if (_reading || !Workspace.Idle) return _reading ? "Service-health capture/original retention is active. Stop keeps earlier admitted observations; closing waits for recovery bytes." : "Wait for the current operation to finish.";
            if (_opening) return "The original operational file is being checked. Wait before another open or read; no service request is made.";
            if (HasPendingOriginal) return "Save, export or explicitly discard the pending original before another read. It retains its original source tenant.";
            var blocker = _controller.OperationalReadBlocker;
            return blocker.Length != 0 ? blocker : Acknowledged ? "" : "Review the source, fixed reads, access and limitations, then tick acknowledgement before Read.";
        }
    }
    public string Outcome => _report is null ? "No current operational result." : $"Service-health context · {_report.Status} · as-of {_report.AsOfUtc}\n" + string.Join(" · ", _report.Sections.Select(section => section.Id + ": " + section.Status));
    public string Message => _message;
    public string LastExport => _export;
    public IReadOnlyList<string> Sections => _sheets.Select(sheet => sheet.Name).ToArray();
    public string SelectedSection { get => _section; set { if (SetProperty(ref _section, value)) BuildRows(); } }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) BuildRows(); } }
    public DataView? Rows => _rows;
    public bool HasPendingOriginal => _pending.Length > 0;
    public string PendingSummary => HasPendingOriginal ? $"Original operational record {_pendingId} · source tenant {_pendingTenant}. Exact bytes stay in memory until saved, exported or explicitly discarded. Changing client never rebinds them. A forced termination can lose this pending original." : "No pending original.";
    public ICommand ReadCommand { get; } public ICommand StopCommand { get; } public ICommand OpenCommand { get; }
    public ICommand HtmlCommand { get; } public ICommand JsonCommand { get; } public ICommand CsvCommand { get; } public ICommand ExcelCommand { get; }
    public ICommand SaveOriginalCommand { get; } public ICommand ExportOriginalCommand { get; } public ICommand DiscardOriginalCommand { get; } public ICommand OpenConnectionCommand { get; }
    internal Func<string, bool> ConfirmDiscard { get; set; } = summary => System.Windows.MessageBox.Show(summary + "\n\nDiscard this original without another read?", "Discard original operational report", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
    internal Func<OperationalContextReport, Task<byte[]>> SerialiseOriginal { get; set; } = report => Task.Run(() => new UTF8Encoding(false, true).GetBytes(OperationalContextSchema.Serialise(report)));
    private void ClearResult() { _report = null; _original = []; _sheets = []; _section = ""; _rows = null; _export = ""; }
    private void EnsureContext()
    {
        var context = _controller.ContextKey;
        if (_context == context) return;
        _context = context; _generation++; _acknowledged = false; ClearResult();
        _message = "Client, session, catalogue or evidence changed. Former results and acknowledgement were cleared; pending originals retain their source.";
    }
    private bool CanExport() => Workspace.Idle && _report is not null && _context == _controller.ContextKey;
    public async Task ReadAsync()
    {
        var reason = ReadGuidance; if (reason.Length != 0) throw new ToolkitException(reason);
        var context = _context; var generation = ++_generation;
        var reportId=_selectedReportId;var customer=_customerTenantId;var partnerDeclared=_partnerDeclared;
        _acknowledged = false; _reading = true; ClearResult(); Notify();
        try
        {
            await _controller.ReadOperationalAsync(true, context, () => generation == _generation, async report =>
            {
                var original = await SerialiseOriginal(report);
                _pending = original.ToArray(); _pendingTenant = report.TenantId; _pendingId = report.Id;
                await SetReportAsync(report, original, false, context, generation);
                _message = "Source reads finished with the states above. Save or export original JSON before closing or reading again. Other formats do not replace the original.";
            },reportId,customer.Length==0?null:customer,partnerDeclared);
        }
        finally { _reading = false; Notify(); }
    }
    private async Task SetReportAsync(OperationalContextReport report, byte[] original, bool supplied, string context, int generation)
    {
        var sheets = await Task.Run(() => RegisteredReportDocuments.OperationalSheets(report, supplied));
        _controller.RequireContext(context);
        if (generation != _generation) throw new TenantMismatchException("Operational context changed while projecting the result.");
        _report = report; _original = original.ToArray(); _supplied = supplied; _sheets = sheets; _section = sheets[0].Name; _search = ""; BuildRows(); Notify();
    }
    internal Func<string, string, Task<OperationalContextFile>> ReadOriginal { get; set; } = (file, tenant) => Task.Run(() => OperationalContextFile.ReadFile(file, tenant));
    public async Task OpenSuppliedAsync(string file)
    {
        EnsureContext(); if (!CanEdit || Workspace.Profile is null) throw new ToolkitException("Select the original source client, wait for the operation and resolve pending evidence before opening JSON.");
        var context = _context; var generation = ++_generation; var tenant = Workspace.Profile.TenantId;
        _acknowledged = false; _opening = true; ClearResult(); Notify();
        try
        {
            var admitted = await ReadOriginal(file, tenant);
            await SetReportAsync(admitted.Report, admitted.CopyOriginal(), true, context, generation);
            _message = "Supplied historical source claims; integrity is not source authentication. No service request or managed-store insertion occurred.";
        }
        finally { _opening = false; Notify(); }
    }
    private async Task ChooseAndOpenAsync()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Operational evidence (*.json)|*.json", Title = "Open saved or supplied operational JSON (16MiB maximum)" };
        if (picker.ShowDialog() == true) await OpenSuppliedAsync(picker.FileName);
    }
    public async Task ExportAsync(ExportFormat format)
    {
        if (!CanExport()) throw new ToolkitException("Open/read operational evidence for the current source client and wait for the current operation first.");
        var context = _context; var generation = _generation; var original = _original.ToArray();
        var file = await _controller.ExportOperationalAsync(_report!, format, _supplied, original, context);
        if (generation != _generation) throw new TenantMismatchException("Context changed during export. The file retains its original source; no new result was admitted.");
        if (format == ExportFormat.Json) ResolvePending(file, original);
        _export = file; _message = "Complete operational report exported; display filters did not limit it. Unknown/failed sources remain explicit."; Notify();
    }
    public async Task SavePendingOriginalAsync()
    {
        if (!Workspace.Idle || !HasPendingOriginal) throw new ToolkitException("Wait for the operation; no operational original is pending.");
        var original = _pending.ToArray(); var tenant = _pendingTenant; var id = _pendingId;
        var file = await Workspace.ExportAsync(() => Workspace.Evidence.SaveOperationalContext(original, tenant, id), "Saving original operational evidence", "Keeping exact source-client bytes without rerun; ordinary backup bounds remain unchanged.");
        ResolvePending(file, original); _export = file; _message = "Original evidence saved and readback verified under its source tenant; included in ordinary workspace backup."; Notify();
    }
    public async Task ExportPendingOriginalAsync()
    {
        if (!Workspace.Idle || !HasPendingOriginal) throw new ToolkitException("Wait for the operation; no original is pending.");
        var original = _pending.ToArray(); var report = OperationalContextFile.Read(original, _pendingTenant).Report;
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportOperationalContext(report, ExportFormat.Json, originalBytes: original), "Retaining original operational JSON", "Original source client retained; no service request or managed-store insertion.");
        ResolvePending(file, original); _export = file; _message = "Original JSON exported and verified, separately from managed evidence/backup."; Notify();
    }
    private void ResolvePending(string file, byte[] original)
    { OperationalContextFile.VerifyOriginalExport(file, original); if (_pending.AsSpan().SequenceEqual(original)) { _pending = []; _pendingTenant = ""; _pendingId = ""; } }
    private void DiscardPending()
    {
        if (!Workspace.Idle || !HasPendingOriginal || !ConfirmDiscard(PendingSummary)) return;
        if (_original.AsSpan().SequenceEqual(_pending)) ClearResult();
        _pending = []; _pendingTenant = ""; _pendingId = ""; _message = "Original explicitly discarded. No source was rerun and no saved record was deleted."; Notify();
    }
    private void BuildRows()
    {
        _rows = null; var sheet = _sheets.SingleOrDefault(value => value.Name == _section);
        if (sheet is not null)
        {
            var table = new DataTable(); foreach (var heading in sheet.Rows[0]) table.Columns.Add(heading, typeof(string)); table.BeginLoadData();
            foreach (var row in sheet.Rows.Skip(1)) if (_search.Length == 0 || row.Any(value => value.Contains(_search, StringComparison.OrdinalIgnoreCase))) table.Rows.Add(row.Cast<object>().ToArray());
            table.EndLoadData(); _rows = table.DefaultView;
        }
        Notify();
    }
    public override void Refresh() { if (_controller is null) return; EnsureContext(); Notify(); }
    private void Notify()
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(Acknowledged), nameof(ReadGuidance), nameof(Outcome), nameof(Message), nameof(LastExport), nameof(Sections), nameof(SelectedSection), nameof(Search), nameof(Rows), nameof(HasPendingOriginal), nameof(PendingSummary) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
