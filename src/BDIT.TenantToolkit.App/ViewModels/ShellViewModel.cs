using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Input;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private bool _isActive;
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Step { get; init; } = "";
    public string Group { get; init; } = "";
    public string GroupHeading { get; init; } = "";
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

    public ShellViewModel(Workspace workspace)
    {
        Workspace = workspace;
        AddNavigationGroup("Tenant", ("overview", "Overview and licences", ""), ("connect", "Connect", "1"),
            ("configuration", "Configuration", "2"));
        AddNavigationGroup("Build standard", ("standard", "Build Standard", ""), ("assessment", "Assessment", "3"),
            ("deviations", "Deviations", "4"), ("plan", "Plan changes", "5"), ("deploy", "Deploy", "6"),
            ("automation", "Policy automation", "+"), ("recovery", "Undo and recovery", ""),
            ("jobs", "Jobs and completion", ""), ("checks", "Manual checks", ""));
        AddNavigationGroup("Reports", ("reports", "Tenant reports", ""), ("naming", "Naming and ownership", ""),
            ("history", "Evidence and drift", ""));
        // Preserve the existing reviewed library page and its key; the new Graph pages have separate navigation.
        AddNavigationGroup("Scripts", ("scripts", "Scripts & Reports", ""));
        AddNavigationGroup("Settings", ("setup", "Application setup", "+"), ("settings", "Settings and diagnostics", ""));

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
        _pages["reports"] = new GraphReportsViewModel(this);
        _pages["naming"] = new NamingAuditViewModel(this);
        _pages["standard"] = new StandardViewModel(this);
        _pages["settings"] = new SettingsViewModel(this);

        NavigateCommand = new RelayCommand(p => { if (p is NavItem item) Navigate(item.Key); else if (p is string key) Navigate(key); });
        CopyCommand = new RelayCommand(p => CopyToClipboard(p as string ?? p?.ToString() ?? ""));
        CopyDetailsCommand = new RelayCommand(() => CopyToClipboard(DetailsText));
        DisconnectCommand = new AsyncCommand(Workspace.DisconnectAsync, ShowError, () => (Workspace.IsConnected || Workspace.ApplicationSetup is not null) && Workspace.Idle);
        ClearErrorCommand = new RelayCommand(() => ErrorMessage = "");
        CancelOperationCommand = new RelayCommand(Workspace.CancelOperation, () => Workspace.Busy);

        workspace.StateChanged += () => RaiseHeader();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Workspace.Busy) or nameof(Workspace.BusyMessage) or nameof(Workspace.ProgressDetail))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(BusyMessage));
                OnPropertyChanged(nameof(NextAction));
                OnPropertyChanged(nameof(ProgressDetail));
            }
        };
        if (workspace.StandardError is not null) ErrorMessage = workspace.StandardError;
        Navigate("overview");
    }

    public object? CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }

    private void AddNavigationGroup(string group, params (string Key, string Title, string Step)[] items)
    {
        for (var i = 0; i < items.Length; i++)
            NavItems.Add(new NavItem { Key = items[i].Key, Title = items[i].Title, Step = items[i].Step,
                Group = group, GroupHeading = i == 0 ? group : "" });
    }

    public void Navigate(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        foreach (var item in NavItems) item.IsActive = item.Key == key;
        CurrentPage = page;
        page.OnNavigatedTo();
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

    public string ProductName => Workspace.Settings.ProductName.ToUpperInvariant();
    public string WindowTitle => $"{Workspace.Settings.ProductName} {Workspace.Version}";
    public bool IsConnected => Workspace.IsConnected;
    public bool HeaderHasConnection => Workspace.IsConnected || Workspace.ApplicationSetup is not null;
    public bool HeaderHasDetails => HeaderHasConnection || Workspace.Profile is not null;
    public string HeaderAccount => Workspace.ApplicationSetup?.Identity.Account ?? Workspace.Session?.Account ?? "No active connection";
    public string HeaderTenantId => Workspace.ApplicationSetup?.Identity.TenantId ?? Workspace.Session?.TenantId ?? Workspace.Profile?.TenantId ?? "—";
    public bool IsDeploymentSession => Workspace.Session?.Mode == SessionMode.Deployment || Workspace.Session?.HasWriteScopes == true || Workspace.ApplicationSetup is not null;
    public bool IsAssessmentSession => Workspace.Session?.Mode == SessionMode.Assessment && Workspace.Session.HasWriteScopes == false;
    public string ModeBadge => Workspace.ApplicationSetup is not null ? "APPLICATION SETUP — WRITE ACCESS" : Workspace.Session is null ? "NOT CONNECTED" : Workspace.Session.Mode == SessionMode.Deployment ? (Workspace.ExperimentalChangesEnabled ? "WRITE ACCESS · EXPERIMENTAL ON" : "WRITE ACCESS · EXPERIMENTAL OFF") : Workspace.Session.HasWriteScopes ? "ASSESSMENT ONLY — TOKEN HAS WRITE SCOPES" : "READ-ONLY ASSESSMENT";
    public string EvidenceStatus
    {
        get
        {
            var capture = Workspace.Snapshot ?? Workspace.ExchangeSnapshot;
            if (capture is null) return Workspace.Profile is null ? "No client evidence selected" : "No full capture open · choose saved evidence or capture read-only";
            if (!CaptureMatchesCurrentTenant) return "Capture belongs to another tenant · reopen evidence for the current client";
            var live = Workspace.Snapshot is not null && Workspace.SnapshotIsLive;
            var source = Workspace.Snapshot is not null ? (live ? "Live full capture" : "Historical evidence · offline")
                : Workspace.ExchangeCapturedByTool ? "Supplemental Exchange capture" : "Historical Exchange evidence · offline";
            var at = DateTimeOffset.TryParse(capture.CapturedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var time) ? time.ToUniversalTime().ToString("dd MMM yyyy HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture) : capture.CapturedAt;
            return $"{source} · {at} · {(capture.Complete ? "complete" : "INCOMPLETE")}";
        }
    }
    private bool CaptureMatchesCurrentTenant => (Workspace.Snapshot ?? Workspace.ExchangeSnapshot) is { } capture
        && string.Equals(capture.TenantId, Workspace.Profile?.TenantId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(capture.TenantId, HeaderTenantId, StringComparison.OrdinalIgnoreCase);
    public string TenantTitle => Workspace.ApplicationSetup is not null ? "Application setup tenant" : Workspace.Session?.TenantName is { Length: > 0 } name ? name : Workspace.Profile?.Company ?? "No client selected";
    public string TenantSubtitle => Workspace.ApplicationSetup is { } setup ? setup.Identity.TenantId + " · setup identity verified" : Workspace.Session is not null
        ? (Workspace.Session.PrimaryDomain.Length > 0 ? Workspace.Session.PrimaryDomain : Workspace.Session.TenantId) + (Workspace.Session.TenantVerified ? " · verified" : " · NOT verified")
        : Workspace.Profile is { } profile ? (profile.Domain.Length > 0 ? profile.Domain + " · saved reference domain" : "Saved client · no reference domain")
        : "Choose or create a client on the Connect page";
    public string StandardText => Workspace.Standard is null ? "No Build Standard loaded" : $"Build Standard {Workspace.Standard.Release} · {Workspace.Standard.IntegrityDigest[..Math.Min(12, Workspace.Standard.IntegrityDigest.Length)]}";
    public string NextAction
    {
        get
        {
            if (Workspace.Busy) return "Operation in progress. Review its status before starting another action.";
            if (Workspace.Standard is null) return "Load a valid Build Standard in Settings.";
            if (Workspace.ApplicationSetup is not null) return "Finish application setup validation, then connect with assessment access.";
            if (Workspace.Profile is null) return "Choose a client in Connect, or export the engineer guide offline.";
            if (!Workspace.IsConnected) return "Open Configuration's saved captures, or review local reports, jobs and the change register. Connect and capture fresh evidence before planning changes.";
            if (Workspace.Snapshot is null) return "Capture this tenant in Configuration.";
            if (!Workspace.SnapshotIsLive) return "Stored evidence is for review. Capture again before planning a change.";
            if (!Workspace.Snapshot.Complete) return "Review failed collections and capture again. Incomplete evidence blocks writes.";
            if (Workspace.LastRun is { } run && run.BeforeSnapshotId == Workspace.Snapshot.Id) return run.Status == RunStatus.Completed
                ? "Review recorded outcomes and complete the separate functional checks."
                : "Review incomplete outcomes in Evidence and recovery. Reconcile uncertain writes before planning again.";
            if (Workspace.Assessment is null) return "Assess the capture, then inspect findings and prerequisites.";
            if (Workspace.Plan is null) return "Select eligible controls in Plan changes and review the exact proposal.";
            if (!Workspace.Plan.WriteRows.Any()) return "The plan has no writes. Review manual steps and blocked rows.";
            if (!Workspace.IsDeploymentSession) return "Deployment access requires a new capture and reviewed plan.";
            if (!Workspace.ExperimentalChangesEnabled) return Workspace.ExperimentalChangeGuidance;
            if (Workspace.AcknowledgedSnapshotId != Workspace.Snapshot.Id) return "Export and acknowledge before evidence in Deploy, then review confirmation.";
            return "Review the queued actions and explicitly approve the verified tenant and exact changes. Engine checks still apply.";
        }
    }
    public string VersionText => $"{Workspace.Settings.ProductName} {Workspace.Version} · Evidence: {Workspace.Paths.DataDirectory}";

    public string DetailsText
    {
        get
        {
            var s = Workspace.Session;
            if (s is null) return Workspace.ApplicationSetup is { } setup ? $"Privileged application setup session\nTenant: {setup.Identity.TenantId}\nAccount: {setup.Identity.Account}\nOperator: {setup.Identity.AccountObjectId}\nSetup scopes: {string.Join(", ", setup.Identity.Scopes)}"
                : Workspace.Profile is { } profile ? $"Local evidence review — no active connection\nClient: {profile.Company}\nSaved profile ID: {profile.Id}\nTenant: {profile.TenantId}\nReference domain: {profile.Domain}\nEvidence: {EvidenceStatus}\nCapture ID: {(CaptureMatchesCurrentTenant ? (Workspace.Snapshot ?? Workspace.ExchangeSnapshot)?.Id : "none for this client")}\nCaptured at: {(CaptureMatchesCurrentTenant ? (Workspace.Snapshot ?? Workspace.ExchangeSnapshot)?.CapturedAt : "none for this client")}\nA saved profile is not an authenticated connection. Historical evidence cannot authorise deployment."
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
        OnPropertyChanged(nameof(HeaderAccount));
        OnPropertyChanged(nameof(HeaderTenantId));
        OnPropertyChanged(nameof(IsDeploymentSession));
        OnPropertyChanged(nameof(IsAssessmentSession));
        OnPropertyChanged(nameof(ModeBadge));
        OnPropertyChanged(nameof(TenantTitle));
        OnPropertyChanged(nameof(TenantSubtitle));
        OnPropertyChanged(nameof(StandardText));
        OnPropertyChanged(nameof(NextAction));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(IsBusy));
        if (Workspace.StandardError is not null && ErrorMessage.Length == 0) ErrorMessage = Workspace.StandardError;
    }
}
