using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Engine.Drift;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Exchange;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Recovery;
using BDIT.TenantToolkit.Engine.Standards;
using BDIT.TenantToolkit.Engine.Workflow;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Setup;

namespace BDIT.TenantToolkit.App.Services;

/// <summary>
/// Composition root and single source of truth for the UI. Every tenant operation runs through <see cref="RunExclusiveAsync"/>
/// so only one operation touches the connection at a time, and every state change raises <see cref="StateChanged"/>.
/// The workspace mirrors the engineer workflow: connect, capture, assess, plan, acknowledge, deploy, review evidence.
/// </summary>
public sealed class Workspace : ObservableObject
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private PendingTenantDiscovery? _pendingDiscovery;
    private Task _pendingRelease = Task.CompletedTask;
    private bool _busy;
    private string _busyMessage = "";
    private string _progressDetail = "";
    private CancellationTokenSource? _operationCancellation;
    private TaskCompletionSource? _operationCompletion;
    public CancellationToken OperationToken => _operationCancellation?.Token ?? CancellationToken.None;
    public bool ShutdownComplete { get; private set; }

    public ToolkitPaths Paths { get; }
    public ToolkitSettings Settings { get; }
    public ToolkitLogger Logger { get; }
    public bool Diagnostics { get; }
    public string Version => ToolkitVersion.Current;

    public EvidenceStore Evidence { get; }
    public StandardsLoader Standards { get; }
    public TenantConnectionService Connections { get; }
    public TenantCollector Collector { get; }
    public AssessmentEngine Engine { get; }
    public DeploymentPlanner Planner { get; }
    public DeploymentExecutor Executor { get; }
    public DriftAnalyser Drift { get; }
    public ReportExporter Exporter { get; }
    public RecoveryService Recovery { get; }
    public LicenceInventory? Licences { get; private set; }
    public string InterruptedNotice { get; private set; } = "";
    public string StopGuidance => $"Stop cancels policy reads. An in-flight policy request waits for its {Settings.GraphWriteTimeoutSeconds}-second transport budget. Verification and after-capture each have a 60-second budget and may finish incomplete. Local evidence is saved before closing.";

    public ObservableCollection<TenantProfile> Profiles { get; } = new();
    public ObservableCollection<StandardRelease> Releases { get; } = new();
    public ObservableCollection<LogEntry> Activity { get; } = new();

    public StandardCatalogue? Standard { get; private set; }
    public string? StandardError { get; private set; }
    public TenantProfile? Profile { get; private set; }
    public ConnectedTenant? Connection { get; private set; }
    public ApplicationSetupService? ApplicationSetup { get; private set; }
    public TenantSession? Session => Connection?.Session;
    public AccessReport? Access { get; private set; }
    public TenantSnapshot? Snapshot { get; private set; }
    public bool SnapshotIsLive { get; private set; }
    public TenantSnapshot? ExchangeSnapshot { get; private set; }
    public bool ExchangeCapturedByTool { get; private set; }
    /// <summary>True while ExchangeSnapshot was reopened from history rather than captured, imported or refreshed in this session.</summary>
    private bool _exchangeFromHistory;
    public AssessmentResult? Assessment { get; private set; }
    public DeploymentPlan? Plan { get; private set; }
    public string? AcknowledgedSnapshotId { get; private set; }
    public DeploymentRun? LastRun { get; private set; }
    public DeploymentControl? Control { get; private set; }

    public bool Busy { get => _busy; private set { if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(Idle)); OnPropertyChanged(nameof(CanEnableExperimentalChanges)); } }
    public bool Idle => !_busy;
    public string BusyMessage { get => _busyMessage; private set => SetProperty(ref _busyMessage, value); }
    public string ProgressDetail { get => _progressDetail; private set => SetProperty(ref _progressDetail, value); }

    public bool IsConnected => Connection is not null;
    public bool IsDeploymentSession => Session?.Mode == SessionMode.Deployment;
    public ExperimentalOperationGuard ExperimentalGuard { get; } = new();
    public bool ExperimentalChangesEnabled
    {
        get
        {
            if (!string.Equals(Settings.ResolveClient(SessionMode.Deployment, Profile)?.ClientId, Session?.ClientId, StringComparison.OrdinalIgnoreCase))
                ExperimentalGuard.Invalidate();
            return ExperimentalGuard.IsEnabled(Connection, Session, Standard, Profile);
        }
    }
    public bool CanEnableExperimentalChanges => Idle && ExperimentalGuard.CanEnable(Session, Standard, Profile)
        && string.Equals(Settings.ResolveClient(SessionMode.Deployment, Profile)?.ClientId, Session?.ClientId, StringComparison.OrdinalIgnoreCase);
    public string ExperimentalChangeGuidance => ExperimentalChangesEnabled
        ? "Experimental changes enabled for this verified session only. Review and approve every exact operation. Live service behaviour is unverified."
        : CanEnableExperimentalChanges ? ExperimentalOperationGuard.ClosedReason
            : "Experimental changes need a current verified write-capable connection. Connect for deployment, or reconnect after an identity/permission/expiry problem. Read-only checks and local previews remain available.";
    public string ExperimentalContextText => Session is null ? "No verified deployment connection. Connect read-only first, then deliberately switch to deployment access."
        : $"Tenant: {Session.TenantName} ({Session.TenantId})\nAccount: {Session.Account} ({Session.AccountObjectId})\nApplication: {Session.ClientLabel} ({Session.ClientId})\nMode: {Session.Mode} · Microsoft Graph\nActual returned scopes: {string.Join(", ", Session.Scopes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}\nToken expires: {Session.TokenExpiresAt}\nOpt-in is temporary, is never exported, and does not establish production acceptance.";
    public void SetExperimentalChanges(bool approved)
    {
        if (!Idle) throw new ToolkitException("Wait for the active operation before changing experimental access.");
        if (approved)
        {
            if (!CanEnableExperimentalChanges) throw new SafetyViolationException("Connect with current verified deployment access before enabling experimental changes.");
            ExperimentalGuard.Enable(RequireConnection(), Session!, RequireStandard(), Profile!, true);
        }
        else ExperimentalGuard.Invalidate();
        Notify();
    }
    public void RequireExperimentalChanges(ExperimentalOperation operation)
    {
        if (!ExperimentalChangesEnabled) { ExperimentalGuard.Invalidate(); throw new SafetyViolationException(ExperimentalOperationGuard.ClosedReason); }
        ExperimentalGuard.Require(operation, Connection, Session, Standard, Profile);
    }
    public void InvalidateExperimentalChanges() { ExperimentalGuard.Invalidate(); Notify(); }

    public async Task ReviewPermissionRequestAsync(string tenant, string clientId, IReadOnlyList<string> scopes, string purpose, CancellationToken ct, string? knownAccount = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!ProfileValidator.IsGuid(clientId) || scopes.Count == 0 || scopes.Any(string.IsNullOrWhiteSpace))
            throw new SafetyViolationException("An exact application ID and non-empty permission list are required before an access request can be approved.");
        var app = System.Windows.Application.Current;
        if (app is null) throw new SafetyViolationException("This Microsoft access request requires visible, deliberate desktop approval. No request was sent.");
        var details = PermissionRequestDetails(tenant, clientId, scopes, purpose, knownAccount);
        var confirmed = await app.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new BDIT.TenantToolkit.App.Views.PermissionRequestDialog(details) { Owner = app.MainWindow };
            return dialog.ShowForReview(ct);
        });
        ct.ThrowIfCancellationRequested();
        if (!confirmed)
        { CancelOperation(); throw new OperationCanceledException("The displayed Microsoft access request was not approved. No request was sent.", ct); }
    }

    private static string PermissionRequestDetails(string tenant, string clientId, IReadOnlyList<string> scopes, string purpose, string? knownAccount) =>
        $"Purpose: {purpose}\nTarget tenant: {tenant} (target of this exact request)\nApplication (client) ID: {clientId}\nResource: Microsoft Graph — https://graph.microsoft.com\nAccount context: {(string.IsNullOrWhiteSpace(knownAccount) ? "Not known; Microsoft will ask you to choose" : knownAccount)}\nInteractive identity is verified after sign-in. Consent grants are checked separately; browser success is not verification.\nMSAL also uses the standard sign-in protocol scopes openid, profile and offline_access; these are separate from Graph API permissions.\n\nExact requested permissions:\n" + string.Join("\n", scopes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

    private void OnAuthorisationInvalidated()
    {
        ExperimentalGuard.Invalidate();
        if (Session is { } session) session.OperatorVerified = false;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Notify);
    }



    public event Action? StateChanged;

    public Workspace(ToolkitPaths paths, ToolkitSettings settings, ToolkitLogger logger, bool diagnostics)
    {
        Paths = paths;
        Settings = settings;
        Logger = logger;
        Diagnostics = diagnostics;
        Evidence = new EvidenceStore(paths, logger);
        Recovery = new RecoveryService(Evidence, SystemClock.Instance);
        Standards = new StandardsLoader(paths, logger);
        Connections = new TenantConnectionService(settings, paths, _http, logger, ToolkitVersion.Current);
        Collector = new TenantCollector(logger, SystemClock.Instance, ToolkitVersion.Current);
        Engine = new AssessmentEngine(SystemClock.Instance, ToolkitVersion.Current);
        Planner = new DeploymentPlanner(SystemClock.Instance, ToolkitVersion.Current);
        Executor = new DeploymentExecutor(Evidence, Collector, logger, SystemClock.Instance, ToolkitVersion.Current);
        Drift = new DriftAnalyser(SystemClock.Instance, Engine);
        Exporter = new ReportExporter(paths, settings.CompanyName);
        logger.EntryWritten += OnLogEntry;
    }

    private void OnLogEntry(LogEntry entry)
    {
        var app = Application.Current;
        if (app is null) return;
        app.Dispatcher.BeginInvoke(() =>
        {
            Activity.Add(entry);
            while (Activity.Count > 500) Activity.RemoveAt(0);
        });
    }

    public void Initialise()
    {
        LoadProfiles();
        foreach (var r in Standards.ListReleases()) Releases.Add(r);
        var preferred = Releases.FirstOrDefault(r => string.Equals(r.Release, Settings.DefaultStandardRelease, StringComparison.OrdinalIgnoreCase)) ?? Releases.FirstOrDefault();
        if (preferred is not null) TrySelectStandard(preferred.FileName);
        Notify();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var p in Evidence.LoadProfiles()) Profiles.Add(p);
        foreach (var p in Profiles)
        {
            try { Evidence.MarkInterruptedRuns(p.TenantId); }
            catch (ToolkitException ex) { Logger.Warn("App", $"Could not review previous runs for {p.Company}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Loads clients adopted from a backup archive. Adoption needs an empty workspace, so nothing is selected or
    /// connected; reloading at once stops a new client saved before a restart from overwriting the adopted profiles.
    /// </summary>
    public void ReloadAdoptedEvidence()
    {
        if (Profile is not null || IsConnected || !Idle) throw new ToolkitException("Adopted evidence can only be loaded into an idle, disconnected workspace with no client selected.");
        LoadProfiles();
        Notify();
    }

    public bool TrySelectStandard(string fileName)
    {
        try
        {
            CancelPendingDiscovery();
            Standard = Standards.Load(fileName);
            StandardError = null;
            Plan = null;
            AcknowledgedSnapshotId = null;
            Assessment = null;
            Notify();
            return true;
        }
        catch (ToolkitException ex)
        {
            Standard = null;
            StandardError = ex.Message;
            Logger.Error("Standards", ex.Message, ex);
            Notify();
            return false;
        }
    }

    /// <summary>
    /// The engineer choosing another release. Like loading an imported or local candidate, this is refused while
    /// connected: the connection requested routes and permissions for the release it was made with, and a capture
    /// taken for one release is not evidence for another. Whatever was captured or assessed is set aside.
    /// </summary>
    public void SelectRelease(string fileName)
    {
        if (Busy || IsConnected) throw new ToolkitException("Disconnect before changing the Build Standard release, so the next connection requests the routes and permissions that release needs.");
        if (!TrySelectStandard(fileName)) throw new ConfigurationException(StandardError ?? "The Build Standard release could not be loaded.");
        InvalidatePolicyState();
    }

    public StandardCatalogue RequireStandard() =>
        Standard ?? throw new ConfigurationException(StandardError ?? "No Build Standard release is loaded. Check the standards folder and manifest.");

    private void Notify()
    {
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(ApplicationSetup));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDeploymentSession));
        OnPropertyChanged(nameof(ExperimentalChangesEnabled));
        OnPropertyChanged(nameof(ExperimentalChangeGuidance));
        OnPropertyChanged(nameof(CanEnableExperimentalChanges));
        OnPropertyChanged(nameof(ExperimentalContextText));
        StateChanged?.Invoke();
    }

    // ---- exclusive operations --------------------------------------------------------------------------------------

    public async Task RunExclusiveAsync(string message, Func<IProgress<string>, Task> operation)
    {
        if (Busy) throw new ToolkitException("Another operation is already running. Wait for it to finish.");
        Busy = true;
        _operationCancellation = new CancellationTokenSource();
        _operationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BusyMessage = message;
        ProgressDetail = "";
        var progress = new Progress<string>(p => ProgressDetail = p);
        try { await operation(progress); }
        catch (OperationCanceledException) when (_operationCancellation.IsCancellationRequested)
        { Logger.Info("App", "Operation cancelled. Review any captured evidence before continuing."); }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _operationCompletion.TrySetResult();
            Busy = false;
            BusyMessage = "";
            ProgressDetail = "";
            Notify();
        }
    }

    public void InvalidatePolicyState()
    {
        Plan = null; AcknowledgedSnapshotId = null; SnapshotIsLive = false; Assessment = null;
        Notify();
    }

    public void UseImportedStandard(DevicePolicyImport import)
    {
        if (Busy || IsConnected) throw new ToolkitException("Disconnect before changing the standard so the next connection requests the correct routes and permissions.");
        var tenant = Profile?.TenantId ?? throw new ToolkitException("Select the target tenant profile first.");
        var directory = Path.Combine(Paths.TenantDirectory(tenant), "catalogues");
        var text = ToolkitJson.Serialize(import.Standard);
        var file = Path.Combine(directory, import.Standard.Release + ".json");
        Evidence.WriteJsonAtomic(file, import.Standard);
        Evidence.WriteJsonAtomic(file + ".integrity.json", new { sha256 = CanonicalJson.Sha256Hex(text), import.SourceDigest, import.RemovedProperties });
        import.Standard.IntegrityDigest = CanonicalJson.Sha256Hex(text);
        Standard = import.Standard; Standard.SourceFileName = Path.GetFileName(file);
        StandardError = null; InvalidatePolicyState();
    }

    public IReadOnlyList<string> LocalCandidates()
    {
        if (Profile is null) return Array.Empty<string>();
        var directory = Path.Combine(Paths.TenantDirectory(Profile.TenantId), "catalogues");
        return Directory.Exists(directory) ? Directory.GetFiles(directory, "import-*.json").Where(f => !f.EndsWith(".integrity.json", StringComparison.Ordinal)).Select(Path.GetFileName).OfType<string>().Order().ToList() : Array.Empty<string>();
    }

    public void LoadLocalCandidate(string fileName)
    {
        if (Busy || IsConnected) throw new ToolkitException("Disconnect before loading another candidate standard.");
        if (!LocalCandidates().Contains(fileName, StringComparer.Ordinal)) throw new ConfigurationException("Select a saved candidate from this tenant.");
        var file = Path.Combine(Paths.TenantDirectory(Profile!.TenantId), "catalogues", fileName);
        var text = File.ReadAllText(file);
        var integrity = ToolkitJson.ParseObject(File.ReadAllText(file + ".integrity.json"));
        var digest = CanonicalJson.Sha256Hex(text);
        if (integrity["sha256"]?.ToString() != digest) throw new IntegrityException("Local candidate does not match its recorded digest.");
        Standard = StandardsLoader.Parse(text, fileName); Standard.IntegrityDigest = digest; StandardError = null;
        InvalidatePolicyState();
    }

    public void CancelOperation()
    {
        if (Executor.IsRunning) Control?.Stop();
        else _operationCancellation?.Cancel();
        ProgressDetail = StopGuidance;
    }

    public Task<string> ExportAsync(Func<string> export) =>
        ExportAsync(export, "Writing report", "Creating the report. Closing waits for the file to finish.");

    /// <summary>Runs a local file task exclusively, under a title and progress text that say what is happening.</summary>
    public async Task<string> ExportAsync(Func<string> work, string title, string progressText)
    {
        var result = "";
        await RunExclusiveAsync(title, async progress =>
        {
            progress.Report(progressText);
            result = await Task.Run(work);
        });
        return result;
    }

    public void ApplyProfileToSession(TenantProfile input, bool save)
    {
        var profile = ProfileValidator.Validate(input, DateTimeOffset.UtcNow);
        if (Connection is not null && !string.Equals(Connection.Session.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("These settings belong to another tenant. Reconnect before applying them.");
        if (save) profile = SaveProfile(profile);
        CancelPendingDiscovery();
        if (Profile?.TenantId != profile.TenantId) { ExchangeSnapshot = null; ExchangeCapturedByTool = false; _exchangeFromHistory = false; }
        Profile = profile;
        Plan = null;
        AcknowledgedSnapshotId = null;
        Assessment = null;
        Notify();
    }

    // ---- profiles ------------------------------------------------------------------------------------------------

    public TenantProfile SaveProfile(TenantProfile input)
    {
        if (Busy) throw new ToolkitException("Wait for the active operation before changing a saved profile.");
        var profile = ProfileValidator.Validate(input, DateTimeOffset.UtcNow);
        if (Connection is not null && Profile?.Id == profile.Id
            && !string.Equals(Connection.Session.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("Disconnect before changing the tenant ID of the connected profile.");
        var all = Profiles.ToList();
        // One saved client per tenant: evidence, managed-object mappings and deviations are all keyed by tenant ID, so a
        // second profile for the same tenant would silently split a client's history in two.
        var sameTenant = all.FirstOrDefault(p => string.Equals(p.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase) && p.Id != profile.Id);
        if (sameTenant is not null)
            throw new ConfigurationException($"'{sameTenant.Company}' is already saved for tenant {profile.TenantId}. Select it in the saved clients list to edit or connect to it rather than creating a second entry.");
        var index = all.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            profile.CreatedAt = all[index].CreatedAt;
            all[index] = profile;
        }
        else all.Add(profile);
        Evidence.SaveProfiles(all);
        Profiles.Clear();
        foreach (var p in all) Profiles.Add(p);
        if (Profile?.Id == profile.Id)
        {
            if (!string.Equals(Profile.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase))
            { ExchangeSnapshot = null; ExchangeCapturedByTool = false; _exchangeFromHistory = false; Assessment = null; CancelPendingDiscovery(); }
            Profile = profile;
            Plan = null;
            AcknowledgedSnapshotId = null;
        }
        Logger.Info("Profiles", $"Saved profile '{profile.Company}' for tenant {profile.TenantId}.", profile.TenantId);
        Notify();
        return profile;
    }

    public async Task DeleteProfileAsync(string id)
    {
        if (Busy) throw new ToolkitException("Wait for the active operation before deleting a profile.");
        if (Profile?.Id == id) await DisconnectAsync();
        var remaining = Profiles.Where(p => p.Id != id).ToList();
        Evidence.SaveProfiles(remaining);
        Profiles.Clear();
        foreach (var p in remaining) Profiles.Add(p);
        Logger.Info("Profiles", "Saved connection removed. Tenant objects and saved evidence are retained.");
        Notify();
    }

    // ---- connection ----------------------------------------------------------------------------------------------

    public async Task<DiscoveredTenant?> DiscoverTenantAsync()
    {
        DiscoveredTenant? discovered = null;
        await RunExclusiveAsync("Finding the signed-in organisation", async progress =>
        {
            progress.Report("Choose the client's own work or school account in Microsoft sign-in.");
            var discoveryScopes = Settings.ResolveClient(SessionMode.Assessment, null)?.IsSharedFallback == true
                ? Connections.ScopesFor(SessionMode.Assessment, RequireStandard()) : new[] { "User.Read", "Organization.Read.All" };
            await CancelPendingDiscoveryAsync();
            var result = await TenantDiscoveryService.DiscoverRetainedAsync(_http, Settings, RequireStandard(),
                Settings.ResolveClient(SessionMode.Assessment, null)?.IsSharedFallback == true
                    ? Connections.ScopesFor(SessionMode.Assessment, RequireStandard()) : new[] { "User.Read", "Organization.Read.All" }, AuthenticationWindow(), Logger, OperationToken,
                ct => ReviewPermissionRequestAsync("Not selected — Microsoft will ask you to choose", ToolkitSettings.MicrosoftGraphPowerShellClientId, discoveryScopes, "Quick Connect: read-only discovery; possible Microsoft consent", ct));
            _pendingDiscovery = result;
            if (OperationToken.IsCancellationRequested) { await CancelPendingDiscoveryAsync(); OperationToken.ThrowIfCancellationRequested(); }
            discovered = result.Identity;
        });
        return discovered;
    }

    public void CancelPendingDiscovery()
    {
        var pending = _pendingDiscovery; _pendingDiscovery = null;
        if (pending is not null) _pendingRelease = ReleasePendingAsync(_pendingRelease, pending);
    }
    private static async Task ReleasePendingAsync(Task previous, PendingTenantDiscovery pending)
    { await previous; await pending.DisposeAsync(); }
    public async Task CancelPendingDiscoveryAsync() { CancelPendingDiscovery(); await _pendingRelease; }

    public Task ConnectAsync(TenantProfile profile, SessionMode mode, string? loginHint = null,
        DiscoveredTenant? expectedIdentity = null) => RunExclusiveAsync(
        mode == SessionMode.Deployment ? "Connecting with deployment access" : "Connecting (read-only)", async progress =>
        {
            var standard = RequireStandard();
            Connections.ParentWindowHandle = AuthenticationWindow();
            Connections.LoginHint = loginHint ?? Session?.Account ?? ApplicationSetup?.Identity.Account ?? "";
            // Repeated connection buttons for the same complete profile need fresh read checks, not another
            // interactive sign-in. A changed mode, application, profile or discovery confirmation uses full verification.
            if (expectedIdentity is null && loginHint is null && CanReuseConnection(profile, mode))
            {
                progress.Report("Reusing the current verified session; checking read access without another sign-in.");
                Access = await Connections.CheckAccessAsync(Connection!, standard, progress, OperationToken);
                await LoadLicencesCoreAsync(includeUsers: false);
                return;
            }
            ExperimentalGuard.Invalidate();
            var requestedClient = Settings.ResolveClient(mode, profile) ?? throw new ConfigurationException("No application is configured for this access mode.");
            var requestedScopes = Connections.ScopesFor(mode, standard).ToArray();
            var requestedAccount = Connections.LoginHint;
            var approvedUntil = DateTimeOffset.MinValue;
            if (mode == SessionMode.Deployment)
            {
                await ReviewPermissionRequestAsync(profile.TenantId, requestedClient.ClientId, requestedScopes,
                    "Request deployment access — experimental tenant changes stay off until separately approved", OperationToken, requestedAccount);
                approvedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            }
            async Task BeforeInteractive(CancellationToken ct)
            {
                if (!string.Equals(Settings.ResolveClient(mode, profile)?.ClientId, requestedClient.ClientId, StringComparison.OrdinalIgnoreCase)
                    || !requestedScopes.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(Connections.ScopesFor(mode, standard)))
                    throw new SafetyViolationException("The displayed application or permissions changed. Review a new request; no sign-in request was sent.");
                if (mode == SessionMode.Deployment)
                {
                    if (approvedUntil <= DateTimeOffset.UtcNow) throw new SafetyViolationException("Access-request approval expired. Review the request again.");
                    ct.ThrowIfCancellationRequested();
                }
                else await ReviewPermissionRequestAsync(profile.TenantId, requestedClient.ClientId, requestedScopes,
                    "Read-only assessment access; possible Microsoft consent", ct, requestedAccount);
            }
            Plan = null; AcknowledgedSnapshotId = null;
            ConnectedTenant nextConnection;
            if (expectedIdentity is not null)
            {
                var pending = _pendingDiscovery ?? throw new AuthenticationRequiredException("Quick Connect was cancelled. Start it again.");
                if (pending.Identity != expectedIdentity) throw new TenantMismatchException("Quick Connect account changed.");
                _pendingDiscovery = null;
                try { nextConnection = await Connections.ConfirmDiscoveryAsync(pending, profile, standard, progress, OperationToken, BeforeInteractive); }
                finally { await pending.DisposeAsync(); }
            }
            else
            {
                await CancelPendingDiscoveryAsync();
                nextConnection = await Connections.ConnectAsync(profile, mode, standard, progress, OperationToken, beforeInteractive: BeforeInteractive);
            }
            await DisconnectCoreAsync(releaseOnly: true);
            await DisconnectSetupCoreAsync();
            Profile = profile;
            Connection = nextConnection;
            nextConnection.AuthorisationInvalidated += OnAuthorisationInvalidated;
            Evidence.MarkInterruptedRuns(profile.TenantId);
            var interrupted = Evidence.LoadRuns(profile.TenantId).Count(r => r.Status == RunStatus.Interrupted);
            InterruptedNotice = interrupted == 0 ? "" : $"{interrupted} interrupted deployment run(s) need review in the change register. Preserve the original evidence; uncertain requests must not be replayed.";
            progress.Report("Checking access (read-only).");
            Access = await Connections.CheckAccessAsync(Connection, standard, progress, OperationToken);
            await LoadLicencesCoreAsync(includeUsers: false);
        });

    public bool CanReuseConnection(TenantProfile profile, SessionMode mode) => Profile is not null
        && Session is { TenantVerified: true, OperatorVerified: true } session && session.Mode == mode
        && DateTimeOffset.TryParse(session.TokenExpiresAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var expires) && expires > DateTimeOffset.UtcNow
        && string.Equals(session.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(session.ClientId, Settings.ResolveClient(mode, profile)?.ClientId, StringComparison.OrdinalIgnoreCase)
        && ConnectionProfileDigest(Profile) == ConnectionProfileDigest(profile);

    private static string ConnectionProfileDigest(TenantProfile profile)
    {
        // Form validation and saving update these local timestamps even when every client setting is unchanged.
        // Keep every other field in the comparison, including future additions to the profile schema.
        var value = ToolkitJson.ToNode(profile)!.AsObject();
        value.Remove("createdAt"); value.Remove("updatedAt");
        return CanonicalJson.Sha256(value);
    }

    public Task CheckAccessAsync() => RunExclusiveAsync("Checking access", async progress =>
    {
        var connection = RequireConnection();
        Access = await Connections.CheckAccessAsync(connection, RequireStandard(), progress, OperationToken);
        Plan = null;
        AcknowledgedSnapshotId = null;
    });

    public async Task DisconnectAsync()
    {
        if (Busy) throw new ToolkitException("Stop the active run and wait for evidence collection before disconnecting.");
        await CancelPendingDiscoveryAsync();
        await DisconnectCoreAsync();
        await DisconnectSetupCoreAsync();
        Notify();
    }

    private async Task DisconnectCoreAsync(bool releaseOnly = false)
    {
        ExperimentalGuard.Invalidate();
        var connection = Connection;
        Connection = null;
        Licences = null;
        InterruptedNotice = "";
        Access = null;
        Snapshot = null;
        ExchangeSnapshot = null; ExchangeCapturedByTool = false; _exchangeFromHistory = false;
        SnapshotIsLive = false;
        Assessment = null;
        Plan = null;
        AcknowledgedSnapshotId = null;
        Control = null;
        if (connection is not null)
        { connection.AuthorisationInvalidated -= OnAuthorisationInvalidated; if (releaseOnly) await connection.ReleaseAsync(); else await connection.DisposeAsync(); }
    }

    public Task ConnectApplicationSetupAsync(string tenantId) => RunExclusiveAsync("Signing in for application setup", async progress =>
    {
        if (ApplicationSetup is { } existing && string.Equals(existing.Identity.TenantId, tenantId.Trim(), StringComparison.OrdinalIgnoreCase))
            return;
        ExperimentalGuard.Invalidate();
        await ReviewPermissionRequestAsync(tenantId.Trim(), ApplicationSetupService.BootstrapClientId, ApplicationSetupService.SetupScopes,
            "Temporary administrator setup access — experimental application registration/configuration and grant inspection", OperationToken, Session?.Account);
        var approvedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        Task BeforeSetupInteractive(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (approvedUntil <= DateTimeOffset.UtcNow) throw new SafetyViolationException("Setup access-request approval expired. Review it again.");
            return Task.CompletedTask;
        }
        Plan = null; AcknowledgedSnapshotId = null;
        var nextSetup = await ApplicationSetupService.ConnectAsync(_http, tenantId.Trim(), Logger, OperationToken,
            AuthenticationWindow(), Settings.UseSystemBrowser, Session?.Account ?? "", BeforeSetupInteractive);
        await CancelPendingDiscoveryAsync();
        await DisconnectSetupCoreAsync();
        ApplicationSetup = nextSetup;
    });

    public Task DisconnectApplicationSetupAsync() => RunExclusiveAsync("Closing privileged setup session", _ => DisconnectSetupCoreAsync());

    private static IntPtr AuthenticationWindow() => System.Windows.Application.Current?.MainWindow is { } window
        ? new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle() : IntPtr.Zero;

    private async Task DisconnectSetupCoreAsync()
    {
        var setup = ApplicationSetup;
        ApplicationSetup = null;
        if (setup is not null) await setup.DisposeAsync();
    }

    public ConnectedTenant RequireConnection()
    {
        var connection = Connection ?? throw new ToolkitException("Connect to a tenant first.");
        if (Profile is null || !string.Equals(connection.Session.TenantId, Profile.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new TenantMismatchException("The connected tenant does not match the selected client profile.");
        return connection;
    }

    // ---- capture, assess, plan, deploy ---------------------------------------------------------------------------------

    public Task CaptureAsync() => RunExclusiveAsync("Reading tenant configuration", async progress =>
    {
        var connection = RequireConnection();
        var standard = RequireStandard();
        Plan = null;
        AcknowledgedSnapshotId = null;
        var collectionProgress = new Progress<CollectionProgress>(p => ((IProgress<string>)progress).Report($"{p.Message} ({p.Completed}/{p.Total})"));
        var snapshot = await Collector.CollectAsync(connection.Graph, connection.Session, Profile!, standard, collectionProgress, OperationToken);
        Evidence.SaveSnapshot(snapshot);
        Snapshot = snapshot;
        SnapshotIsLive = true;
        Assessment = null;
        progress.Report("Comparing with the Build Standard.");
        RunAssessment();
    });

    /// <summary>Loads a stored snapshot for offline review. It is never eligible for deployment.</summary>
    public void LoadStoredSnapshot(string snapshotId)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var stored = Evidence.LoadSnapshot(profile.TenantId, snapshotId) ?? throw new ToolkitException("Snapshot not found.");
        if (stored.ExchangeCapture is not null)
        {
            // Supplemental Exchange evidence is refused when modified, as the headless runner refuses it.
            if (!Evidence.SnapshotIntegrityIntact(stored)) throw new IntegrityException("This stored Exchange/Purview snapshot no longer matches its recorded integrity digest. It was modified after capture and cannot be reopened as evidence.");
            SetExchangeSnapshot(stored, false, fromHistory: true);
            return;
        }
        Snapshot = stored;
        SnapshotIsLive = false;
        Plan = null;
        AcknowledgedSnapshotId = null;
        RunAssessment();
        Notify();
    }

    /// <summary>
    /// Runs a partial check and saves it to the scoped store. File is empty, and NotSavedReason says why, when a valid
    /// completed result is too large to store: the result is still returned so it is not lost, but it must be shown
    /// as not saved.
    /// </summary>
    public async Task<(ScopedCheckEvidence Evidence, string File, string? NotSavedReason)> RunScopedCheckAsync(CheckSelection selection, bool historical)
    {
        ScopedCheckEvidence? result = null;
        string file = "";
        string? notSaved = null;
        await RunExclusiveAsync(historical ? "Reviewing selected stored evidence" : "Reading selected requirements", async progress =>
        {
            var profile = Profile ?? throw new ToolkitException("Select a saved client first.");
            var catalogue = RequireStandard();
            selection.ValidateFor(catalogue, profile);
            var service = new ScopedCheckService(SystemClock.Instance, ToolkitVersion.Current, Logger, Paths.StandardsDirectory);
            var mappings = Evidence.LoadMappings(profile.TenantId);
            var deviations = Evidence.LoadDeviations(profile.TenantId);
            if (historical)
            {
                // The same sources as RunAssessment: the Graph capture, or an Exchange-only capture, with any separately
                // imported Exchange/Purview evidence used in place of the source's own.
                var source = Snapshot ?? ExchangeSnapshot ?? throw new ToolkitException("Open or capture a configuration first. This review will retain its original capture time.");
                var separate = Snapshot is null ? null : ExchangeSnapshot?.ExchangeCapture;
                result = service.ReviewHistorical(source, catalogue, profile, selection, mappings, deviations, "stored-evidence review", separate);
            }
            else
            {
                var connection = RequireConnection();
                result = await service.CollectAsync(connection.Graph, connection.Session, catalogue, profile, selection, mappings,
                    deviations, new Progress<CollectionProgress>(p => progress.Report(p.Message)), OperationToken);
            }
            var (saved, reason) = new ScopedCheckStore(Paths).TrySave(result, catalogue, profile);
            file = saved ?? ""; notSaved = reason;
            if (notSaved is not null) Logger.Warn("Scoped check", "Partial check result not saved: " + notSaved, profile.TenantId);
            // No ordinary snapshot, assessment, plan or acknowledgement is replaced by this partial check.
        });
        return (result ?? throw new OperationCanceledException("No scoped result was accepted."), file, notSaved);
    }

    public void RunAssessment()
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var snapshot = Snapshot ?? ExchangeSnapshot ?? throw new ToolkitException("Read the tenant configuration first.");
        var standard = RequireStandard();
        // A stored Graph snapshot is judged as of its own capture, so reopening history reproduces its findings.
        // An Exchange-only snapshot reopened from history is judged as of its capture too, as the headless runner does.
        var stored = Snapshot is { } graph ? (SnapshotIsLive ? null : graph)
            : _exchangeFromHistory ? ExchangeSnapshot : null;
        DateTimeOffset? evidenceTime = stored is not null && Timestamps.TryParse(stored.CapturedAt, out var capturedAt) ? capturedAt : null;
        Assessment = AssessmentContext.Assess(Engine, Evidence, snapshot, standard, profile, Session?.Account ?? "offline review", ExchangeSnapshot?.ExchangeCapture, evidenceTime);
        Evidence.SaveAssessment(Assessment);
        Logger.Info("Assessment", $"Assessment {Assessment.Id}: {Assessment.Summary.Compliant} compliant, {Assessment.Summary.Missing} missing, {Assessment.Summary.PartialMatch} partial, {Assessment.Summary.UnableToAssess} unknown.", profile.TenantId);
        Notify();
    }

    public void SaveExchangeDomain(string domain)
    {
        var profile = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(Profile ?? throw new ToolkitException("Select a client first.")));
        profile.Parameters.PolicyInputs ??= new();
        profile.Parameters.PolicyInputs["exchangeDomain"] = System.Text.Json.Nodes.JsonValue.Create(MailDomain.Validate(domain));
        SaveProfile(profile);
        InvalidatePolicyState();
    }

    public void ImportExchangeCapture(string fileName, string enteredDomain)
    {
        if (Busy) throw new ToolkitException("Wait for the active operation before importing evidence.");
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var standard = RequireStandard();
        if (standard.SchemaVersion < 5 || !standard.Controls.Any(c => c.Area == "Exchange"))
            throw new ConfigurationException("Select standard 2026.09.12 or a release with Exchange controls before importing.");
        MailDomain.Validate(enteredDomain);
        using var stream = File.OpenRead(fileName);
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[ExchangeCaptureSchema.MaximumBytes + 1];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        if (count > ExchangeCaptureSchema.MaximumBytes) throw new ConfigurationException("Exchange capture exceeds the 2 MiB limit.");
        var snapshot = ExchangeEvidenceImporter.Import(new string(buffer, 0, count), profile, standard, enteredDomain, DateTimeOffset.UtcNow);
        Evidence.SaveSnapshot(snapshot);
        SetExchangeSnapshot(snapshot, false);
    }

    private void SetExchangeSnapshot(TenantSnapshot snapshot, bool capturedByTool, bool fromHistory = false)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        if (!string.Equals(snapshot.TenantId, profile.TenantId, StringComparison.OrdinalIgnoreCase) || snapshot.ExchangeCapture is null)
            throw new TenantMismatchException("Exchange evidence belongs to another tenant or has no observations.");
        ExchangeCaptureSchema.Validate(snapshot.ExchangeCapture, profile.TenantId, DateTimeOffset.UtcNow);
        ExchangeSnapshot = snapshot; ExchangeCapturedByTool = capturedByTool; _exchangeFromHistory = fromHistory;
        // The Graph capture, plan and acknowledgement are unchanged. Exchange cannot satisfy their gates.
        RunAssessment(); Notify();
    }

    public Task CaptureExchangeAsync(bool includePurview, IExchangeCaptureRunner? runner = null) => RunExclusiveAsync("Reading Exchange/Purview configuration", async progress =>
    {
        var connection = RequireConnection(); var profile = Profile!; var standard = RequireStandard();
        if (!connection.Session.TenantVerified || !connection.Session.OperatorVerified || standard.SchemaVersion < 5)
            throw new TenantMismatchException("Verify the connected tenant and operator before Exchange capture.");
        var domain = MailDomain.Validate(connection.Session.PrimaryDomain.Length > 0 ? connection.Session.PrimaryDomain : profile.Domain);
        var json = await (runner ?? new ExchangeCaptureRunner()).CaptureAsync(profile.TenantId, domain,
            connection.Session.Account, includePurview, progress, OperationToken);
        OperationToken.ThrowIfCancellationRequested();
        var snapshot = ExchangeEvidenceImporter.Import(json, profile, standard, domain, DateTimeOffset.UtcNow);
        Evidence.SaveSnapshot(snapshot);
        SetExchangeSnapshot(snapshot, true);
    });

    public IReadOnlyList<string> ExchangeDomains => ExchangeSnapshot?.ExchangeCapture is { } capture
        && ExchangeCaptureSchema.Complete(capture, "acceptedDomains", out var domains)
        ? domains.Select(d => d["DomainName"]?.GetValue<string>() ?? "").Where(d =>
            { try { return MailDomain.Validate(d) == d; } catch (ConfigurationException) { return false; } })
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList()
        : Array.Empty<string>();

    public void SelectExchangeDomain(string domain)
    {
        if (Busy) throw new ToolkitException("Wait for capture to finish before selecting a mail domain.");
        domain = MailDomain.Validate(domain);
        if (!ExchangeDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            throw new ConfigurationException("Select a domain from complete accepted-domain observations.");
        var previous = ExchangeSnapshot ?? throw new ToolkitException("Capture Exchange first.");
        var capture = ToolkitJson.Deserialize<ExchangeCapture>(ToolkitJson.Serialize(previous.ExchangeCapture!));
        capture.Domain = domain; capture.Dns.Clear();
        var snapshot = ExchangeEvidenceImporter.Snapshot(capture, Profile!, RequireStandard());
        Evidence.SaveSnapshot(snapshot); SetExchangeSnapshot(snapshot, ExchangeCapturedByTool);
    }

    public Task CheckExchangeDnsAsync(IDnsLookup? dns = null) => RunExclusiveAsync("Checking public DKIM and DMARC DNS records", async _ =>
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var snapshot = ExchangeSnapshot ?? throw new ToolkitException("Capture or import Exchange observations first.");
        var updated = await ExchangeEvidenceImporter.CheckDnsAsync(snapshot, profile, RequireStandard(), dns ?? new WindowsDnsLookup(), DateTimeOffset.UtcNow, OperationToken);
        Evidence.SaveSnapshot(updated);
        SetExchangeSnapshot(updated, ExchangeCapturedByTool);
    });

    public string ExportExchangeProposal(string controlId, string typedTenant, string enteredDomain)
    {
        if (Busy) throw new ToolkitException("Wait for the active operation before exporting a proposal.");
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var snapshot = ExchangeSnapshot ?? throw new ToolkitException("Capture or import Exchange observations first.");
        var now = DateTimeOffset.UtcNow;
        var text = ExchangeProposal.Create(RequireStandard(), profile, snapshot, Evidence, controlId, typedTenant, enteredDomain, now);
        return Exporter.ExportExchangeProposal(text, controlId, now);
    }

    public DeploymentPlan BuildPlan(IReadOnlyList<string> controlIds)
    {
        if (Busy) throw new ToolkitException("Wait for the active operation.");
        var connection = RequireConnection();
        var snapshot = Snapshot ?? throw new ToolkitException("Read the tenant configuration first.");
        if (!SnapshotIsLive) throw new ToolkitException("A stored snapshot is loaded for review. Read the live tenant configuration before planning.");
        var profile = Profile!;
        var plan = Planner.Build(new PlanRequest
        {
            Profile = profile,
            Standard = RequireStandard(),
            Snapshot = snapshot,
            Mappings = Evidence.LoadMappings(profile.TenantId),
            Deviations = Evidence.LoadDeviations(profile.TenantId),
            SelectedControlIds = controlIds,
            Session = connection.Session
        });
        Evidence.SavePlan(plan);
        Plan = plan;
        AcknowledgedSnapshotId = null;
        Logger.Info("Plan", $"Plan {plan.Id} built: {plan.Rows.Count(r => r.Action == PlanAction.Create)} create, {plan.Rows.Count(r => r.Action == PlanAction.Update)} update, {plan.Rows.Count(r => !r.IsWrite)} not automated.", profile.TenantId);
        Notify();
        return plan;
    }

    public void AcknowledgeSnapshot()
    {
        var snapshot = Snapshot ?? throw new ToolkitException("Capture a snapshot first.");
        if (!SnapshotIsLive) throw new ToolkitException("Only the live capture can be acknowledged for deployment.");
        AcknowledgedSnapshotId = snapshot.Id;
        Logger.Info("Plan", $"Before-change snapshot {snapshot.Id} acknowledged by the engineer.", snapshot.TenantId);
        Notify();
    }

    public void ValidatePlanForExecution()
    {
        var connection = RequireConnection();
        var plan = Plan ?? throw new ToolkitException("Build and review a plan first.");
        var snapshot = Snapshot ?? throw new ToolkitException("Capture a snapshot first.");
        var profile = Profile!;
        DeploymentPlanner.Validate(plan, new PlanValidationContext
        {
            Profile = profile,
            Standard = RequireStandard(),
            Snapshot = snapshot,
            Mappings = Evidence.LoadMappings(profile.TenantId),
            Session = connection.Session,
            Deviations = Evidence.LoadDeviations(profile.TenantId),
            AcknowledgedSnapshotId = AcknowledgedSnapshotId,
            Now = DateTimeOffset.UtcNow,
            MaxSnapshotAge = TimeSpan.FromMinutes(Settings.SnapshotMaxAgeMinutes),
            MaxPlanAge = TimeSpan.FromMinutes(Settings.PlanMaxAgeMinutes)
        });
        if (Access is not null)
        {
            var missing = plan.WriteRows.Select(r => r.Collection).Distinct()
                .Where(c => Access.Writes.Any(w => w.Collection == c && w.Status == "Missing scope")).ToList();
            if (missing.Count > 0)
                throw new PlanValidationException("A required delegated write scope is missing for: " + string.Join(", ", missing) + ". Reconnect with deployment access and consent to the write permissions.");
        }
    }

    public Task DeployAsync(string typedTenantId) => RunExclusiveAsync("Deploying reviewed changes", async progress =>
    {
        RequireExperimentalChanges(ExperimentalOperation.Deploy);
        var connection = RequireConnection();
        var profile = Profile!;
        if (!TenantConfirmation.Matches(typedTenantId, profile.TenantId))
            throw new TenantMismatchException("The typed tenant ID does not match the connected tenant. Deployment refused.");
        ValidatePlanForExecution();
        var plan = Plan!;
        var snapshot = Snapshot!;
        var mappings = Evidence.LoadMappings(profile.TenantId);
        Control = new DeploymentControl();
        LastRun = await Executor.StartAsync(new ExecutionRequest
        {
            TypedTenant = typedTenantId,
            Plan = plan,
            Profile = profile,
            Standard = RequireStandard(),
            Snapshot = snapshot,
            Mappings = mappings,
            Session = connection.Session,
            Graph = connection.Graph,
            AcknowledgedSnapshotId = AcknowledgedSnapshotId,
            MaxSnapshotAge = TimeSpan.FromMinutes(Settings.SnapshotMaxAgeMinutes),
            MaxPlanAge = TimeSpan.FromMinutes(Settings.PlanMaxAgeMinutes)
        }, Control, progress);
        Plan = null;
        AcknowledgedSnapshotId = null;
        Snapshot = null;
        SnapshotIsLive = false;
        Assessment = null;
        Control = null;
    });

    public void PauseDeployment() => Control?.Pause();
    public void ResumeDeployment() => Control?.Resume();
    public void StopDeployment() { Control?.Stop(); ProgressDetail = StopGuidance; }

    // ---- deviations and manual checks ---------------------------------------------------------------------------------

    public IReadOnlyList<Deviation> LoadDeviations() => Profile is null ? Array.Empty<Deviation>() : Evidence.LoadDeviations(Profile.TenantId);

    public void SaveDeviation(Deviation deviation)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var standard = RequireStandard();
        if (ControlInstances.Find(standard, profile, deviation.ControlId) is null) throw new ConfigurationException($"'{deviation.ControlId}' is not a control in the loaded standard.");
        if (string.IsNullOrWhiteSpace(deviation.Reason) || deviation.Reason.Trim().Length < 8) throw new ConfigurationException("Record a meaningful reason for the deviation (at least 8 characters).");
        if (string.IsNullOrWhiteSpace(deviation.ApprovedBy)) throw new ConfigurationException("Record who approved the deviation.");
        if (deviation.ReviewBy.Length > 0 && !DateOnly.TryParse(deviation.ReviewBy, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new ConfigurationException("Review-by must be a date in yyyy-MM-dd format or empty.");
        deviation.TenantId = profile.TenantId;
        deviation.Id = string.IsNullOrWhiteSpace(deviation.Id) ? Guid.NewGuid().ToString() : deviation.Id;
        deviation.RecordedAt = Timestamps.Format(DateTimeOffset.UtcNow);
        deviation.RecordedByAccount = Session?.Account ?? "local operator - not independently verified";
        var list = Evidence.LoadDeviations(profile.TenantId).Where(d => !string.Equals(d.ControlId, deviation.ControlId, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Add(deviation);
        Evidence.SaveDeviations(profile.TenantId, list);
        Plan = null;
        AcknowledgedSnapshotId = null;
        Logger.Info("Deviations", $"Deviation recorded for {deviation.ControlId} ({deviation.Kind}).", profile.TenantId, deviation.ControlId);
        if (Snapshot is not null) RunAssessment(); else Notify();
    }

    public void DeleteDeviation(string controlId)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var list = Evidence.LoadDeviations(profile.TenantId).Where(d => !string.Equals(d.ControlId, controlId, StringComparison.OrdinalIgnoreCase)).ToList();
        Evidence.SaveDeviations(profile.TenantId, list);
        Plan = null;
        AcknowledgedSnapshotId = null;
        Logger.Info("Deviations", $"Deviation removed for {controlId}.", profile.TenantId, controlId);
        if (Snapshot is not null) RunAssessment(); else Notify();
    }

    public ManualCheckRegister LoadManualChecks() => Profile is null ? new ManualCheckRegister() : Evidence.LoadManualChecks(Profile.TenantId);

    public void SaveManualCheck(string controlId, string status, string note)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        if (ControlInstances.Find(RequireStandard(), profile, controlId) is null) throw new ConfigurationException("Unknown control.");
        if (status is not ("Pending" or "Pass" or "Fail" or "Unknown")) throw new ConfigurationException("Invalid outcome.");
        if (status != "Pending" && (note ?? "").Trim().Length < 8) throw new ConfigurationException("Record an evidence note of at least 8 characters.");
        var register = Evidence.LoadManualChecks(profile.TenantId);
        register.Checks[controlId] = new ManualCheck
        {
            ControlId = controlId,
            Status = status,
            Note = (note ?? "").Trim().Length > 2000 ? note!.Trim()[..2000] : (note ?? "").Trim(),
            RecordedAt = Timestamps.Format(DateTimeOffset.UtcNow),
            RecordedBy = Session?.Account ?? "local operator - not independently verified"
        };
        Evidence.SaveManualChecks(register);
        Notify();
    }

    // ---- jobs (INT-049/050) -------------------------------------------------------------------------------------------

    /// <summary>Who a workflow record names as its actor: the signed-in account, or a stated unverified local operator.</summary>
    public string Actor => Session?.Account ?? "local operator - not independently verified";

    public (IReadOnlyList<TenantJob> Jobs, IReadOnlyList<UnreadableRecord> Unreadable) LoadJobs() =>
        Profile is null ? (Array.Empty<TenantJob>(), Array.Empty<UnreadableRecord>()) : Evidence.LoadJobs(Profile.TenantId);

    /// <summary>
    /// Projects a job against the loaded standard, the selected client's inputs and, when it is a saved capture of this
    /// tenant, the capture in view. Computing it writes nothing and a complete job grants no authority.
    /// </summary>
    public (JobProjection Projection, JobCompletion Completion) ProjectJob(string jobId)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var standard = RequireStandard();
        var now = DateTimeOffset.UtcNow;
        var projection = JobProjection.Build(Evidence, profile.TenantId, jobId, standard, profile, now, SavedCapture());
        return (projection, JobCompletion.Build(Evidence, projection, standard, profile, now));
    }

    /// <summary>The capture in view when it is saved for the selected client's tenant; records can only pin a saved capture.</summary>
    public TenantSnapshot? SavedCapture() =>
        Profile is not null && Snapshot is not null && string.Equals(Snapshot.TenantId, Profile.TenantId, StringComparison.OrdinalIgnoreCase)
            && Evidence.LoadSnapshot(Profile.TenantId, Snapshot.Id) is not null ? Snapshot : null;

    /// <summary>
    /// Stored assessments an outcome may cite: of the saved capture in view, under the loaded standard. Empty when no saved capture is in view,
    /// since a citation pins the capture it assessed.
    /// </summary>
    public IReadOnlyList<StoredAssessment> CitableAssessments()
    {
        if (Profile is null || Standard is not { } standard || SavedCapture() is not { } capture) return Array.Empty<StoredAssessment>();
        return Evidence.LoadAssessments(Profile.TenantId)
            .Where(a => string.Equals(a.Result.SnapshotId, capture.Id, StringComparison.OrdinalIgnoreCase) && a.Result.Release == standard.Release
                && string.Equals(a.Result.StandardDigest, standard.IntegrityDigest, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>The stable requirement identity for a new record: the existing line's, else shipped lineage's, else the control ID.</summary>
    public string SemanticIdFor(string controlId, string? existing)
    {
        var standard = RequireStandard();
        ReleaseLineage? lineage = null;
        try { lineage = ReleaseLineage.Load(Paths.StandardsDirectory, StandardsManifest.Load(Paths.StandardsDirectory), standard.Release); }
        catch (ToolkitException ex) { Logger.Warn("Jobs", "Release lineage could not be read; new records use the control ID as their identity. " + ex.Message); }
        return SemanticIdentity.Resolve(controlId, lineage, existing);
    }

    public TenantJob OpenJob(string intention, string owner, string notes)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var job = new JobWorkflow(Evidence, SystemClock.Instance).OpenJob(profile, RequireStandard(), intention, owner, notes, Actor, SavedCapture()?.Id);
        Logger.Info("Jobs", $"Job {job.Id} opened ({job.Intention}).", profile.TenantId);
        Notify();
        return job;
    }

    public TenantObservation RecordObservation(string jobId, ObservationRequest request)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var observation = new JobWorkflow(Evidence, SystemClock.Instance).Record(profile.TenantId, jobId, profile, RequireStandard(), request, Actor);
        Logger.Info("Jobs", $"Outcome {observation.Status} recorded for {observation.InstanceKey} in job {jobId}.", profile.TenantId, observation.ControlId);
        Notify();
        return observation;
    }

    public TenantDisposition RecordDecision(string jobId, DispositionRequest request)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var disposition = new JobWorkflow(Evidence, SystemClock.Instance).Decide(profile.TenantId, jobId, profile, RequireStandard(), request, Actor);
        Logger.Info("Jobs", $"Decision {disposition.Decision} recorded for {disposition.InstanceKey} in job {jobId}.", profile.TenantId, disposition.ControlId);
        Notify();
        return disposition;
    }

    public CutoverRevision RecordCutover(string jobId, CutoverRequest request)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var revision = new JobWorkflow(Evidence, SystemClock.Instance).Cutover(profile.TenantId, jobId, profile, RequireStandard(), request, Actor);
        Logger.Info("Jobs", $"Cutover case {revision.CaseId} recorded at {revision.Stage} for {revision.InstanceKey} in job {jobId}.", profile.TenantId, revision.ControlId);
        Notify();
        return revision;
    }

    // ---- drift ---------------------------------------------------------------------------------------------------------

    public DriftReport CompareSnapshots(string beforeId, string afterId)
    {
        var profile = Profile ?? throw new ToolkitException("Select a client first.");
        var before = Evidence.LoadSnapshot(profile.TenantId, beforeId) ?? throw new ToolkitException("Before snapshot not found.");
        var after = Evidence.LoadSnapshot(profile.TenantId, afterId) ?? throw new ToolkitException("After snapshot not found.");
        return Drift.Compare(before, after, RequireStandard(), profile, Evidence.LoadMappings(profile.TenantId), Evidence.LoadDeviations(profile.TenantId));
    }

    public Task LoadLicencesAsync(bool includeUsers) => RunExclusiveAsync("Reading licences and assignments", _ => LoadLicencesCoreAsync(includeUsers));

    public async Task<WriteVerification?> ReverifyAsync(string runId, string? controlId, bool historical = false)
    {
        WriteVerification? result = null;
        await RunExclusiveAsync("Re-verifying recorded write using read-only requests", async _ =>
        {
            var connection = RequireConnection();
            Plan = null; AcknowledgedSnapshotId = null; SnapshotIsLive = false;
            var service = new WriteVerificationService(Evidence, SystemClock.Instance);
            result = controlId is null
                ? await service.VerifyRecoveryAsync(connection.Graph, connection.Session, RequireStandard(), runId, OperationToken)
                : await service.VerifyDeploymentAsync(connection.Graph, connection.Session, RequireStandard(), runId, controlId, historical, OperationToken);
        });
        return result;
    }

    private async Task LoadLicencesCoreAsync(bool includeUsers)
    {
        var connection = RequireConnection();
        Licences = await new LicenceInventoryService(SystemClock.Instance).CaptureAsync(connection.Graph, connection.Session.TenantId, includeUsers, OperationToken);
        var file = System.IO.Path.Combine(Paths.TenantDirectory(connection.Session.TenantId), "licensing", Guid.NewGuid() + ".json");
        Evidence.WriteJsonAtomic(file, Licences);
    }

    /// <summary>The previewed recovery, or null when the engineer stopped the operation before the preview was produced.</summary>
    public async Task<RecoveryPlan?> PreviewRecoveryAsync(string runId, string controlId, RecoveryAction action)
    {
        RecoveryPlan? result = null;
        await RunExclusiveAsync("Capturing evidence and previewing recovery", async progress =>
        {
            var connection = RequireConnection();
            Plan = null; AcknowledgedSnapshotId = null;
            var snapshot = await Collector.CollectAsync(connection.Graph, connection.Session, Profile!, RequireStandard(), new Progress<CollectionProgress>(p => progress.Report(p.Message)), OperationToken);
            Evidence.SaveSnapshot(snapshot);
            Snapshot = snapshot; SnapshotIsLive = true;
            result = await Recovery.PreviewAsync(connection.Graph, connection.Session, RequireStandard(), snapshot, runId, controlId, action, OperationToken);
        });
        return result;
    }

    /// <summary>
    /// The recorded recovery run, or null when the engineer stopped the operation before the service returned one. A
    /// null result says nothing about whether a write was sent; the change register is the record of that.
    /// </summary>
    public async Task<RecoveryRun?> ExecuteRecoveryAsync(RecoveryPlan plan, string tenantConfirmation, bool approved, bool reviewedDrift)
    {
        RequireExperimentalChanges(ExperimentalOperation.Recovery);
        RecoveryRun? result = null;
        await RunExclusiveAsync("Applying reviewed recovery", async _ =>
        {
            var connection = RequireConnection();
            Plan = null; AcknowledgedSnapshotId = null; SnapshotIsLive = false;
            result = await Recovery.ExecuteAsync(connection.Graph, connection.Session, RequireStandard(), plan, tenantConfirmation, approved, reviewedDrift, OperationToken);
        });
        return result;
    }

    // ---- shutdown ------------------------------------------------------------------------------------------------------

    /// <summary>Requests a stop at the next safe boundary, waits for evidence, then removes cached tokens.</summary>
    public async Task ShutdownAsync()
    {
        if (ShutdownComplete) return;
        CancelOperation();
        if (Executor.CurrentTask is not null)
        {
            Logger.Warn("App", "Shutdown requested while a deployment is in flight; waiting for the current write and after-change evidence.");
            await Executor.WaitForCompletionAsync(Control);
        }
        if (_operationCompletion is not null) await _operationCompletion.Task;
        await CancelPendingDiscoveryAsync();
        await DisconnectCoreAsync();
        await DisconnectSetupCoreAsync();
        Logger.Info("App", "Toolkit closed.");
        Logger.Flush();
        ShutdownComplete = true;
    }
}
