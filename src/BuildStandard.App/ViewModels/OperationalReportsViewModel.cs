using System.Data;
using BuildStandard.Engine.Identity;
using System.IO;
using System.Text;
using System.Windows.Input;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using BuildStandard.Core.Reporting;
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
        IncidentReferences = new(shell,controller);
        IncidentReferences.PropertyChanged += (_, _) => Notify();
        ReadCommand = Command(ReadAsync, () => ReadGuidance.Length == 0);
        StopCommand = Sync(() => { if ((!_reading && !_lookingUp) || Workspace.Idle) throw new ToolkitException("No operational read is active."); Workspace.CancelOperation(); }, () => (_reading || _lookingUp) && Workspace.Busy);
        OpenCommand = Command(ChooseAndOpenAsync, () => CanEdit && Workspace.Profile is not null);
        HtmlCommand = Command(() => ExportAsync(ExportFormat.Html), CanExport);
        JsonCommand = Command(() => ExportAsync(ExportFormat.Json), CanExport);
        CsvCommand = Command(() => ExportAsync(ExportFormat.Csv), CanExport);
        ExcelCommand = Command(() => ExportAsync(ExportFormat.Xlsx), CanExport);
        SaveOriginalCommand = Command(SavePendingOriginalAsync, () => Workspace.Idle && HasPendingOriginal);
        ExportOriginalCommand = Command(ExportPendingOriginalAsync, () => Workspace.Idle && HasPendingOriginal);
        DiscardOriginalCommand = Sync(DiscardPending, () => Workspace.Idle && HasPendingOriginal);
        OpenConnectionCommand = Sync(() => Shell.Navigate("connect"), () => Workspace.Idle);
        SearchGroupsCommand = Command(SearchGroupsAsync, () => GroupSearchGuidance.Length == 0);
        UseGroupCommand = Sync(UseSelectedGroup, () => GroupSelectionGuidance.Length == 0);
        Refresh();
    }
    public IncidentReferencesViewModel IncidentReferences { get; }
    public bool RequiresCloseReview => HasPendingOriginal || _opening || IncidentReferences.RequiresCloseReview;
    private string _groupQuery = "", _groupLookupContext = "", _groupLookupStatus = "Search existing group names, then choose an exact returned name and ID. Team provisioning is checked during capture.", _groupLookupKey = "";
    private int _groupLookupGeneration;
    private bool _lookingUp;
    private ResolvedTenantObject? _selectedGroup;
    public string GroupQuery { get => _groupQuery; set { if (SetProperty(ref _groupQuery, value)) SelectionChanged(); } }
    public IReadOnlyList<ResolvedTenantObject> GroupMatches { get; private set; } = [];
    public ResolvedTenantObject? SelectedGroup { get => _selectedGroup; set { if (SetProperty(ref _selectedGroup, value)) Notify(); } }
    public string GroupLookupStatus => _groupLookupStatus;
    public ICommand SearchGroupsCommand { get; }
    public ICommand UseGroupCommand { get; }
    public string GroupSearchGuidance
    {
        get
        {
            EnsureContext();
            if (!IsCollaborationReport) return "Select Sites, groups and Teams first.";
            if (!CanEdit) return "Wait for the current operation and resolve any pending original before searching.";
            var blocker = _controller.CollaborationLookupBlocker;
            if (blocker.Length != 0) return blocker;
            if (GroupQuery.Trim().Length is < 2 or > 200 || GroupQuery.Any(char.IsControl)) return "Enter an exact group ID or 2 to 200 name characters.";
            return Acknowledged ? "" : "Acknowledge the group-name lookup and its Group.Read.All access before Search groups.";
        }
    }
    public string GroupSelectionGuidance => !CanEdit || !IsCollaborationReport ? "Wait for the operation and resolve any pending original."
        : SelectedGroup is null || !GroupMatches.Contains(SelectedGroup) ? "Select an exact returned group name and ID first."
        : _groupLookupContext != _controller.ContextKey || _groupLookupGeneration != _generation || _groupLookupKey != GroupQuery
            ? "Client or search changed. Search again before adding a group." : "";
    private void ClearGroupLookup()
    {
        GroupMatches = []; _selectedGroup = null; _groupLookupContext = ""; _groupLookupKey = "";
    }
    public async Task SearchGroupsAsync()
    {
        var reason = GroupSearchGuidance;
        if (reason.Length != 0) throw new ToolkitException(reason);
        var context = _context; var query = GroupQuery; var generation = ++_generation;
        _acknowledged = false; _lookingUp = true; ClearGroupLookup();
        _groupLookupStatus = "Looking up bounded existing groups. No group is selected automatically."; Notify();
        try
        {
            var matches = await _controller.FindCollaborationGroupsAsync(query, true, context, () => generation == _generation);
            _controller.RequireContext(context);
            if (generation != _generation) throw new TenantMismatchException("The lookup selection changed; search again.");
            GroupMatches = matches; _groupLookupContext = context; _groupLookupGeneration = generation; _groupLookupKey = query;
            _groupLookupStatus = matches.Count == 0 ? "Lookup completed: no matching groups. Try an exact group ID. This does not prove there are no groups in the tenant."
                : $"{matches.Count} returned groups. Select the exact name and ID, then Add selected group. This does not establish that a Team exists.";
        }
        catch
        {
            ClearGroupLookup(); _groupLookupStatus = "Could not complete the lookup in the current context. No groups were selected; this is not an empty-success result."; throw;
        }
        finally { _lookingUp = false; Notify(); }
    }
    public void UseSelectedGroup()
    {
        var reason = GroupSelectionGuidance;
        if (reason.Length != 0) throw new ToolkitException(reason);
        var selected = SelectedGroup!;
        if (!string.Equals(selected.TenantId, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase) || selected.Kind != ObjectLookupKind.Group)
            throw new TenantMismatchException("The selected group does not belong to this client lookup.");
        var ids = CollaborationGroups.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ids.Contains(selected.ObjectId, StringComparer.OrdinalIgnoreCase)) throw new ConfigurationException("That exact group is already selected.");
        var changed = string.Join('\n', ids.Append(selected.ObjectId));
        _ = CollaborationSelectionInput.Parse(CollaborationSearch, CollaborationSites, changed, IncludeLibraries, IncludeOwners, IncludeMembers);
        CollaborationGroups = changed;
        _groupLookupStatus = "Added locally: " + selected.Label + ". Review sections and acknowledge the exact capture; no tenant changes were made."; Notify();
    }
    private string _collaborationSearch = "", _collaborationSites = "", _collaborationGroups = "";
    private bool _includeLibraries, _includeOwners, _includeMembers;
    public string CollaborationSearch { get => _collaborationSearch; set { if (SetProperty(ref _collaborationSearch, value)) SelectionChanged(); } }
    public string CollaborationSites { get => _collaborationSites; set { if (SetProperty(ref _collaborationSites, value)) SelectionChanged(); } }
    public string CollaborationGroups { get => _collaborationGroups; set { if (SetProperty(ref _collaborationGroups, value)) SelectionChanged(); } }
    public bool IncludeLibraries { get => _includeLibraries; set { if (SetProperty(ref _includeLibraries, value)) SelectionChanged(); } }
    public bool IncludeOwners { get => _includeOwners; set { if (SetProperty(ref _includeOwners, value)) SelectionChanged(); } }
    public bool IncludeMembers { get => _includeMembers; set { if (SetProperty(ref _includeMembers, value)) SelectionChanged(); } }
    private CollaborationReadSelection CollaborationSelection() => CollaborationSelectionInput.Parse(CollaborationSearch, CollaborationSites, CollaborationGroups, IncludeLibraries, IncludeOwners, IncludeMembers);
    private string _incidentStartUtc = "", _incidentEndUtc = "";
    public string IncidentStartUtc { get => _incidentStartUtc; set { if (SetProperty(ref _incidentStartUtc, value)) SelectionChanged(); } }
    public string IncidentEndUtc { get => _incidentEndUtc; set { if (SetProperty(ref _incidentEndUtc, value)) SelectionChanged(); } }
    private SecurityIncidentReadSelection IncidentSelection() => SecurityIncidentReadSelection.Parse(IncidentStartUtc, IncidentEndUtc, DateTimeOffset.UtcNow);
    public string SelectedReportId { get=>_selectedReportId; set { if(SetProperty(ref _selectedReportId,value)) SelectionChanged(); } }
    public string CustomerTenantId { get=>_customerTenantId; set { if(SetProperty(ref _customerTenantId,value)) SelectionChanged(); } }
    public bool PartnerDeclared { get=>_partnerDeclared; set { if(SetProperty(ref _partnerDeclared,value)) SelectionChanged(); } }
    private void SelectionChanged() { _generation++;_acknowledged=false;ClearResult();ClearGroupLookup();Notify(); }
    public bool CanEdit => Workspace.Idle && !_reading && !_opening && !_lookingUp && !HasPendingOriginal && !IncidentReferences.RequiresCloseReview;
    public bool Acknowledged { get => _acknowledged; set { EnsureContext(); if (SetProperty(ref _acknowledged, value)) Notify(); } }
    public string Notice => IsIncidentReport || _report?.ReportId == "security-incidents" ? OperationalContextSchema.IncidentNotice : IsCollaborationReport || _report?.ReportId == "collaboration-inventory" ? OperationalContextSchema.CollaborationNotice : RegisteredReportDocuments.OperationalNotice;
    public sealed record ReportChoice(string Id, string Name);
    public IReadOnlyList<ReportChoice> ReportChoices { get; } = [new("service-health", "Service health"), new("partner-relationships", "Partner relationships"), new("collaboration-inventory", "Sites, groups and Teams"), new("security-incidents", "Security incident context")];
    public bool IsIncidentReport => SelectedReportId == "security-incidents";
    public bool IsCollaborationReport => SelectedReportId == "collaboration-inventory";
    public bool IsPartnerReport => SelectedReportId == "partner-relationships";
    public string ReadLabel => IsIncidentReport ? "Read incident context" : IsCollaborationReport ? "Read selected collaboration metadata" : IsPartnerReport ? "Read partner relationships" : "Read service-health context";
    public string AcknowledgementText => IsIncidentReport
        ? "I acknowledge this source tenant, exact UTC created-date interval and metadata-only incident read. Resolved is a workflow state, not verified remediation. This is not consent, full Defender coverage or approval for a response action."
        : IsCollaborationReport
        ? "I acknowledge this source tenant, the exact site/group search, selected IDs and chosen metadata/direct-role reads. Group-name lookup uses Group.Read.All and selects nothing automatically. This is not consent, complete estate coverage, effective access or permission to change a tenant."
        : IsPartnerReport
        ? "I acknowledge the declared partner source, exact selected customer, fixed relationship and assignment reads and limitations. This does not establish effective customer rights or authorise changes."
        : "I acknowledge this source tenant, the two fixed health reads, actual read-access requirement and limitations. This is not consent or approval to change a tenant.";
    public string AccessPreview => (IsIncidentReport
        ? "Experimental incident context · fixed GET /security/incidents over the displayed UTC interval, start inclusive/end exclusive, at most 31 days. Requires returned SecurityIncident.Read.All and appropriate security RBAC/workload availability. No alert bodies, comments, identities or response actions. "
        : IsCollaborationReport
        ? "Experimental collaboration inventory. Selected site search/metadata/libraries use Sites.Read.All (the permission permits content access; this adapter reads metadata only). Selected groups/owners use existing Group.Read.All; direct Team members use TeamMember.Read.All. Optional site/Team access is default off. FullControl permissions, OneDrive provisioning routes, content and inherited permissions are blocked. "
        : IsPartnerReport
        ? "Experimental partner context · fixed relationship and direct assignment GETs using actually returned DelegatedAdminRelationship.Read.All. The connected source remains the partner; customer selection never connects to that customer. Direct role claims are not effective access. At most 50 child inventories. "
        : "Experimental service-health context · fixed overview and issue GETs using actually returned ServiceHealth.Read.All. Advisories are not tenant availability. ")
        + "No sign-in, consent or write occurs here. Five minutes total, 100 requests, 5,000 rows and 16 MiB. Read purpose is not proof that a token has no additional rights.";
    public string ReadGuidance
    {
        get
        {
            EnsureContext();
            if (_reading || !Workspace.Idle) return _reading ? "Operational capture/original retention is active. Stop keeps earlier admitted observations; closing waits for recovery bytes." : "Wait for the current operation to finish.";
            if (_opening) return "The original operational file is being checked. Wait before another open or read; no service request is made.";
            if (HasPendingOriginal) return "Save, export or explicitly discard the pending original before another read. It retains its original source tenant.";
            if (IncidentReferences.RequiresCloseReview) return "Save or explicitly discard pending incident references before replacing their source.";
            CollaborationReadSelection? choice = null; SecurityIncidentReadSelection? incidents = null;
            try { if (IsCollaborationReport) choice = CollaborationSelection(); if (IsIncidentReport) incidents = IncidentSelection(); }
            catch (ToolkitException error) { return error.Message; }
            var blocker = _controller.OperationalReadBlockerFor(SelectedReportId, IsPartnerReport ? CustomerTenantId : null, IsPartnerReport && PartnerDeclared, choice, incidents);
            return blocker.Length != 0 ? blocker : Acknowledged ? "" : "Review the source, fixed reads, access and limitations, then tick acknowledgement before Read.";
        }
    }
    public string Outcome => _report is null ? "No current operational result." : $"{_report.ReportId} context · {_report.Status} · source {_report.TenantId}" + (_report.CustomerTenantId is null ? "" : $" · selected customer {_report.CustomerTenantId}") + $" · as-of {_report.AsOfUtc}\n" + string.Join(" · ", _report.Sections.Select(section => section.Id + ": " + section.Status));
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
    private void ClearResult() {
        IncidentReferences?.ClearCurrent(); _report = null; _original = []; _sheets = []; _section = ""; _rows = null; _export = ""; }
    private void EnsureContext()
    {
        var context = _controller.ContextKey;
        if (_context == context) return;
        _context = context; _generation++; _acknowledged = false; ClearResult(); ClearGroupLookup(); _groupQuery = "";
        _collaborationSearch = ""; _collaborationSites = ""; _collaborationGroups = "";
        _includeLibraries = false; _includeOwners = false; _includeMembers = false;
        _incidentStartUtc = ""; _incidentEndUtc = "";
        _message = "Client, session, catalogue or evidence changed. Former results and acknowledgement were cleared; pending originals retain their source.";
    }
    private bool CanExport() => Workspace.Idle && _report is not null && _context == _controller.ContextKey;
    public async Task ReadAsync()
    {
        var reason = ReadGuidance; if (reason.Length != 0) throw new ToolkitException(reason);
        var context = _context; var generation = ++_generation;
        var collaboration = IsCollaborationReport ? CollaborationSelection() : null;
        var incidents = IsIncidentReport ? IncidentSelection() : null;
        var reportId=_selectedReportId;var customer=IsPartnerReport ? _customerTenantId : "";var partnerDeclared=IsPartnerReport && _partnerDeclared;
        _acknowledged = false; _reading = true; ClearResult(); Notify();
        try
        {
            await _controller.ReadOperationalAsync(true, context, () => generation == _generation, async report =>
            {
                var original = await SerialiseOriginal(report);
                _pending = original.ToArray(); _pendingTenant = report.TenantId; _pendingId = report.Id;
                await SetReportAsync(report, original, false, context, generation);
                _message = "Source reads finished with the states above. Save or export original JSON before closing or reading again. Other formats do not replace the original.";
            },reportId,customer.Length==0?null:customer,partnerDeclared,collaboration,incidents);
        }
        finally { _reading = false; Notify(); }
    }
    private async Task SetReportAsync(OperationalContextReport report, byte[] original, bool supplied, string context, int generation)
    {
        var projection = await Task.Run(() => (sheets: RegisteredReportDocuments.OperationalSheets(report, supplied),
            source: report.ReportId == "security-incidents" ? OperationalContextFile.Read(original,report.TenantId) : null));
        _controller.RequireContext(context);
        if (generation != _generation) throw new TenantMismatchException("Operational context changed while projecting the result.");
        if (projection.source is not null) IncidentReferences.Admit(projection.source,context);
        else IncidentReferences.ClearCurrent();
        _report = report; _original = original.ToArray(); _supplied = supplied; _sheets = projection.sheets; _section = _sheets[0].Name; _search = ""; BuildRows(); Notify();
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
        foreach (var name in new[] { nameof(IsIncidentReport), nameof(IncidentStartUtc), nameof(IncidentEndUtc), nameof(GroupQuery), nameof(GroupMatches), nameof(SelectedGroup), nameof(GroupLookupStatus), nameof(GroupSearchGuidance), nameof(GroupSelectionGuidance), nameof(IsCollaborationReport), nameof(Notice), nameof(CollaborationSearch), nameof(CollaborationSites), nameof(CollaborationGroups), nameof(IncludeLibraries), nameof(IncludeOwners), nameof(IncludeMembers), nameof(IsPartnerReport), nameof(ReadLabel), nameof(AccessPreview), nameof(AcknowledgementText), nameof(CanEdit), nameof(Acknowledged), nameof(ReadGuidance), nameof(Outcome), nameof(Message), nameof(LastExport), nameof(Sections), nameof(SelectedSection), nameof(Search), nameof(Rows), nameof(HasPendingOriginal), nameof(PendingSummary) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
