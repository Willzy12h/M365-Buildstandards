using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Windows.Input;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Graph.Setup;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>
/// Guided application setup: one administrator sign-in, one reviewed approval, then the toolkit creates both
/// applications, opens Microsoft's permission approval for each in turn and reads the actual grants back. The
/// safeguards of the step-by-step flow all remain: writes need the reviewed plan, the approval tick and the typed
/// tenant ID; names never establish ownership; an unsuccessful, missing or stopped approval ends the sequence and says
/// so; and nothing is retried after an uncertain write.
/// </summary>
public sealed class ApplicationSetupViewModel : PageViewModel
{
    private string _tenantId = "", _namePrefix = "M365 BuildStandard", _confirmation = "", _assessmentId = "", _deploymentId = "",
        _outcome = "Enter the client's tenant ID and sign in as an administrator. The toolkit then shows both applications and their permissions for you to approve.";
    private bool _permissionsApproved, _assignOperator = true;
    private ApplicationSetupPlan? _plan;
    private ApplicationSetupRow? _selectedRow;
    private string _validatedContext = "";
    public ApplicationSetupViewModel(ShellViewModel shell) : base(shell, "Application setup")
    {
        ConnectCommand = Command(ConnectAsync, () => Workspace.Idle && ProfileValidator.IsGuid(TenantId.Trim()));
        PreviewCommand = Command(PreviewAsync, () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        CreateCommand = Command(SetUpAsync, () => Workspace.Idle && _plan is not null && PermissionsApproved && TenantConfirmation.Matches(Confirmation, _plan.TenantId));
        ValidateCommand = Command(ValidateAsync, () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        AssessmentConsentCommand = Command(() => OpenConsentAsync(SessionMode.Assessment), () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        DeploymentConsentCommand = Command(() => OpenConsentAsync(SessionMode.Deployment), () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        ApplyIdsCommand = Sync(ApplyIds, () => Workspace.Idle && Validations.Count == 2 && Validations.All(v => v.ConfigurationValid));
        ContinueAssessmentCommand = Command(() => ContinueAsync(SessionMode.Assessment), () => Workspace.Idle && Ready(SessionMode.Assessment));
        ContinueDeploymentCommand = Command(() => ContinueAsync(SessionMode.Deployment), () => Workspace.Idle && Ready(SessionMode.Deployment));
        CheckDelegatedAdminCommand = Command(() => ContinueAsync(SessionMode.Deployment, true),
            () => Workspace.Idle && Ready(SessionMode.Deployment, false) && !Ready(SessionMode.Deployment));
        DisconnectCommand = Command(Workspace.DisconnectApplicationSetupAsync, () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        OpenEntraCommand = Sync(() => OpenBrowser("https://entra.microsoft.com/"));
        Validations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NextStep));
    }
    public string TenantId { get => _tenantId; set { if (SetProperty(ref _tenantId, value)) InvalidatePlan(); } }
    public string NamePrefix { get => _namePrefix; set { if (SetProperty(ref _namePrefix, value)) InvalidatePlan(); } }
    public string Confirmation { get => _confirmation; set => SetProperty(ref _confirmation, value); }
    public bool PermissionsApproved { get => _permissionsApproved; set => SetProperty(ref _permissionsApproved, value); }
    /// <summary>On by default: without it the engineer who ran setup cannot connect until someone assigns them in Entra.</summary>
    public bool AssignOperator { get => _assignOperator; set { if (SetProperty(ref _assignOperator, value)) InvalidatePlan(); } }
    public bool UseSystemBrowser { get => Workspace.Settings.UseSystemBrowser; set { Workspace.Settings.UseSystemBrowser = value; OnPropertyChanged(); } }
    public string AssessmentClientId { get => _assessmentId; set { if (SetProperty(ref _assessmentId, value)) InvalidatePlan(); } }
    public string DeploymentClientId { get => _deploymentId; set { if (SetProperty(ref _deploymentId, value)) InvalidatePlan(); } }
    public string Outcome { get => _outcome; private set => SetProperty(ref _outcome, value); }
    public string SetupIdentity => Workspace.ApplicationSetup is { } setup ? $"SETUP WRITE ACCESS · {setup.Identity.Account}\nTenant {setup.Identity.TenantId}" : "Setup session disconnected. Normal assessment access cannot create applications.";
    public string PlanSummary => _plan is null ? "Sign in, or select Preview again, to see the applications and permissions for this tenant." : $"Tenant: {_plan.TenantName} · {_plan.TenantId}\nSigned in as {_plan.OperatorName} · Standard {_plan.StandardRelease}\nPlan {_plan.Id} · expires five minutes after {_plan.CreatedAt:u}";

    /// <summary>What the engineer should do now, so the page reads as one sequence rather than a set of controls.</summary>
    public string NextStep
    {
        get
        {
            if (Workspace.ApplicationSetup is null) return "Next: enter the client's tenant ID and select Sign in as administrator.";
            if (Validations.Count > 0)
                return Ready(SessionMode.Assessment) ? "Setup is complete. Select Connect read-only now." : "Setup needs attention: see the checks in step 3.";
            if (_plan is null) return "Next: select Preview again to review the applications.";
            if (_plan.Rows.Any(r => r.Status == "Review existing"))
                return "Tool applications with these names already exist. Enter their client IDs under Existing applications, then select Preview again.";
            return "Next: check the tenant, both applications and their permissions, tick approval, type the tenant ID and select Create apps and grant permissions.";
        }
    }
    public string SelectedPayload => SelectedRow?.ApplicationPayload.ToJsonString(new() { WriteIndented = true }) ?? "Select an application to inspect its exact registration payload.";
    public ApplicationSetupRow? SelectedRow { get => _selectedRow; set { SetProperty(ref _selectedRow, value); OnPropertyChanged(nameof(SelectedPayload)); OnPropertyChanged(nameof(Permissions)); } }
    public IReadOnlyList<SetupPermission> Permissions => SelectedRow?.Permissions ?? new();
    public ObservableCollection<ApplicationSetupRow> PlanRows { get; } = new();
    public ObservableCollection<ApplicationSetupItemResult> Results { get; } = new();
    public ObservableCollection<ApplicationPermissionValidation> Validations { get; } = new();
    public ICommand ConnectCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand AssessmentConsentCommand { get; }
    public ICommand DeploymentConsentCommand { get; }
    public ICommand ApplyIdsCommand { get; }
    public ICommand ContinueAssessmentCommand { get; }
    public ICommand ContinueDeploymentCommand { get; }
    public ICommand CheckDelegatedAdminCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand OpenEntraCommand { get; }

    /// <summary>
    /// Whether a setup run left both applications created, configured and verified, so Microsoft can be asked to approve
    /// their permissions. Anything less stops the sequence before approval: a partial or uncertain run is reconciled, not
    /// continued.
    /// </summary>
    public static bool ReadyForApproval(ApplicationSetupResult result) =>
        result.Status == ApplicationSetupService.CompletedStatus && result.AfterComplete
        && result.Rows.Count == 2 && result.Rows.All(r => ProfileValidator.IsGuid(r.ClientId));

    /// <summary>
    /// Which applications still need Microsoft's approval, in order, read from the actual grants; or why approval cannot
    /// be asked for yet. An application whose configuration needs attention is never sent for approval.
    /// </summary>
    public static (IReadOnlyList<SessionMode> Modes, string? Blocker) ApprovalsNeeded(ApplicationSetupValidation validation)
    {
        var modes = new List<SessionMode>();
        foreach (var mode in new[] { SessionMode.Assessment, SessionMode.Deployment })
        {
            var row = validation.Rows.FirstOrDefault(r => r.Mode == mode);
            if (row is null) return (Array.Empty<SessionMode>(), $"The {Name(mode)} application could not be checked, so no approval was requested. Select Check again.");
            if (row.ConsentComplete) continue;
            if (!row.ConfigurationValid) return (Array.Empty<SessionMode>(), $"The {Name(mode)} application needs attention before its permissions can be approved, so no approval was requested. See the checks in step 3.");
            modes.Add(mode);
        }
        return (modes, null);
    }

    public void UseTenant(string tenantId, string company) { TenantId = tenantId; }
    private ApplicationSetupService RequireSetup()
    {
        var setup = Workspace.ApplicationSetup ?? throw new ToolkitException("Sign in for application setup first.");
        if (!string.Equals(setup.Identity.TenantId, TenantId.Trim(), StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("The setup session is connected to another tenant. Sign in again.");
        return setup;
    }
    private void InvalidatePlan() { _plan = null; PlanRows.Clear(); SelectedRow = null; PermissionsApproved = false; Confirmation = ""; Validations.Clear(); OnPropertyChanged(nameof(PlanSummary)); OnPropertyChanged(nameof(NextStep)); }
    private async Task ConnectAsync()
    {
        InvalidatePlan();
        await Workspace.ConnectApplicationSetupAsync(TenantId);
        if (!string.Equals(Workspace.ApplicationSetup?.Identity.TenantId, TenantId.Trim(), StringComparison.OrdinalIgnoreCase)) return;
        await PreviewAsync();
    }
    private Task PreviewAsync() => Workspace.RunExclusiveAsync("Previewing application registrations and permissions", async progress =>
    {
        InvalidatePlan();
        _plan = await RequireSetup().PreviewAsync(Workspace.RequireStandard(), NamePrefix, Workspace.OperationToken,
            AssessmentClientId.Trim(), DeploymentClientId.Trim(), AssignOperator);
        foreach (var row in _plan.Rows) PlanRows.Add(row);
        SelectedRow = PlanRows.FirstOrDefault();
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(NextStep));
        Outcome = "Preview only; nothing has changed. Check the tenant name, both applications and every permission before approving.";
    });

    /// <summary>
    /// The guided sequence behind "Create apps and grant permissions". Each stage runs only if the one before it
    /// finished cleanly, and the page says where and why it stopped.
    /// </summary>
    private Task SetUpAsync() => Workspace.RunExclusiveAsync("Setting up the tenant applications", async progress =>
    {
        var plan = _plan ?? throw new PlanValidationException("Preview application setup again.");
        var service = RequireSetup();
        var standard = Workspace.RequireStandard();
        Results.Clear();
        progress.Report("Creating and configuring the applications. Each write is recorded before it is sent.");
        var result = await service.ExecuteAsync(plan, standard, Confirmation, PermissionsApproved,
            Path.Combine(Workspace.Paths.TenantDirectory(service.Identity.TenantId), "application-setup"), Workspace.OperationToken);
        foreach (var row in result.Rows) Results.Add(row);
        var assessmentId = result.Rows.FirstOrDefault(r => r.Mode == SessionMode.Assessment)?.ClientId;
        var deploymentId = result.Rows.FirstOrDefault(r => r.Mode == SessionMode.Deployment)?.ClientId;
        if (!string.IsNullOrWhiteSpace(assessmentId)) AssessmentClientId = assessmentId;
        if (!string.IsNullOrWhiteSpace(deploymentId)) DeploymentClientId = deploymentId;
        var created = $"{result.Status}. After-change evidence: {(result.AfterComplete ? "captured" : "incomplete — " + result.AfterError)}.\nEvidence: {result.EvidenceDirectory}";
        Outcome = created + "\n" + result.NextSteps;
        InvalidatePlan();
        if (!ProfileValidator.IsGuid(AssessmentClientId) || !ProfileValidator.IsGuid(DeploymentClientId)) return;
        Shell.Page<ConnectViewModel>().UseApplicationIds(TenantId.Trim(), AssessmentClientId, DeploymentClientId);
        if (!ReadyForApproval(result))
        {
            var context = ValidationContext;
            var validation = await service.ValidateAsync(standard, AssessmentClientId, DeploymentClientId, Workspace.OperationToken);
            GuardContext(context);
            ShowValidation(validation, context);
            Outcome = created + "\nSetup stopped before asking Microsoft to approve any permissions, because not every write was confirmed. Review each outcome above. Nothing is retried automatically: reconcile the applications, then preview again.";
            return;
        }
        await ApproveAndCheckAsync(service, standard, progress, created);
    });

    private async Task ApproveAndCheckAsync(ApplicationSetupService service, StandardCatalogue standard, IProgress<string> progress, string created)
    {
        var context = ValidationContext;
        progress.Report("Checking which applications still need Microsoft's approval.");
        var current = await service.ValidateAsync(standard, AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        GuardContext(context);
        var (needed, blocker) = ApprovalsNeeded(current);
        if (blocker is not null)
        {
            ShowValidation(current, context);
            Outcome = created + "\n" + blocker;
            return;
        }
        foreach (var mode in needed)
        {
            var failure = await ApproveAsync(service, standard, mode, progress);
            if (failure is not null) { Outcome = created + "\n" + failure; return; }
            GuardContext(context);
        }
        progress.Report("Reading the actual grants from Microsoft Graph.");
        var validation = await service.ValidateAsync(standard, AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        if (needed.Count > 0 && validation.Rows.Any(r => !r.ConsentComplete))
        {
            // A grant can take a short time to appear after approval. This is a read, so reading again is safe.
            progress.Report("Microsoft has not shown every grant yet. Reading them again in 20 seconds.");
            await Task.Delay(TimeSpan.FromSeconds(20), Workspace.OperationToken);
            validation = await service.ValidateAsync(standard, AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        }
        GuardContext(context);
        ShowValidation(validation, context);
        Outcome = created + "\n" + (validation.Ready
            ? "Both applications are set up and approved. Select Connect read-only now to assess the tenant."
            : Ready(SessionMode.Assessment)
                ? "The assessment application is ready: select Connect read-only now. The deployment application needs attention; see step 3."
                : "Setup finished, but the checks in step 3 need attention before you can connect. If Microsoft has not shown a grant yet, select Check again in a minute.");
    }

    private Task ValidateAsync() => Workspace.RunExclusiveAsync("Checking application configuration and granted permissions", async progress =>
    {
        var service = RequireSetup();
        Validations.Clear();
        var context = ValidationContext;
        var validation = await service.ValidateAsync(Workspace.RequireStandard(), AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        GuardContext(context);
        ShowValidation(validation, context);
        Outcome = validation.Ready ? "Configuration, consent and engineer assignment verified. Select Connect read-only now or Continue to deployment." : "Review what is missing in step 3. Approve each application's permissions under Existing applications and manual steps, or select Preview again to repair registration settings or assign this engineer.";
    });

    private Task OpenConsentAsync(SessionMode mode) => Workspace.RunExclusiveAsync("Waiting for administrator consent", async progress =>
    {
        var service = RequireSetup();
        var standard = Workspace.RequireStandard();
        var context = ValidationContext;
        Validations.Clear();
        var failure = await ApproveAsync(service, standard, mode, progress);
        if (failure is not null) { Outcome = failure; return; }
        GuardContext(context);
        if (!ProfileValidator.IsGuid(AssessmentClientId.Trim()) || !ProfileValidator.IsGuid(DeploymentClientId.Trim()))
        {
            Outcome = "Microsoft reported approval. Enter both client IDs, then select Check again to read the actual grants.";
            return;
        }
        progress.Report("Browser response received. Reading the actual application configuration and consent grants.");
        var validation = await service.ValidateAsync(standard, AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        GuardContext(context);
        ShowValidation(validation, context);
        Outcome = validation.Ready ? "Both applications are ready. Select Connect read-only now or Continue to deployment." : "Approval returned. Review the required, granted and missing permissions in step 3; approve the other application if needed, then select Check again.";
    });

    /// <summary>
    /// Opens Microsoft's approval for one application and waits for the browser to return. Returns why it did not
    /// complete, or null when Microsoft reported approval — which is not evidence of a grant: the caller reads the grants.
    /// </summary>
    private async Task<string?> ApproveAsync(ApplicationSetupService service, StandardCatalogue standard, SessionMode mode, IProgress<string> progress)
    {
        var name = Name(mode);
        var id = (mode == SessionMode.Assessment ? AssessmentClientId : DeploymentClientId).Trim();
        await service.ValidateConsentRedirectAsync(id, Workspace.OperationToken);
        using var callback = await OpenCallbackAsync(id, ApplicationSetupService.RequiredScopes(standard, mode), progress);
        Outcome = $"Microsoft's approval page for the {name} application is open in your browser. Sign in as an administrator if asked, check the permission list, select Accept, then return here. This is separate from the four temporary setup permissions.";
        progress.Report(Outcome);
        OpenBrowser(callback.ConsentUri.ToString());
        try
        {
            var response = await callback.WaitAsync(Workspace.OperationToken);
            return response.ApprovalReported ? null
                : $"Microsoft did not report approval for the {name} application. {response.Message} The account must be able to grant tenant-wide consent, and a newly created application can take a minute to appear to Microsoft. When ready, select Approve {name} permissions under Existing applications and manual steps; the applications do not need creating again.";
        }
        catch (TimeoutException)
        {
            return $"No answer came back from Microsoft's approval page for the {name} application within five minutes. Select Check again to read the actual grants; do not recreate the applications or assume approval failed.";
        }
        catch (OperationCanceledException) when (Workspace.OperationToken.IsCancellationRequested)
        {
            return $"Stopped waiting for the {name} approval. It may already have been given: select Check again to read the actual grants.";
        }
    }

    /// <summary>
    /// Opens the local approval callback. Its port is fixed by the registered redirect and bound exclusively, and Windows
    /// can hold it briefly after the previous approval's connections close. Waiting for it is local and has no tenant
    /// effect, so the second approval waits rather than stopping the sequence.
    /// </summary>
    private async Task<AdminConsentCallback> OpenCallbackAsync(string clientId, List<string> scopes, IProgress<string> progress)
    {
        var giveUp = DateTimeOffset.UtcNow.AddMinutes(4);
        while (true)
        {
            try { return AdminConsentCallback.Create(TenantId.Trim(), clientId, scopes); }
            catch (ConfigurationException ex) when (ex.InnerException is SocketException && DateTimeOffset.UtcNow < giveUp)
            {
                progress.Report("Waiting for Windows to release the local approval port. This can take a couple of minutes after the previous approval.");
                await Task.Delay(TimeSpan.FromSeconds(5), Workspace.OperationToken);
            }
        }
    }

    private void ShowValidation(ApplicationSetupValidation validation, string context)
    {
        Validations.Clear();
        foreach (var row in validation.Rows) Validations.Add(row);
        _validatedContext = context;
        OnPropertyChanged(nameof(NextStep));
    }
    private void GuardContext(string context)
    {
        if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed while this was running. Select Check again to validate the current IDs.");
    }
    private void ApplyIds()
    {
        RequireSetup();
        if (_validatedContext != ValidationContext || Validations.Count != 2 || Validations.Any(v => !v.ConfigurationValid))
            throw new ConfigurationException("Validate these application IDs against the current tenant and standard first.");
        Shell.Page<ConnectViewModel>().UseApplicationIds(TenantId.Trim(), AssessmentClientId.Trim(), DeploymentClientId.Trim());
        Shell.Navigate("connect");
    }
    private bool Ready(SessionMode mode, bool requireDirectAssignment = true) => _validatedContext == ValidationContext
        && Validations.Any(v => v.Mode == mode && v.ConfigurationValid && v.ConsentComplete && (!requireDirectAssignment || v.EngineerAssignmentConfirmed));
    private async Task ContinueAsync(SessionMode mode, bool checkDelegatedAdmin = false)
    {
        RequireSetup();
        if (!Ready(mode, !checkDelegatedAdmin)) throw new ConfigurationException("Validate this application's configuration, consent and engineer assignment first.");
        var connect = Shell.Page<ConnectViewModel>();
        connect.UseApplicationIds(TenantId.Trim(), AssessmentClientId.Trim(), DeploymentClientId.Trim());
        await connect.ConnectAsync(mode);
    }
    private static string Name(SessionMode mode) => mode == SessionMode.Assessment ? "assessment" : "deployment";
    private static void OpenBrowser(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    private string ValidationContext => $"{TenantId.Trim()}|{Workspace.ApplicationSetup?.Identity.TenantId}|{Workspace.ApplicationSetup?.Identity.AccountObjectId}|{Workspace.Standard?.IntegrityDigest}|{AssessmentClientId}|{DeploymentClientId}";
    public override void Refresh()
    {
        if (Validations.Count > 0 && _validatedContext != ValidationContext) Validations.Clear();
        OnPropertyChanged(nameof(SetupIdentity)); OnPropertyChanged(nameof(PlanSummary)); OnPropertyChanged(nameof(NextStep));
    }
}
