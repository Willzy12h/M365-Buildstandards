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
    private string _tenantId = "", _namePrefix = "BDIT", _confirmation = "", _assessmentId = "", _deploymentId = "", _outcome = "No application setup has run.";
    private bool _setupApproved, _permissionsApproved;
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
        DisconnectCommand = Command(Workspace.DisconnectApplicationSetupAsync, () => Workspace.Idle && Workspace.ApplicationSetup is not null);
        OpenEntraCommand = Sync(() => OpenBrowser("https://entra.microsoft.com/"));
    }
    public string TenantId { get => _tenantId; set { if (SetProperty(ref _tenantId, value)) InvalidatePlan(); } }
    public string NamePrefix { get => _namePrefix; set { if (SetProperty(ref _namePrefix, value)) InvalidatePlan(); } }
    public string Confirmation { get => _confirmation; set => SetProperty(ref _confirmation, value); }
    public bool SetupApproved { get => _setupApproved; set => SetProperty(ref _setupApproved, value); }
    public bool PermissionsApproved { get => _permissionsApproved; set => SetProperty(ref _permissionsApproved, value); }
    public string AssessmentClientId { get => _assessmentId; set { if (SetProperty(ref _assessmentId, value)) Validations.Clear(); } }
    public string DeploymentClientId { get => _deploymentId; set { if (SetProperty(ref _deploymentId, value)) Validations.Clear(); } }
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
    public ICommand DisconnectCommand { get; }
    public ICommand OpenEntraCommand { get; }

    public void UseTenant(string tenantId, string company) { TenantId = tenantId; if (NamePrefix.Length == 0) NamePrefix = Workspace.Settings.CompanyName; }
    private ApplicationSetupService RequireSetup()
    {
        var setup = Workspace.ApplicationSetup ?? throw new ToolkitException("Sign in for application setup first.");
        if (!string.Equals(setup.Identity.TenantId, TenantId.Trim(), StringComparison.OrdinalIgnoreCase)) throw new TenantMismatchException("The setup session is connected to another tenant. Sign in again.");
        return setup;
    }
    private void InvalidatePlan() { _plan = null; PlanRows.Clear(); SelectedRow = null; PermissionsApproved = false; Confirmation = ""; Validations.Clear(); OnPropertyChanged(nameof(PlanSummary)); }
    private async Task ConnectAsync() { InvalidatePlan(); Validations.Clear(); await Workspace.ConnectApplicationSetupAsync(TenantId); }
    private Task PreviewAsync() => Workspace.RunExclusiveAsync("Previewing application registrations and permissions", async progress =>
    {
        InvalidatePlan();
        _plan = await RequireSetup().PreviewAsync(Workspace.RequireStandard(), NamePrefix, Workspace.OperationToken);
        foreach (var row in _plan.Rows) PlanRows.Add(row);
        SelectedRow = PlanRows.FirstOrDefault();
        OnPropertyChanged(nameof(PlanSummary));
        Outcome = "Preview only. Review both applications and permission descriptions before approving creation.";
    });
    private Task CreateAsync() => Workspace.RunExclusiveAsync("Creating approved application registrations", async progress =>
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
    });
    private Task ValidateAsync() => Workspace.RunExclusiveAsync("Checking application configuration and granted permissions", async progress =>
    {
        Validations.Clear();
        var context = ValidationContext;
        var validation = await RequireSetup().ValidateAsync(Workspace.RequireStandard(), AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
        if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed during validation. Validate the current IDs again.");
        foreach (var row in validation.Rows) Validations.Add(row);
        _validatedContext = context;
        Outcome = validation.Ready ? "Application configuration, consent and current engineer assignment verified. Reconnect with each application to test effective access." : "Setup requires attention. Review the issues below, complete consent or assignment, then validate again.";
    });
    private Task OpenConsentAsync(SessionMode mode) => Workspace.RunExclusiveAsync("Waiting for administrator consent", async progress =>
    {
        var service = RequireSetup();
        var standard = Workspace.RequireStandard();
        var context = ValidationContext;
        Validations.Clear();
        var id = mode == SessionMode.Assessment ? AssessmentClientId : DeploymentClientId;
        using var callback = AdminConsentCallback.Create(TenantId.Trim(), id.Trim(), ApplicationSetupService.RequiredScopes(standard, mode));
        progress.Report("Review Microsoft's administrator consent screen. The toolkit will validate the actual grants after you return.");
        OpenBrowser(callback.ConsentUri.ToString());
        var response = await callback.WaitAsync(Workspace.OperationToken);
        Outcome = response.Message;
        if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed while consent was open. Validate the current IDs again.");
        if (response.ApprovalReported && ProfileValidator.IsGuid(AssessmentClientId.Trim()) && ProfileValidator.IsGuid(DeploymentClientId.Trim()))
        {
            progress.Report("Browser response received. Reading the actual application configuration and consent grants.");
            var validation = await service.ValidateAsync(standard, AssessmentClientId.Trim(), DeploymentClientId.Trim(), Workspace.OperationToken);
            if (context != ValidationContext) throw new ConfigurationException("Setup inputs changed during grant verification. Validate again.");
            foreach (var row in validation.Rows) Validations.Add(row);
            _validatedContext = context;
            Outcome = validation.Ready ? "Configuration, consent and direct engineer assignment verified. Reconnect with each application to test effective access." : "Browser approval returned. Graph validation still requires attention; review the actual grant and assignment results below. Consent propagation can take time.";
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
    private static void OpenBrowser(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    private string ValidationContext => $"{TenantId.Trim()}|{Workspace.ApplicationSetup?.Identity.TenantId}|{Workspace.ApplicationSetup?.Identity.AccountObjectId}|{Workspace.Standard?.IntegrityDigest}|{AssessmentClientId}|{DeploymentClientId}";
    public override void Refresh()
    {
        if (Validations.Count > 0 && _validatedContext != ValidationContext) Validations.Clear();
        OnPropertyChanged(nameof(SetupIdentity)); OnPropertyChanged(nameof(PlanSummary));
    }
}
