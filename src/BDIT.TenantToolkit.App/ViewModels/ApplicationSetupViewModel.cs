using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph.Setup;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class ApplicationSetupViewModel : PageViewModel
{
    private string _tenantId = "", _namePrefix = "M365 BuildStandard", _confirmation = "", _assessmentId = "", _deploymentId = "", _outcome = "Preview your new or existing tool registrations, approve setup, then grant each app its listed permissions.";
    private bool _setupApproved, _permissionsApproved, _assignOperator;
    private ApplicationSetupPlan? _plan;
    private ApplicationSetupRow? _selectedRow;
    private string _validatedContext = "";
    public ApplicationSetupViewModel(ShellViewModel shell) : base(shell, "Application setup")
    {
        ConnectCommand = Command(ConnectAsync, () => Workspace.Idle && SetupApproved);
        PreviewCommand = Command(PreviewAsync, () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        CreateCommand = Command(CreateAsync, () => Workspace.Idle && _plan is not null && PermissionsApproved);
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
    }
    public string TenantId { get => _tenantId; set { if (SetProperty(ref _tenantId, value)) InvalidatePlan(); } }
    public string NamePrefix { get => _namePrefix; set { if (SetProperty(ref _namePrefix, value)) InvalidatePlan(); } }
    public string Confirmation { get => _confirmation; set => SetProperty(ref _confirmation, value); }
    public bool SetupApproved { get => _setupApproved; set => SetProperty(ref _setupApproved, value); }
    public bool PermissionsApproved { get => _permissionsApproved; set => SetProperty(ref _permissionsApproved, value); }
    public bool AssignOperator { get => _assignOperator; set { if (SetProperty(ref _assignOperator, value)) InvalidatePlan(); } }
    public bool UseSystemBrowser { get => Workspace.Settings.UseSystemBrowser; set { Workspace.Settings.UseSystemBrowser = value; OnPropertyChanged(); } }
    public string AssessmentClientId { get => _assessmentId; set { if (SetProperty(ref _assessmentId, value)) InvalidatePlan(); } }
    public string DeploymentClientId { get => _deploymentId; set { if (SetProperty(ref _deploymentId, value)) InvalidatePlan(); } }
    public string Outcome { get => _outcome; private set => SetProperty(ref _outcome, value); }
    public string SetupIdentity => Workspace.ApplicationSetup is { } setup ? $"SETUP WRITE ACCESS · {setup.Identity.Account}\nTenant {setup.Identity.TenantId}" : "Setup session disconnected. Normal assessment access cannot create applications.";
    public string PlanSummary => _plan is null ? "Preview first to resolve current Microsoft Graph permissions and detect existing applications." : $"{_plan.TenantName} · {_plan.TenantId}\nOperator {_plan.OperatorName} · Standard {_plan.StandardRelease}\nPlan {_plan.Id} · expires five minutes after {_plan.CreatedAt:u}";
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

    public void UseTenant(string tenantId, string company) { TenantId = tenantId; }
    private ApplicationSetupService RequireSetup()
    {
        var setup = Workspace.ApplicationSetup ?? throw new ToolkitException("Sign in for application setup first.");
        if (!string.Equals(setup.Identity.TenantId, TenantId.Trim(), StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("The setup session is connected to another tenant. Sign in again.");
        return setup;
    }
    private void InvalidatePlan() { _plan = null; PlanRows.Clear(); SelectedRow = null; PermissionsApproved = false; Confirmation = ""; Validations.Clear(); OnPropertyChanged(nameof(PlanSummary)); }
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
        Outcome = "Preview only. Review registration changes, permission descriptions and the engineer-assignment choice before approval.";
    });
    private Task CreateAsync() => Workspace.RunExclusiveAsync("Applying approved application setup", async progress =>
    {
        var plan = _plan ?? throw new PlanValidationException("Preview application setup again.");
        var service = RequireSetup();
        Results.Clear();
        var result = await service.ExecuteAsync(plan, Workspace.RequireStandard(), Confirmation, PermissionsApproved,
            Path.Combine(Workspace.Paths.TenantDirectory(service.Identity.TenantId), "application-setup"), Workspace.OperationToken);
        foreach (var row in result.Rows) Results.Add(row);
        var assessmentId = result.Rows.FirstOrDefault(r => r.Mode == SessionMode.Assessment)?.ClientId;
        var deploymentId = result.Rows.FirstOrDefault(r => r.Mode == SessionMode.Deployment)?.ClientId;
        if (!string.IsNullOrWhiteSpace(assessmentId)) AssessmentClientId = assessmentId;
        if (!string.IsNullOrWhiteSpace(deploymentId)) DeploymentClientId = deploymentId;
        Outcome = $"{result.Status}. After-change evidence: {(result.AfterComplete ? "captured" : "incomplete — " + result.AfterError)}.\nEvidence: {result.EvidenceDirectory}\n{result.NextSteps}";
        InvalidatePlan();
        if (ProfileValidator.IsGuid(AssessmentClientId) && ProfileValidator.IsGuid(DeploymentClientId))
        {
            Shell.Page<ConnectViewModel>().UseApplicationIds(TenantId.Trim(), AssessmentClientId, DeploymentClientId);
            var context = ValidationContext;
            var validation = await service.ValidateAsync(Workspace.RequireStandard(), AssessmentClientId, DeploymentClientId, Workspace.OperationToken);
            if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed during validation; validate again.");
            foreach (var row in validation.Rows) Validations.Add(row);
            _validatedContext = context;
        }
    });
    private Task ValidateAsync() => Workspace.RunExclusiveAsync("Checking application configuration and granted permissions", async progress =>
    {
        Validations.Clear();
        var context = ValidationContext;
        var validation = await RequireSetup().ValidateAsync(Workspace.RequireStandard(), AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed during validation. Validate the current IDs again.");
        foreach (var row in validation.Rows) Validations.Add(row);
        _validatedContext = context;
        Outcome = validation.Ready ? "Configuration, consent and engineer assignment verified. Choose Continue read-only or Continue to deployment below." : "Review missing permissions below. Approve each application's permissions; use Preview setup to repair registration settings or assign this engineer.";
    });
    private Task OpenConsentAsync(SessionMode mode) => Workspace.RunExclusiveAsync("Waiting for administrator consent", async progress =>
    {
        var service = RequireSetup();
        var standard = Workspace.RequireStandard();
        var context = ValidationContext;
        Validations.Clear();
        var id = mode == SessionMode.Assessment ? AssessmentClientId : DeploymentClientId;
        await service.ValidateConsentRedirectAsync(id.Trim(), Workspace.OperationToken);
        using var callback = AdminConsentCallback.Create(TenantId.Trim(), id.Trim(), ApplicationSetupService.RequiredScopes(standard, mode));
        Outcome = $"Approve the {mode} application's full permission list in Microsoft. This is separate from the temporary setup account's four permissions. Return here when the browser says approval was received.";
        progress.Report(Outcome);
        OpenBrowser(callback.ConsentUri.ToString());
        AdminConsentCallbackResult response;
        try { response = await callback.WaitAsync(Workspace.OperationToken); }
        catch (TimeoutException)
        {
            Outcome = "The browser callback timed out. Select Validate setup to read actual grants; do not recreate the applications or infer consent failure from the missing callback.";
            return;
        }
        catch (OperationCanceledException) when (Workspace.OperationToken.IsCancellationRequested)
        {
            Outcome = "Stopped waiting for the browser. Consent may already exist. Select Validate setup to check the actual grants.";
            return;
        }
        Outcome = response.Message;
        if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed while consent was open. Validate the current IDs again.");
        if (response.ApprovalReported && ProfileValidator.IsGuid(AssessmentClientId.Trim()) && ProfileValidator.IsGuid(DeploymentClientId.Trim()))
        {
            progress.Report("Browser response received. Reading the actual application configuration and consent grants.");
            var validation = await service.ValidateAsync(standard, AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
            if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed during grant verification. Validate again.");
            foreach (var row in validation.Rows) Validations.Add(row);
            _validatedContext = context;
            Outcome = validation.Ready ? "Both applications are ready. Continue directly into read-only assessment or deployment below." : "Approval returned. Review the required, granted and missing scopes below; approve the other application if needed, then validate again.";
        }
    });
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
    private static void OpenBrowser(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    private string ValidationContext => $"{TenantId.Trim()}|{Workspace.ApplicationSetup?.Identity.TenantId}|{Workspace.ApplicationSetup?.Identity.AccountObjectId}|{Workspace.Standard?.IntegrityDigest}|{AssessmentClientId}|{DeploymentClientId}";
    public override void Refresh()
    {
        if (Validations.Count > 0 && _validatedContext != ValidationContext) Validations.Clear();
        OnPropertyChanged(nameof(SetupIdentity)); OnPropertyChanged(nameof(PlanSummary));
    }
}
