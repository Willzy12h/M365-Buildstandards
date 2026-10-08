using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows.Input;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Reports;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class CollectionRow
{
    public string Collection { get; init; } = "";
    public string Api { get; init; } = "";
    public string Status { get; init; } = "";
    public int Count { get; init; }
    public string Detail { get; init; } = "";
}

public sealed class ObjectRow
{
    public string Collection { get; init; } = "";
    public string CollectionKey { get; init; } = "";
    public string Name { get; init; } = "";
    public string ObjectId { get; init; } = "";
    public string Type { get; init; } = "";
    public string State => DescribeState(Item);
    public string StateExplanation => "State is the resource's reported state field, not a compliance result. 'Not reported' means the response has no state field (some object types do not expose one). 'Unknown' means the field was empty, null or unexpected. Neither means enabled, disabled or compliant; inspect settings, targeting and collection completeness separately.";
    public string Targeting { get; init; } = "";
    public JsonObject Item { get; init; } = new();

    public static string DescribeState(JsonObject item)
    {
        if (!item.TryGetPropertyValue("state", out var state)) return "Not reported";
        if (state is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)) return "Unknown";
        return text;
    }
}

public sealed class ConfigurationViewModel : PageViewModel
{
    private string _filterCollection = "All";
    private string _search = "";
    private ObjectRow? _selectedObject;
    private SnapshotSummary? _selectedStored;
    private NameResolver _names = new();
    private string _mailDomain = "";
    private string _exchangeImportFile = "";
    private string _domainContext = "";
    private string _proposalContext = "";
    private string _proposalTenant = "";
    private ControlDefinition? _proposalControl;

    public ConfigurationViewModel(ShellViewModel shell) : base(shell, "Configuration")
    {
        CaptureCommand = Command(Workspace.CaptureAsync, () => Workspace.IsConnected && Workspace.Idle);
        LoadStoredCommand = Sync(() => { if (SelectedStored is not null) Workspace.LoadStoredSnapshot(SelectedStored.Id); }, () => SelectedStored is not null && Workspace.Idle);
        ExportHtmlCommand = Command(() => Export(ExportFormat.Html), () => Workspace.Snapshot is not null);
        ExportJsonCommand = Command(() => Export(ExportFormat.Json), () => Workspace.Snapshot is not null);
        ExportCsvCommand = Command(() => Export(ExportFormat.Csv), () => Workspace.Snapshot is not null);
        ExportXlsxCommand = Command(() => Export(ExportFormat.Xlsx), () => Workspace.Snapshot is not null);
        OpenExportCommand = Sync(() => Infrastructure.ShellFolders.RevealFile(_lastExportFile), () => _lastExportFile.Length > 0);
        CopySummaryCommand = CopyText(() => SnapshotText);
        ExportExchangeEvidenceCommand = Command(async () =>
        {
            var snapshot = Workspace.ExchangeSnapshot ?? throw new ToolkitException("Capture or import Exchange observations first.");
            _lastExportFile = await Workspace.ExportAsync(() => Workspace.Exporter.ExportSnapshot(snapshot, Workspace.Standard, ExportFormat.Json));
            LastExport = "Exported separate read-only Exchange/Purview observations: " + _lastExportFile;
            OnPropertyChanged(nameof(LastExport));
        }, () => Workspace.ExchangeSnapshot is not null && Workspace.Idle);
        CaptureExchangeCommand = Command(() => Workspace.CaptureExchangeAsync(IncludePurview), () => SupportsExchange && Workspace.IsConnected && Workspace.Idle);
        SelectExchangeDomainCommand = Sync(() => Workspace.SelectExchangeDomain(SelectedMailDomain ?? ""), () => Workspace.Idle && SelectedMailDomain is not null);
        SaveMailDomainCommand = Sync(() => Workspace.SaveExchangeDomain(MailDomainInput), () => SupportsExchange && Workspace.Profile is not null && Workspace.Idle);
        ExportExchangeCaptureCommand = Command(ExportExchangeCapture, () => SupportsExchange && Workspace.Profile is not null && Workspace.Idle);
        ChooseExchangeCaptureCommand = Sync(() =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Read-only Exchange capture (*.json)|*.json", Title = "Select an engineer-generated Exchange capture" };
            if (dialog.ShowDialog() == true) ExchangeImportFile = dialog.FileName;
        }, () => SupportsExchange && Workspace.Idle);
        ImportExchangeCaptureCommand = Sync(() => Workspace.ImportExchangeCapture(ExchangeImportFile, MailDomainInput), () => SupportsExchange && Workspace.Profile is not null && Workspace.Idle);
        CheckExchangeDnsCommand = Command(() => Workspace.CheckExchangeDnsAsync(), () => Workspace.ExchangeSnapshot?.ExchangeCapture is not null && Workspace.Profile is not null && Workspace.Idle);
        ExportExchangeProposalCommand = Sync(() =>
        {
            _lastExportFile = Workspace.ExportExchangeProposal(SelectedProposalControl?.Id ?? "", ProposalTenantConfirmation, MailDomainInput);
            LastExport = "Exported commented review proposal; no commands were run: " + _lastExportFile;
            ProposalTenantConfirmation = "";
            OnPropertyChanged(nameof(LastExport)); RaiseAll();
        }, () => SupportsExchange && Workspace.ExchangeSnapshot?.ExchangeCapture is not null && Workspace.Idle);
        Refresh();
    }

    public ICommand CaptureCommand { get; }
    public ICommand LoadStoredCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportXlsxCommand { get; }
    public ICommand OpenExportCommand { get; }
    public ICommand CopySummaryCommand { get; }
    public ICommand ExportExchangeEvidenceCommand { get; }
    public ICommand CaptureExchangeCommand { get; }
    public ICommand SelectExchangeDomainCommand { get; }
    public bool IncludePurview { get; set; } = true;
    public IReadOnlyList<string> AcceptedMailDomains => Workspace.ExchangeDomains;
    public IReadOnlyList<CollectionRow> ExchangeCollectionStatus => Workspace.ExchangeSnapshot?.ExchangeCapture is not { } capture
        ? Array.Empty<CollectionRow>() : ExchangeCaptureSchema.Definitions.Select(d =>
        {
            capture.Collections.TryGetValue(d.Key, out var collection);
            var complete = ExchangeCaptureSchema.Complete(capture, d.Key, out _);
            return new CollectionRow { Collection = d.Value.Command, Status = collection?.Status == CaptureStatus.Collected && !complete
                ? "Incomplete fields" : collection?.Status ?? CaptureStatus.NotAttempted, Count = collection?.Items.Count ?? 0,
                Detail = collection?.Error ?? (complete ? "Configuration observations only; effective behaviour still needs engineer verification."
                    : "Read not complete. Check module compatibility, read RBAC and the selected service; re-capture. Missing settings cannot be inferred.") };
        }).ToList();
    private string? _selectedMailDomain;
    public string? SelectedMailDomain { get => _selectedMailDomain; set => SetProperty(ref _selectedMailDomain, value); }
    public string ExchangeDependencyGuidance => ExchangeCaptureRunner.DependencyGuidance;
    public ICommand SaveMailDomainCommand { get; }
    public ICommand ExportExchangeCaptureCommand { get; }
    public ICommand ChooseExchangeCaptureCommand { get; }
    public ICommand ImportExchangeCaptureCommand { get; }
    public ICommand CheckExchangeDnsCommand { get; }
    public ICommand ExportExchangeProposalCommand { get; }
    public ObservableCollection<ControlDefinition> ProposalControls { get; } = new();
    public ControlDefinition? SelectedProposalControl { get => _proposalControl; set => SetProperty(ref _proposalControl, value); }
    public string ProposalTenantConfirmation { get => _proposalTenant; set => SetProperty(ref _proposalTenant, value); }
    public bool SupportsExchange => Workspace.Standard?.Controls.Any(c => c.Area == "Exchange") == true;
    public string MailDomainInput { get => _mailDomain; set => SetProperty(ref _mailDomain, value); }
    public string ExchangeImportFile { get => _exchangeImportFile; set => SetProperty(ref _exchangeImportFile, value); }
    public string ExchangeSummary => Workspace.ExchangeSnapshot?.ExchangeCapture is { } capture && capture.TenantId == Workspace.Profile?.TenantId
        ? $"{(Workspace.ExchangeCapturedByTool ? "Collected by the tool" : "Imported offline")} {capture.CapturedAt} for {capture.Domain}; module {capture.ModuleVersion}. {capture.Dns.Count} DNS observation(s). Review Exchange and Purview in Assessment. Graph capture is preserved separately. These observations cannot authorise a write; exported source claims are not signed."
        : "No Exchange/Purview observations loaded. Select Capture Exchange and Purview to sign in and read configuration. A supported preinstalled module is required; imports remain available below.";

    public ObservableCollection<CollectionRow> Collections { get; } = new();
    public ObservableCollection<ObjectRow> Objects { get; } = new();
    public ObservableCollection<string> CollectionFilters { get; } = new();
    public ObservableCollection<SnapshotSummary> StoredSnapshots { get; } = new();
    public ObservableCollection<PropertyDifference> SelectedSettings { get; } = new();

    private readonly List<ObjectRow> _allObjects = new();

    public string FilterCollection { get => _filterCollection; set { if (SetProperty(ref _filterCollection, value)) ApplyFilter(); } }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }
    public SnapshotSummary? SelectedStored { get => _selectedStored; set => SetProperty(ref _selectedStored, value); }
    public string LastExport { get; private set; } = "";
    private string _lastExportFile = "";

    public ObjectRow? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (!SetProperty(ref _selectedObject, value)) return;
            SelectedSettings.Clear();
            if (value is null) return;
            foreach (var (path, node) in CanonicalJson.Leaves(value.Item))
            {
                if (path.StartsWith("_", StringComparison.Ordinal) && path != "_assignments") continue;
                SelectedSettings.Add(new PropertyDifference { Setting = path, Current = _names.Render(node), Standard = "", Match = true });
            }
            OnPropertyChanged(nameof(SelectedObjectJson));
        }
    }

    public string SelectedObjectJson => SelectedObject?.Item.ToJsonString(ToolkitJson.Options) ?? "";

    public string SnapshotSummary
    {
        get
        {
            var capture = Workspace.Snapshot;
            if (capture is null) return "No capture loaded. Connect and capture, or open saved evidence for offline review.";
            var origin = Workspace.SnapshotIsLive ? "Live capture" : "Stored capture · offline";
            var time = Timestamps.TryParse(capture.CapturedAt, out var captured)
                ? captured.ToUniversalTime().ToString("dd MMM yyyy, HH:mm 'UTC'", System.Globalization.CultureInfo.GetCultureInfo("en-GB"))
                : "capture time unknown";
            return origin + " · " + (capture.Complete ? "complete" : "INCOMPLETE · review collection status") +
                " · " + time + " · standard " + capture.StandardRelease;
        }
    }

    public string SnapshotText
    {
        get
        {
            var s = Workspace.Snapshot;
            if (s is null) return "No configuration loaded. Connect, then read the tenant configuration. Stored captures can be opened for offline review.";
            var live = Workspace.SnapshotIsLive ? "Live capture" : "Stored capture (offline review - not eligible for deployment)";
            return $"{live} · {s.TenantName} ({s.PrimaryDomain}) · captured {s.CapturedAt} by {s.CapturedBy} · standard {s.StandardRelease} · {(s.Complete ? "complete" : "INCOMPLETE - review collection errors")} · id {s.Id}";
        }
    }

    private async Task Export(ExportFormat format)
    {
        var snapshot = Workspace.Snapshot ?? throw new ToolkitException("Read the tenant configuration first.");
        var standard = Workspace.Standard;
        _lastExportFile = await Workspace.ExportAsync(() => Workspace.Exporter.ExportSnapshot(snapshot, standard, format));
        var objects = snapshot.Collections.Values.Sum(c => c.Count);
        LastExport = $"Exported {objects} object(s) from {snapshot.Collections.Count(c => c.Value.Status == CaptureStatus.Collected)} collection(s): {_lastExportFile}";
        OnPropertyChanged(nameof(LastExport));
        RaiseAll();
    }

    private async Task ExportExchangeCapture()
    {
        var domain = MailDomain.Validate(MailDomainInput);
        var profile = Workspace.Profile ?? throw new ToolkitException("Select a client first.");
        _lastExportFile = await Workspace.ExportAsync(() => Workspace.Exporter.ExportExchangeReadScript(profile.TenantId, domain, DateTimeOffset.UtcNow));
        LastExport = "Exported read-only script for manual review and execution: " + _lastExportFile;
        OnPropertyChanged(nameof(LastExport)); RaiseAll();
    }

    public override void Refresh()
    {
        var proposalContext = Workspace.Profile?.TenantId + "|" + Workspace.ExchangeSnapshot?.Id;
        if (_proposalContext != proposalContext)
        { _proposalContext = proposalContext; ProposalTenantConfirmation = ""; SelectedProposalControl = null; }
        var selectedId = SelectedProposalControl?.Id;
        ProposalControls.Clear();
        foreach (var control in Workspace.Standard?.Controls.Where(c => c.Area is "Exchange" or "Purview") ?? Enumerable.Empty<ControlDefinition>())
            ProposalControls.Add(control);
        SelectedProposalControl = ProposalControls.FirstOrDefault(c => c.Id == selectedId);
        var savedDomain = Workspace.Profile?.Parameters.PolicyInputs?.GetValueOrDefault("exchangeDomain")?.ToString() ?? "";
        var context = (Workspace.Profile?.TenantId ?? "") + "|" + savedDomain;
        if (_domainContext != context) { _domainContext = context; MailDomainInput = savedDomain; ExchangeImportFile = ""; }
        OnPropertyChanged(nameof(SupportsExchange)); OnPropertyChanged(nameof(ExchangeSummary));
        OnPropertyChanged(nameof(AcceptedMailDomains)); OnPropertyChanged(nameof(ExchangeCollectionStatus));
        if (!AcceptedMailDomains.Contains(SelectedMailDomain ?? "", StringComparer.OrdinalIgnoreCase)) SelectedMailDomain = AcceptedMailDomains.FirstOrDefault();
        if (Workspace.ExchangeSnapshot?.ExchangeCapture is { } current) MailDomainInput = current.Domain;
        Collections.Clear();
        _allObjects.Clear();
        CollectionFilters.Clear();
        CollectionFilters.Add("All");
        StoredSnapshots.Clear();
        if (Workspace.Profile is not null)
        {
            try { foreach (var s in Workspace.Evidence.ListSnapshots(Workspace.Profile.TenantId)) StoredSnapshots.Add(s); }
            catch (ToolkitException ex) { Shell.ShowError(ex); }
        }
        var snapshot = Workspace.Snapshot;
        _names = NameResolver.FromSnapshot(snapshot, Workspace.Profile);
        if (snapshot is not null)
        {
            foreach (var (key, c) in snapshot.Collections)
            {
                var def = Workspace.Standard?.FindCollection(key);
                var label = def?.Label ?? key;
                Collections.Add(new CollectionRow
                {
                    Collection = label,
                    Api = c.Api,
                    Status = c.Status != CaptureStatus.Collected ? "Not collected" : c.DetailIncomplete ? "Partially collected" : c.Count == 0 ? "No objects returned" : "Collected",
                    Count = c.Count,
                    Detail = c.Error ?? (c.DetailIncomplete ? "Assignment or setting details incomplete for at least one object. Recapture with this build to record individual failures here; existing capture logs may also contain them." : "Read completed; this is not a compliance result.")
                });
                CollectionFilters.Add(label);
                foreach (var item in c.Items)
                {
                    var id = Text(item["id"]) ?? "";
                    _allObjects.Add(new ObjectRow
                    {
                        Collection = label,
                        CollectionKey = key,
                        Name = Text(item[def?.NameProperty ?? "displayName"]) ?? Text(item["displayName"]) ?? Text(item["name"]) ?? Text(item["userPrincipalName"]) ?? id,
                        ObjectId = id,
                        Type = (Text(item["@odata.type"]) ?? label).Replace("#microsoft.graph.", "", StringComparison.Ordinal),
                        Targeting = _names.AssignmentSummary(item),
                        Item = item
                    });
                }
            }
        }
        if (!CollectionFilters.Contains(_filterCollection)) _filterCollection = "All";
        ApplyFilter();
        OnPropertyChanged(nameof(SnapshotText));
        OnPropertyChanged(nameof(SnapshotSummary));
        OnPropertyChanged(nameof(FilterCollection));
    }

    /// <summary>
    /// A captured property as text, or null when it is absent or not a string. This page lists every object from every
    /// collection, and it refreshes on every workspace change; one object with an unexpected value must not stop it.
    /// </summary>
    private static string? Text(System.Text.Json.Nodes.JsonNode? node) =>
        node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private void ApplyFilter()
    {
        Objects.Clear();
        var q = Search.Trim();
        foreach (var row in _allObjects)
        {
            if (FilterCollection != "All" && row.Collection != FilterCollection) continue;
            if (q.Length > 0 && !row.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !row.ObjectId.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !row.Type.Contains(q, StringComparison.OrdinalIgnoreCase) && !row.Targeting.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Objects.Add(row);
        }
    }
}
