using BuildStandard.Core;
using System.Windows;
using BuildStandard.Core.Safety;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;

namespace BuildStandard.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private bool _isActive;
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Step { get; init; } = "";
    /// <summary>The menu heading this page sits under: Client, Build, Operate or Evidence and reference.</summary>
    public string Group { get; init; } = "";
    /// <summary>True for the first page of its group, where the menu shows the group heading.</summary>
    public bool IsGroupStart { get; init; }
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
}

/// <summary>Base for every page: shared access to the workspace and a consistent way to run commands and surface errors.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(ShellViewModel shell, string title)
    {
        Shell = shell;
        Title = title;
        shell.Workspace.StateChanged += Refresh;
    }

    public ShellViewModel Shell { get; }
    public Workspace Workspace => Shell.Workspace;
    public string Title { get; }

    /// <summary>Copies the command parameter (or the supplied text) to the clipboard. Used by every Copy button.</summary>
    protected RelayCommand CopyText(Func<string> text) => new(() => Shell.CopyToClipboard(text()));

    protected AsyncCommand Command(Func<Task> execute, Func<bool>? canExecute = null) => new(execute, Shell.ShowError, canExecute);
    protected AsyncCommand Command<T>(Func<T?, Task> execute, Func<bool>? canExecute = null) => new(p => execute(p is T t ? t : default), Shell.ShowError, canExecute is null ? null : _ => canExecute());
    protected RelayCommand Sync(Action execute, Func<bool>? canExecute = null) => new(() => { try { execute(); } catch (Exception ex) { Shell.ShowError(ex); } }, canExecute);

    /// <summary>Called whenever workspace state changes. Implementations must be cheap and idempotent.</summary>
    public abstract void Refresh();

    public virtual void OnNavigatedTo() => Refresh();
}

public sealed class ShellViewModel : ObservableObject
{
    private string _activeGroup = "Workspace";
    private double _viewportWidth = 1480;
    public PinnedReferenceViewModel PinnedReference { get; } = new();
    public IEnumerable<NativeNavigationGroup> NavigationGroups => RouteCatalog.Groups.Select(g => new NativeNavigationGroup(g, g == _activeGroup));
    private bool _updatingNavigation;
    public ObservableCollection<NavItem> VisibleNavItems { get; } = new();
    public string ActiveRouteKey
    {
        get => ActiveNavItem?.Key ?? "";
        set { if (!_updatingNavigation && !string.IsNullOrEmpty(value) && value != ActiveRouteKey) Navigate(value); }
    }
    public NavItem? ActiveNavItem
    {
        get => NavItems.FirstOrDefault(n => n.IsActive);
        set { if (!_updatingNavigation && value is not null && !value.IsActive) Navigate(value.Key); }
    }
    public string IdentityContextLabel => Workspace.ApplicationSetup is not null ? "Privileged application setup context"
        : Workspace.Session is null ? "No signed-in tenant session"
        : Workspace.Session.OperatorVerified ? "Verified Microsoft Graph identity" : "Identity verification required";
    public string CurrentPageTitle => ActiveNavItem?.Title ?? "Command centre";
    public bool HasPinnedReference => PinnedReference.HasReference;
    public bool UseSideReference => HasPinnedReference && _viewportWidth >= 1360;
    public bool UseDrawerReference => HasPinnedReference && !UseSideReference;
    public GridLength PinnedColumnWidth => new(UseSideReference ? 300 : 0);
    public ICommand SelectGroupCommand { get; private set; } = null!;
    public ICommand PinCurrentReferenceCommand { get; private set; } = null!;
    public ICommand ClosePinnedReferenceCommand { get; private set; } = null!;
    public ICommand CopyPinnedReferenceCommand { get; private set; } = null!;
    public string PinUnavailableReason => CanPinCurrentReference ? "Pin an immutable copy of this saved capture’s provenance." : "Open or capture this client’s saved configuration first. Wait for any active operation.";
    private bool CanPinCurrentReference => Workspace.Idle && Workspace.Profile is not null && Workspace.Snapshot is not null
        && string.Equals(Workspace.Profile.TenantId, Workspace.Snapshot.TenantId, StringComparison.OrdinalIgnoreCase);
    public void SetViewportWidth(double width) { _viewportWidth = width; NotifyReferenceLayout(); }
    private void NotifyReferenceLayout()
    {
        OnPropertyChanged(nameof(HasPinnedReference)); OnPropertyChanged(nameof(UseSideReference));
        OnPropertyChanged(nameof(UseDrawerReference)); OnPropertyChanged(nameof(PinnedColumnWidth));
        OnPropertyChanged(nameof(PinUnavailableReason));
    }
    private void SelectGroup(string group)
    {
        var first = NavItems.FirstOrDefault(n => n.Group == group);
        if (first is not null && group != _activeGroup) Navigate(first.Key);
    }
    private void PinCurrentReference()
    {
        if (!CanPinCurrentReference) { ErrorMessage = PinUnavailableReason; return; }
        try
        {
            var current = Workspace.Snapshot!;
            var saved = Workspace.Evidence.LoadSnapshot(current.TenantId, current.Id)
                ?? throw new ToolkitException("This capture has no readable saved evidence. Preserve the original record and review History.");
            PinnedReference.Pin(PinnedReferenceRecord.FromSnapshot(saved, Workspace.CapturePresentationContext()));
            PinnedReference.UpdateContext(Workspace.CapturePresentationContext(), Workspace.Standard?.Release);
        }
        catch (Exception ex) when (ex is ToolkitException or System.IO.IOException or UnauthorizedAccessException) { ErrorMessage = ex.Message; }
    }

    private readonly Dictionary<string, PageViewModel> _pages = new(StringComparer.Ordinal);
    private object? _currentPage;
    private string _errorMessage = "";

    public Workspace Workspace { get; }
    public ObservableCollection<NavItem> NavItems { get; } = new();
    public ICommand NavigateCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ClearErrorCommand { get; }
    /// <summary>Copies its command parameter to the clipboard; bound from Copy buttons across the pages.</summary>
    public ICommand CopyCommand { get; }
    public ICommand CopyDetailsCommand { get; }
    public ICommand CancelOperationCommand { get; }
    /// <summary>Opens the page where the current next step is done.</summary>
    public ICommand NextStepCommand { get; }

    public ShellViewModel(Workspace workspace)
    {
        Workspace = workspace;
        SelectGroupCommand = new RelayCommand(value => { if (value is string group) SelectGroup(group); });
        PinCurrentReferenceCommand = new RelayCommand(PinCurrentReference, () => CanPinCurrentReference);
        ClosePinnedReferenceCommand = new RelayCommand(PinnedReference.Close, () => HasPinnedReference);
        CopyPinnedReferenceCommand = new RelayCommand(() => CopyToClipboard(PinnedReference.OriginalDetails), () => HasPinnedReference);
        PinnedReference.PropertyChanged += (_, _) => NotifyReferenceLayout();
        Workspace.StateChanged += () => PinnedReference.UpdateContext(Workspace.CapturePresentationContext(), Workspace.Standard?.Release);

        foreach (var route in RouteCatalog.Items) AddNav(route.Group, route.Key, route.Step, route.Title);

        _pages["home"] = new HomeViewModel(this);
        _pages["overview"] = new OverviewViewModel(this);
        _pages["recovery"] = new RecoveryViewModel(this);
        _pages["connect"] = new ConnectViewModel(this);
        _pages["setup"] = new ApplicationSetupViewModel(this);
        _pages["automation"] = new AutomationViewModel(this);
        _pages["configuration"] = new ConfigurationViewModel(this);
        _pages["assessment"] = new AssessmentViewModel(this);
        _pages["deviations"] = new DeviationsViewModel(this);
        _pages["plan"] = new PlanViewModel(this);
        _pages["deploy"] = new DeployViewModel(this);
        _pages["history"] = new HistoryViewModel(this);
        _pages["jobs"] = new JobsViewModel(this);
        _pages["checks"] = new ManualChecksViewModel(this);
        _pages["scripts"] = new ScriptsViewModel(this);
        _pages["run-reports"] = new RunReportsViewModel(this);
        _pages["reports"] = new GraphReportsViewModel(this);
        _pages["clients"] = new ClientReviewsViewModel(this);
        _pages["mailbox-capacity"] = new MailboxCapacityViewModel(this);
        _pages["naming"] = new NamingAuditViewModel(this);
        _pages["standard"] = new StandardViewModel(this);
        _pages["settings"] = new SettingsViewModel(this);

        NavigateCommand = new RelayCommand(p => { if (p is NavItem item) Navigate(item.Key); else if (p is string key) Navigate(key); });
        CopyCommand = new RelayCommand(p => CopyToClipboard(p as string ?? p?.ToString() ?? ""));
        CopyDetailsCommand = new RelayCommand(() => CopyToClipboard(DetailsText));
        DisconnectCommand = new AsyncCommand(Workspace.DisconnectAsync, ShowError, () => (Workspace.IsConnected || Workspace.ApplicationSetup is not null) && Workspace.Idle);
        ClearErrorCommand = new RelayCommand(() => ErrorMessage = "");
        NextStepCommand = new RelayCommand(() => Navigate(NextActionKey), () => NextActionKey.Length > 0);
        CancelOperationCommand = new RelayCommand(Workspace.CancelOperation, () => Workspace.Busy);

        workspace.StateChanged += () => RaiseHeader();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Workspace.Busy) or nameof(Workspace.BusyMessage) or nameof(Workspace.ProgressDetail))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(BusyMessage));
                OnPropertyChanged(nameof(NextAction));
                OnPropertyChanged(nameof(NextActionKey));
                OnPropertyChanged(nameof(ProgressDetail));
            }
        };
        if (workspace.StandardError is not null) ErrorMessage = workspace.StandardError;
        Navigate("home");
    }

    private void AddNav(string group, string key, string step, string title) =>
        NavItems.Add(new NavItem { Group = group, Key = key, Step = step, Title = title, IsGroupStart = NavItems.LastOrDefault()?.Group != group });

    public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    public long NavigationEpoch { get; private set; }
    public async Task ShowCatalogueReferenceAsync(BuildStandard.Core.Models.CatalogueDocument document, string controlId,
        BuildStandard.App.Services.PresentationContextKey context, long? expectedNavigation = null)
    {
        if (expectedNavigation is { } expected && expected != NavigationEpoch) return;
        NavigationEpoch++;
        if (CurrentPage is CatalogueReaderViewModel previous) previous.Close();
        var reader = new CatalogueReaderViewModel(this);
        try { await reader.OpenAsync(document, controlId, context, CancellationToken.None); CurrentPage = reader; }
        catch { reader.Close(); throw; }
    }
    public void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        NavigationEpoch++;
        if (CurrentPage is CatalogueReaderViewModel reader)
        {
            var standard = Page<StandardViewModel>();
            if (!standard.ReferenceReturnRequested && standard.ReferenceReturnTicket is { } ticket) standard.FinishReferenceReturn(ticket);
            reader.Close();
        }
        _updatingNavigation = true;
        try
        {
            foreach (var item in NavItems) item.IsActive = item.Key == key;
            _activeGroup = NavItems.Single(n => n.Key == key).Group;
            if (!VisibleNavItems.Any() || VisibleNavItems[0].Group != _activeGroup)
            {
                VisibleNavItems.Clear();
                foreach (var item in NavItems.Where(n => n.Group == _activeGroup)) VisibleNavItems.Add(item);
            }
            OnPropertyChanged(nameof(NavigationGroups)); OnPropertyChanged(nameof(ActiveNavItem));
            OnPropertyChanged(nameof(ActiveRouteKey)); OnPropertyChanged(nameof(CurrentPageTitle));
        }
        finally { _updatingNavigation = false; }
        if (key == "deploy") Workspace.MarkRunViewed();
        CurrentPage = page;
        page.OnNavigatedTo();
        OnPropertyChanged(nameof(NextAction));
        OnPropertyChanged(nameof(NextActionKey));
    }

    public T Page<T>() where T : PageViewModel => _pages.Values.OfType<T>().First();

    public ObservableCollection<LogEntry> Activity => Workspace.Activity;

    public string ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>
    /// Clipboard writes can fail transiently when another process holds the clipboard; that must never take the window
    /// down or raise an error bar over a convenience action.
    /// </summary>
    public void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            System.Windows.Clipboard.SetText(text);
            Workspace.Logger.Debug("UI", $"Copied {text.Length} character(s) to the clipboard.");
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Workspace.Logger.Warn("UI", "The clipboard was unavailable: " + ex.Message);
        }
    }

    public void ShowError(Exception ex)
    {
        ErrorMessage = SensitiveDataScrubber.Scrub(ex.Message);
        Workspace.Logger.Error("UI", ex.Message, ex, Workspace.Profile?.TenantId);
    }

    public bool IsBusy => Workspace.Busy;
    public string BusyMessage => Workspace.BusyMessage;
    public string ProgressDetail => Workspace.ProgressDetail;

    public string ProductName => Workspace.Settings.ProductName;
    public string WindowTitle => $"{Workspace.Settings.ProductName} {Workspace.Version}";
    public bool IsConnected => Workspace.IsConnected;
    public bool HeaderHasConnection => Workspace.IsConnected || Workspace.ApplicationSetup is not null;
    public bool HeaderHasDetails => HeaderHasConnection || Workspace.Profile is not null;
    public string HeaderAccount => Workspace.ApplicationSetup?.Identity.Account ?? Workspace.Session?.Account ?? "No active connection";
    public string HeaderTenantId => Workspace.ApplicationSetup?.Identity.TenantId ?? Workspace.Session?.TenantId ?? Workspace.Profile?.TenantId ?? "—";
    public bool IsDeploymentSession => Workspace.Session?.Mode == SessionMode.Deployment || Workspace.Session is { } s && s.TokenCapabilities.Classification != TokenCapability.ReadOnlyVerified || Workspace.ApplicationSetup is not null;
    public bool IsAssessmentSession => Workspace.Session?.Mode == SessionMode.Assessment && Workspace.Session.TokenCapabilities.Classification == TokenCapability.ReadOnlyVerified;
    public string ModeBadge => Workspace.ApplicationSetup is not null ? "APPLICATION SETUP — WRITE ACCESS"
        : Workspace.Session is not { } session ? "No live connection"
        : (session.Mode == SessionMode.Deployment ? (Workspace.ExperimentalChangesEnabled ? "Deployment mode · tenant changes enabled" : "Deployment mode · tenant changes off")
            : "Assessment mode — toolkit changes blocked") + "\n" + session.TokenCapabilities.Description;
    public EvidenceSourcePresentation EvidenceSource => EvidenceSourcePresentation.Create(Workspace.Snapshot,
        Workspace.SnapshotIsLive, Workspace.ExchangeSnapshot, Workspace.ExchangeCapturedByTool, Workspace.Profile?.TenantId, HeaderTenantId);
    public string EvidenceStatus => EvidenceSource.Summary;
    private bool CaptureMatchesCurrentTenant => (Workspace.Snapshot ?? Workspace.ExchangeSnapshot) is { } capture
        && string.Equals(capture.TenantId, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(capture.TenantId, HeaderTenantId, StringComparison.OrdinalIgnoreCase);
    public string TenantTitle => Workspace.ApplicationSetup is not null ? "Application setup tenant" : Workspace.Session?.TenantName is { Length: > 0 } name ? name : Workspace.Profile?.Company ?? "No client selected";
    public string TenantSubtitle => Workspace.ApplicationSetup is { } setup ? setup.Identity.TenantId + " · setup identity verified" : Workspace.Session is not null
        ? (Workspace.Session.PrimaryDomain.Length > 0 ? Workspace.Session.PrimaryDomain : Workspace.Session.TenantId) + (Workspace.Session.TenantVerified ? " · verified" : " · NOT verified")
        : Workspace.Profile is { } profile ? (profile.Domain.Length > 0 ? profile.Domain + " · saved reference domain" : "Saved client · no reference domain")
        : "Choose or create a client on the Connect page";
    public string StandardText => Workspace.Standard is null ? "No Build Standard loaded" : $"Build Standard {Workspace.Standard.Release} · {Workspace.Standard.IntegrityDigest[..Math.Min(12, Workspace.Standard.IntegrityDigest.Length)]}";
    public string NextAction => NextStep().Text;
    /// <summary>The page the next step is done on; empty while an operation runs.</summary>
    public string NextActionKey => NextStep().Key;

    private (string Text, string Key) NextStep()
    {
        var guidance = HomeTaskProjection.Create(Workspace.CaptureHomeTaskContext());
        return (guidance.Guidance, guidance.NextPageKey);
    }

    public string VersionText => $"{Workspace.Settings.ProductName} {Workspace.Version} · Evidence: {Workspace.Paths.DataDirectory}";

    public string DetailsText
    {
        get
        {
            var s = Workspace.Session;
            if (s is null) return Workspace.ApplicationSetup is { } setup ? $"Privileged application setup session\nTenant: {setup.Identity.TenantId}\nAccount: {setup.Identity.Account}\nOperator: {setup.Identity.AccountObjectId}\nSetup scopes: {string.Join(", ", setup.Identity.Scopes)}"
                : Workspace.Profile is { } profile ? $"Local evidence review — no active connection\nClient: {profile.Company}\nSaved profile ID: {profile.Id}\nTenant: {profile.TenantId}\nReference domain: {profile.Domain}\nEvidence: {EvidenceStatus}\nCapture ID: {(CaptureMatchesCurrentTenant ? (Workspace.Snapshot ?? Workspace.ExchangeSnapshot)?.Id : "none for this client")}\nCaptured at: {(CaptureMatchesCurrentTenant ? (Workspace.Snapshot ?? Workspace.ExchangeSnapshot)?.CapturedAt : "none for this client")}\n{EvidenceSource.Details}\nA saved profile is not an authenticated connection. Historical evidence cannot authorise deployment."
                : "Not connected. A saved profile is not an authenticated connection.";
            var sb = new StringBuilder();
            sb.AppendLine("Tenant ID:      " + s.TenantId);
            sb.AppendLine("Account:        " + s.Account);
            sb.AppendLine("Operator ID:    " + (s.OperatorObjectId ?? "not resolved") + (s.OperatorVerified ? " (verified)" : " (NOT verified)"));
            sb.AppendLine("Application:    " + s.ClientLabel);
            sb.AppendLine("Client ID:      " + s.ClientId);
            sb.AppendLine("Authentication: " + s.AuthenticationType);
            sb.AppendLine("Mode:           " + s.Mode);
            sb.AppendLine("Scopes:         " + string.Join(", ", s.Scopes));
            sb.AppendLine("Connected:      " + s.ConnectedAt);
            sb.AppendLine("Evidence:       " + EvidenceStatus);
            if (EvidenceSource.Details.Length > 0) sb.AppendLine(EvidenceSource.Details);
            foreach (var n in s.Notices) sb.AppendLine("Notice:         " + n);
            return sb.ToString().TrimEnd();
        }
    }

    private void RaiseHeader()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HeaderHasConnection));
        OnPropertyChanged(nameof(HeaderHasDetails));
        OnPropertyChanged(nameof(EvidenceStatus));
        OnPropertyChanged(nameof(EvidenceSource));
        OnPropertyChanged(nameof(HeaderAccount));
        OnPropertyChanged(nameof(HeaderTenantId));
        OnPropertyChanged(nameof(IsDeploymentSession));
        OnPropertyChanged(nameof(IsAssessmentSession));
        OnPropertyChanged(nameof(ModeBadge));
        OnPropertyChanged(nameof(TenantTitle));
        OnPropertyChanged(nameof(TenantSubtitle));
        OnPropertyChanged(nameof(StandardText));
        OnPropertyChanged(nameof(NextAction));
        OnPropertyChanged(nameof(NextActionKey));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(IsBusy));
        if (Workspace.StandardError is not null && ErrorMessage.Length == 0) ErrorMessage = Workspace.StandardError;
    }
}
