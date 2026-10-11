using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Engine.Reports;
using Entry = BDIT.TenantToolkit.Engine.Reports.MailboxCapacityEvidenceReader.Entry;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>Pairs historical evidence for a derived local review; no collector, authenticator or tenant mutation.</summary>
public sealed class MailboxCapacityViewModel : PageViewModel
{
    public sealed record Row(MailboxCapacityInventory.Mailbox Source)
    {
        public string Name => string.IsNullOrWhiteSpace(Source.Capacity.Observed.Name) ? "Unknown / not returned" : Source.Capacity.Observed.Name;
        public string Smtp => string.IsNullOrWhiteSpace(Source.Capacity.Observed.PrimarySmtpAddress) ? "Unknown / not returned" : Source.Capacity.Observed.PrimarySmtpAddress;
        public string Configured => Source.Capacity.Configured100GB is null ? "Unknown" : Source.Capacity.Configured100GB.Value ? "Yes" : "No";
        public string Entitlement => Source.Capacity.Eligibility switch { MailboxCapacity.Eligible => "Eligible", MailboxCapacity.NotEligible => "Not eligible", _ => "Unable to check" };
        public string Finding => Source.Capacity.Finding switch
        {
            "Configured100GBAndEligible" => "100 GB; eligible", "Configured100GBButNotEligible" => "100 GB; not eligible",
            "Configured100GBEntitlementUnconfirmed" => "100 GB; entitlement unknown", "EligibleButLowerQuota" => "Eligible; lower quota",
            "EligibleWithDifferentQuota" => "Eligible; different quota", MailboxCapacity.NotEligible => "Not eligible", _ => "Unable to check"
        };
        public string ReadState => Source.RowReadStatus;
        public string Details
        {
            get
            {
                var c = Source.Capacity; var o = c.Observed;
                var lines = new List<string> { "Name: " + Name, "SMTP: " + Smtp, "Mailbox ID: " + o.MailboxId,
                    "Entra object ID: " + (o.ExternalDirectoryObjectId ?? "Unknown / not returned"), "Mailbox type: " + o.MailboxType,
                    "Recorded at: " + o.CapturedAt, "Row read state: " + Source.RowReadStatus, "Row error: " + (Source.RowError ?? "None recorded"),
                    "Configured 100 GB: " + Configured, "Entitlement: " + c.Eligibility, c.EligibilityReason, "Finding: " + c.Finding,
                    "Has archive: " + (Source.HasArchive is null ? "Unknown" : Source.HasArchive.Value ? "Yes" : "No"),
                    "Primary quota read: " + o.PrimaryReadStatus, "Primary size read: " + o.PrimarySizeReadStatus,
                    "Archive read: " + o.ArchiveReadStatus, "Archive size read: " + o.ArchiveSizeReadStatus };
                foreach (var (name, m) in new[] { ("Primary size", c.PrimarySize), ("Warning quota", c.WarningQuota), ("Send quota", c.SendQuota), ("Send/receive quota", c.SendReceiveQuota), ("Archive size", c.ArchiveSize), ("Archive quota", c.ArchiveQuota) })
                    lines.Add(name + ": " + (m.Raw ?? "Unknown / not returned") + " · " + m.State + " · " + (m.Bytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown") + " bytes · " + m.Reason);
                return string.Join("\n", lines);
            }
        }
    }
    private readonly MailboxCapacityEvidenceBrowser _browser;
    private readonly GraphReportsController _guard;
    private string _context = "", _mailboxMode = "Saved record", _licenceMode = "Saved record", _mailboxFile = "", _licenceFile = "", _search = "", _entitlement = "All";
    private Entry? _mailboxEntry, _licenceEntry;
    private Row? _selected;
    private int _tab, _version;
    private MailboxCapacityReviewDocuments.Document? _document;
    public MailboxCapacityViewModel(ShellViewModel shell) : base(shell, "Mailbox capacity")
    {
        _browser = new(Workspace.Evidence); _guard = new(Workspace);
        RefreshSourcesCommand = Sync(RefreshSources, CanUseContext);
        ReviewCommand = Sync(Review, () => CanUseContext() && SelectionProblem.Length == 0);
        ChooseMailboxCommand = Sync(() => ChooseFile(true), CanUseContext);
        ChooseLicenceCommand = Sync(() => ChooseFile(false), CanUseContext);
        ExportHtmlCommand = Command(() => ExportAsync(ExportFormat.Html), CanExport);
        ExportJsonCommand = Command(() => ExportAsync(ExportFormat.Json), CanExport);
        ExportCsvCommand = Command(() => ExportAsync(ExportFormat.Csv), CanExport);
        ExportXlsxCommand = Command(() => ExportAsync(ExportFormat.Xlsx), CanExport);
        CopyRowCommand = Sync(() => { RequireAccepted(); if (SelectedRow is null) throw new ToolkitException("Select a mailbox first."); Shell.CopyToClipboard(SelectedDetails); }, () => CanExport() && SelectedRow is not null);
        CopyProvenanceCommand = Sync(() => { RequireAccepted(); Shell.CopyToClipboard(Provenance); }, CanExport);
        Refresh();
    }
    public IReadOnlyList<string> SourceModes { get; } = ["Saved record", "Supplied file"];
    public IReadOnlyList<string> EntitlementFilters { get; } = ["All", "Eligible", "Not eligible", "Unable to check"];
    public ObservableCollection<Entry> MailboxRecords { get; } = new();
    public ObservableCollection<Entry> LicenceRecords { get; } = new();
    public ObservableCollection<Row> Rows { get; } = new();
    public int SelectedTab { get => _tab; set => SetProperty(ref _tab, value); }
    public string MailboxMode { get => _mailboxMode; set { if (SetProperty(ref _mailboxMode, SourceModes.Contains(value) ? value : "Saved record")) ChangedSelection(); } }
    public string LicenceMode { get => _licenceMode; set { if (SetProperty(ref _licenceMode, SourceModes.Contains(value) ? value : "Saved record")) ChangedSelection(); } }
    public bool MailboxSupplied => MailboxMode == "Supplied file";
    public bool LicenceSupplied => LicenceMode == "Supplied file";
    public string MailboxFile { get => _mailboxFile; set { if (SetProperty(ref _mailboxFile, value ?? "")) ChangedSelection(); } }
    public string LicenceFile { get => _licenceFile; set { if (SetProperty(ref _licenceFile, value ?? "")) ChangedSelection(); } }
    public Entry? SelectedMailbox { get => _mailboxEntry; set { if (SetProperty(ref _mailboxEntry, value)) ChangedSelection(); } }
    public Entry? SelectedLicence { get => _licenceEntry; set { if (SetProperty(ref _licenceEntry, value)) ChangedSelection(); } }
    public string MailboxListing { get; private set; } = "Refresh to list saved mailbox inventories.";
    public string LicenceListing { get; private set; } = "Refresh to list saved users/licences reports.";
    public string Search { get => _search; set { if (SetProperty(ref _search, value ?? "")) Filter(); } }
    public string EntitlementFilter { get => _entitlement; set { if (SetProperty(ref _entitlement, EntitlementFilters.Contains(value) ? value : "All")) Filter(); } }
    public Row? SelectedRow { get => _selected; set { if (SetProperty(ref _selected, value)) { OnPropertyChanged(nameof(SelectedDetails)); CommandManager.InvalidateRequerySuggested(); } } }
    public string SelectedDetails => SelectedRow?.Details ?? "Select a mailbox to inspect exact identities, raw primary/archive values, Unlimited and read reasons.";
    public string ContextSummary => Workspace.Profile is null ? "Select a client in Connect for offline evidence review."
        : Workspace.Profile.Company + " · tenant " + Workspace.Profile.TenantId + " · historical offline review";
    public string Notice => MailboxCapacityReviewDocuments.Notice;
    public string Summary => _document is null ? "No accepted mailbox capacity review." : MailboxCapacityReviewDocuments.Summary(_document);
    public string SourceTimes => _document is null ? "Both source files keep their own recorded times, digest, status and limitations. No state is refreshed."
        : "Mailboxes: " + _document.Review.MailboxSource.StartedAt + " to " + _document.Review.MailboxSource.EndedAt
            + "\nLicences: " + _document.Review.LicenceSource.StartedAt + " to " + _document.Review.LicenceSource.EndedAt;
    public string Provenance => _document is null ? "No accepted source pair." : string.Join("\n", MailboxCapacityReviewDocuments.Sheets(_document)[0].Rows.Skip(1).Select(row => row[0] + ": " + row[1]));
    public string FilterSummary => $"{Rows.Count} of {_document?.Review.Mailboxes.Count ?? 0} recorded mailboxes visible. Display filters only; exports include every review row and both original sources.";
    public string LastExport { get; private set; } = "";
    public string ReviewGuidance => Workspace.Profile is null ? ContextSummary : !Workspace.Idle ? "Wait for the current operation to finish." : SelectionProblem.Length == 0 ? "Review these two historical files locally; no tenant connection is needed." : SelectionProblem;
    public string ExportGuidance => _document is null ? "Review a valid source pair before exporting or copying provenance." : !Workspace.Idle ? "Wait for the current operation to finish." : "Exports contain the full derived review and source provenance. JSON includes both unchanged original source records; it is not new evidence.";
    private string SelectionProblem => MailboxSupplied ? string.IsNullOrWhiteSpace(MailboxFile) ? "Choose the supplied mailbox inventory file." : LicenceProblem
        : SelectedMailbox is null ? "Select a saved mailbox inventory or choose Supplied file." : !SelectedMailbox.CanOpen || !MailboxRecords.Contains(SelectedMailbox) ? SelectedMailbox.Problem.Length == 0 ? "Refresh and select a mailbox record from this listing." : SelectedMailbox.Problem : LicenceProblem;
    private string LicenceProblem => LicenceSupplied ? string.IsNullOrWhiteSpace(LicenceFile) ? "Choose the supplied users/licences file." : ""
        : SelectedLicence is null ? "Select a saved users/licences report or choose Supplied file." : !SelectedLicence.CanOpen || !LicenceRecords.Contains(SelectedLicence) ? SelectedLicence.Problem.Length == 0 ? "Refresh and select a licence record from this listing." : SelectedLicence.Problem : "";
    public ICommand RefreshSourcesCommand { get; }
    public ICommand ReviewCommand { get; }
    public ICommand ChooseMailboxCommand { get; }
    public ICommand ChooseLicenceCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportXlsxCommand { get; }
    public ICommand CopyRowCommand { get; }
    public ICommand CopyProvenanceCommand { get; }
    private bool CanUseContext() => Workspace.Idle && Workspace.Profile is not null && _guard.ContextKey == _context;
    private bool CanExport() => CanUseContext() && _document is not null;
    private void RequireContext()
    { _guard.RequireContext(_context); if (!Workspace.Idle) throw new ToolkitException("Wait for the current operation to finish."); }
    private void RequireAccepted()
    { RequireContext(); if (_document is null) throw new ToolkitException("Review the current source pair first."); }
    private void ChangedSelection() { _version++; Clear(); Notify(); }
    private void Clear() { _document = null; Rows.Clear(); SelectedRow = null; LastExport = ""; }
    public void RefreshSources()
    {
        RequireContext(); ChangedSelection(); SelectedMailbox = null; SelectedLicence = null;
        MailboxRecords.Clear(); LicenceRecords.Clear();
        var tenant = Workspace.Profile!.TenantId;
        var m = _browser.ListMailboxes(tenant); var l = _browser.ListLicences(tenant);
        foreach (var entry in m.Entries) MailboxRecords.Add(entry); foreach (var entry in l.Entries) LicenceRecords.Add(entry);
        MailboxListing = (m.Complete ? "" : "INCOMPLETE — ") + m.Detail; LicenceListing = (l.Complete ? "" : "INCOMPLETE — ") + l.Detail; Notify();
    }
    private void ChooseFile(bool mailbox)
    {
        RequireContext(); var dialog = new Microsoft.Win32.OpenFileDialog { Title = mailbox ? "Choose mailbox inventory evidence" : "Choose users/licences evidence", Filter = "Report evidence (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        RequireContext(); if (mailbox) { MailboxMode = "Supplied file"; MailboxFile = dialog.FileName; } else { LicenceMode = "Supplied file"; LicenceFile = dialog.FileName; }
    }
    public void Review()
    {
        RequireContext(); Clear(); Notify();
        try
        {
            if (SelectionProblem.Length != 0) throw new ToolkitException(SelectionProblem);
            var version = _version; var context = _context; var tenant = Workspace.Profile!.TenantId;
            var mailbox = _browser.Mailboxes(tenant, MailboxSupplied ? null : SelectedMailbox!.Id, MailboxFile);
            var licence = _browser.Licences(tenant, LicenceSupplied ? null : SelectedLicence!.Id, LicenceFile);
            var document = MailboxCapacityReviewDocuments.Create(mailbox, licence, tenant);
            _guard.RequireContext(context); if (_version != version) throw new ToolkitException("Source selection changed during review; no result accepted.");
            _document = document; SelectedTab = 1; Filter();
        }
        finally { Notify(); }
    }
    public async Task ExportAsync(ExportFormat format)
    {
        RequireAccepted(); var document = _document!; var version = _version; var context = _context;
        void Pin()
        { _guard.RequireContext(context); if (_version != version || !ReferenceEquals(_document, document)) throw new ToolkitException("Source pair changed during export; no output is attributed to this selection."); }
        var file = await Workspace.ExportAsync(() => { Pin(); return Workspace.Exporter.ExportMailboxCapacity(document, format); }, "Exporting historical mailbox review", "Full derived review and original provenance; no tenant reads or changes.");
        Pin(); LastExport = "Full derived review exported: " + file; Notify();
    }
    private void Filter()
    {
        Rows.Clear(); SelectedRow = null;
        if (_document is not null) foreach (var m in _document.Review.Mailboxes)
        { var row = new Row(m); if ((Search.Length == 0 || row.Details.Contains(Search, StringComparison.OrdinalIgnoreCase)) && (EntitlementFilter == "All" || row.Entitlement == EntitlementFilter)) Rows.Add(row); }
        Notify();
    }
    public override void Refresh()
    {
        if (_guard is null) return;
        var current = _guard.ContextKey;
        if (_context != current) { _context = current; _version++; Clear(); SelectedMailbox = null; SelectedLicence = null; MailboxRecords.Clear(); LicenceRecords.Clear(); MailboxFile = ""; LicenceFile = ""; MailboxListing = "Refresh for this tenant's saved mailbox records."; LicenceListing = "Refresh for this tenant's saved licence records."; }
        Notify();
    }
    private void Notify()
    {
        foreach (var name in new[] { nameof(MailboxSupplied), nameof(LicenceSupplied), nameof(MailboxListing), nameof(LicenceListing), nameof(ContextSummary), nameof(Summary), nameof(SourceTimes), nameof(Provenance), nameof(FilterSummary), nameof(SelectedDetails), nameof(LastExport), nameof(ReviewGuidance), nameof(ExportGuidance) }) OnPropertyChanged(name);
        CommandManager.InvalidateRequerySuggested();
    }
}
