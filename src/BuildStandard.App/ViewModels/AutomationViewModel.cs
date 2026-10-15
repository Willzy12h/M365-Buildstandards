using System.Text.Json.Nodes;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Execution;
using BuildStandard.Engine.Planning;
using BuildStandard.Engine.Prerequisites;
using BuildStandard.Engine.Standards;
using Microsoft.Win32;
using BuildStandard.Core.Safety;

namespace BuildStandard.App.ViewModels;

public sealed class AutomationViewModel : PageViewModel
{
    private string _inputs = "{}", _importFile = "", _name = "", _replacements = "{}", _objectId = "", _include = "", _exclude = "", _typedTenant = "", _result = "", _tenant = "";
    private ControlDefinition? _control;
    private ReviewedChangeKind _kind = ReviewedChangeKind.SecureCompliance;
    private AssignmentPopulation _population = AssignmentPopulation.Groups;
    private string _inputContext = "";
    private ReviewedChangePlan? _plan;
    private EntraLapsPlan? _lapsPlan;
    private bool _approved;
    private string _lastRunId = "", _lastLapsRunId = "";
    private string _devices = "";
    public OfficeLocationEditor Offices { get; }
    private OfficeLocationRow? _selectedOffice;
    public OfficeLocationRow? SelectedOffice { get => _selectedOffice; set { SetProperty(ref _selectedOffice, value); CommandManager.InvalidateRequerySuggested(); } }
    // Compatibility for existing profile/input harness callers; the native editor uses typed rows.
    public string OfficeLocations { get => OfficeLocationValidator.Render(Offices.Rows.Select(r => r.Value())); set { Offices.Load(OfficeLocationValidator.ParseLines(value)); OnPropertyChanged(); } }
    public ICommand AddOfficeCommand { get; }
    public ICommand RemoveOfficeCommand { get; }
    private string _packageFile = "", _lastPackageRunId = "";
    private string? _savedCandidate;
    public string? SavedCandidate { get => _savedCandidate; set => SetProperty(ref _savedCandidate, value); }
    public ObservableCollection<string> SavedCandidates { get; } = new();
    private PackagePublishPlan? _packagePlan;
    public string PackageFile { get => _packageFile; set { if (SetProperty(ref _packageFile, value)) ClearApproval(); } }
    public string DeviceIds { get => _devices; set { if (SetProperty(ref _devices, value)) ClearApproval(); } }
    public AutomationViewModel(ShellViewModel shell) : base(shell, "Policy automation")
    {
        Offices = new OfficeLocationEditor(() => { ClearApproval(); OnPropertyChanged(nameof(OfficeLocations)); });
        AddOfficeCommand = Sync(() => { if (!Workspace.Idle || Workspace.Profile is null) return; Offices.Add(); SelectedOffice = Offices.Rows.Last(); }, () => Workspace.Profile is not null && Workspace.Idle && Offices.Rows.Count < 100);
        RemoveOfficeCommand = Sync(() => { if (!Workspace.Idle || Workspace.Profile is null || SelectedOffice is null) return; Offices.Remove(SelectedOffice); SelectedOffice = null; }, () => Workspace.Profile is not null && Workspace.Idle && SelectedOffice is not null);
        SaveInputsCommand = Sync(SaveInputs, () => Workspace.Profile is not null && Workspace.Idle);
        ChooseImportCommand = Sync(ChooseImport, () => Workspace.Idle);
        ImportCommand = Sync(Import, () => SelectedControl is not null && Workspace.Profile is not null && Workspace.Idle && !Workspace.IsConnected);
        PreviewCommand = Command(Preview, () => Workspace.IsConnected && Workspace.Idle);
        ExecuteCommand = Command(Execute, () => Workspace.IsDeploymentSession && Workspace.ExperimentalChangesEnabled && Workspace.Idle && _plan is not null && Approved);
        PreviewLapsCommand = Command(PreviewLaps, () => Workspace.IsConnected && Workspace.Idle);
        ExecuteLapsCommand = Command(ExecuteLaps, () => Workspace.IsDeploymentSession && Workspace.ExperimentalChangesEnabled && Workspace.Idle && _lapsPlan is not null && Approved);
        ReverifyCommand = Command(Reverify, () => Workspace.IsConnected && Workspace.Idle && _lastRunId.Length > 0);
        ReverifyLapsCommand = Command(ReverifyLaps, () => Workspace.IsConnected && Workspace.Idle && _lastLapsRunId.Length > 0);
        CaptureCommand = Command(Workspace.CaptureAsync, () => Workspace.IsConnected && Workspace.Idle);
        ChoosePackageCommand = Sync(() => { var d = new OpenFileDialog { Filter = "Intune Windows package (*.intunewin)|*.intunewin" }; if (d.ShowDialog() == true) PackageFile = d.FileName; }, () => Workspace.Idle);
        PreviewPackageCommand = Command(PreviewPackage, () => Workspace.IsConnected && Workspace.Idle && SelectedControl is not null);
        PublishPackageCommand = Command(PublishPackage, () => Workspace.IsDeploymentSession && Workspace.ExperimentalChangesEnabled && Workspace.Idle && _packagePlan is not null && Approved);
        ReverifyPackageCommand = Command(ReverifyPackage, () => Workspace.IsConnected && Workspace.Idle && _lastPackageRunId.Length > 0);
        CheckReadinessCommand = Command(CheckReadiness, () => Workspace.IsConnected && Workspace.Idle);
        LoadCandidateCommand = Sync(() => Workspace.LoadLocalCandidate(SavedCandidate!), () => SavedCandidate is not null && Workspace.Idle && !Workspace.IsConnected);
        Refresh();
    }
    public ObservableCollection<ControlDefinition> Controls { get; } = new();

    /// <summary>
    /// One labelled field per client input the standard declares, generated from its parameter list so the page stays
    /// correct when the standard gains an input. The JSON box remains for imports and paste, but nothing requires it.
    /// </summary>
    public ObservableCollection<PolicyInputField> InputFields { get; } = new();
    private bool _showAllInputs;
    public bool ShowAllInputs { get => _showAllInputs; set { if (SetProperty(ref _showAllInputs, value)) RefreshInputScope(); } }
    public IReadOnlyList<PolicyInputField> VisibleInputFields
    {
        get
        {
            var keys = PolicyInputScope.Keys(Workspace.Standard?.Parameters ?? [], SelectedControl, ShowAllInputs);
            return InputFields.Where(f => keys.Contains(f.Key)).ToList();
        }
    }
    public bool ShowOfficeEditor => ShowAllInputs || SelectedControl?.RepeatFor == "officeLocations";
    public string InputScopeGuidance => ShowAllInputs ? "Showing all client inputs. Hidden/revealed fields retain their values; saving validates the whole profile."
        : SelectedControl is null ? "Choose a control above to see its required inputs, or show all client inputs."
        : $"Inputs for {SelectedControl.Name}. Use Show all client inputs to edit other values or resolve an error outside this control. Office rows are shown for the office-location control.";
    public string ImportGuidance => !Workspace.Idle ? "Wait for the current task before changing the local candidate."
        : Workspace.Profile is null ? "Select a client first. Imports stay in that client's local data folder."
        : Workspace.IsConnected ? "Disconnect before importing or loading a candidate; then reconnect, capture and review it."
        : "Choose a supported control and a policy JSON export, resolve foreign IDs and validate the local candidate. Nothing is deployed by importing.";
    public string PreviewGuidance => !Workspace.Idle ? "Wait for the current task to finish." : !Workspace.IsConnected ? "Connect to the intended client first; preview reads state and sends no changes."
        : "Choose the intended action and supply its inputs. Preview reads the current state; it does not apply the change.";
    public string ApplyGuidance => !Workspace.Idle ? "Wait for the active operation to finish; do not repeat a write."
        : !Workspace.IsDeploymentSession ? "Connect with deployment access, then prepare a fresh exact preview."
        : !Workspace.ExperimentalChangesEnabled ? Workspace.ExperimentalChangeGuidance
        : _plan is null && _lapsPlan is null && _packagePlan is null ? "Prepare a fresh exact preview first."
        : !Approved ? "Read the exact preview, then tick its approval box. The target-tenant confirmation still applies."
        : "Confirm the target tenant from this exact preview. Final freshness, ownership and unresolved-write checks still apply.";
    private void RefreshInputScope()
    {
        OnPropertyChanged(nameof(VisibleInputFields)); OnPropertyChanged(nameof(ShowOfficeEditor)); OnPropertyChanged(nameof(InputScopeGuidance));
    }

    public IReadOnlyList<ReviewedChangeKind> Kinds { get; } = Enum.GetValues<ReviewedChangeKind>();
    public IReadOnlyList<AssignmentPopulation> Populations { get; } = Enum.GetValues<AssignmentPopulation>();
    public AssignmentPopulation SelectedPopulation { get => _population; set { if (SetProperty(ref _population, value)) ClearApproval(); } }
    public bool IsAssignmentAction => Kind == ReviewedChangeKind.AssignGroups;
    public ICommand SaveInputsCommand { get; }
    public ICommand ChooseImportCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ExecuteCommand { get; }
    public ICommand PreviewLapsCommand { get; }
    public ICommand ExecuteLapsCommand { get; }
    public ICommand ReverifyCommand { get; }
    public ICommand ReverifyLapsCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand ChoosePackageCommand { get; }
    public ICommand PreviewPackageCommand { get; }
    public ICommand PublishPackageCommand { get; }
    public ICommand ReverifyPackageCommand { get; }
    public ICommand CheckReadinessCommand { get; }
    public ICommand LoadCandidateCommand { get; }
    public ObservableCollection<ServiceReadiness> Readiness { get; } = new();
    public ControlDefinition? SelectedControl { get => _control; set { if (SetProperty(ref _control, value)) { ClearApproval(); OnPropertyChanged(nameof(Requirements)); RefreshInputScope(); } } }
    public ReviewedChangeKind Kind { get => _kind; set { if (SetProperty(ref _kind, value)) { ClearApproval(); OnPropertyChanged(nameof(IsAssignmentAction)); } } }
    public string PolicyInputs { get => _inputs; set => SetProperty(ref _inputs, value); }
    public string ImportFile { get => _importFile; set => SetProperty(ref _importFile, value); }
    public string CandidateName { get => _name; set => SetProperty(ref _name, value); }
    public string Replacements { get => _replacements; set => SetProperty(ref _replacements, value); }
    public string ObjectId { get => _objectId; set { if (SetProperty(ref _objectId, value)) ClearApproval(); } }
    public string IncludedGroups { get => _include; set { if (SetProperty(ref _include, value)) ClearApproval(); } }
    public string ExcludedGroups { get => _exclude; set { if (SetProperty(ref _exclude, value)) ClearApproval(); } }
    public string TypedTenant { get => _typedTenant; set => SetProperty(ref _typedTenant, value); }
    public bool Approved { get => _approved; set { if (SetProperty(ref _approved, value)) OnPropertyChanged(nameof(ApplyGuidance)); } }
    public string Result { get => _result; private set => SetProperty(ref _result, value); }
    public string Requirements
    {
        get
        {
            if (SelectedControl is null) return "Select a control to see its required inputs.";
            // A property read re-serialised the payload once per parameter on every change notification.
            var used = ParameterUsage.Keys(SelectedControl.Payload);
            var inputs = Workspace.RequireStandard().Parameters
                .Where(p => used.Contains(p.Key) || p.RequiredForControls?.Contains(SelectedControl.Id) == true)
                .Select(p => p.Key + " (" + p.Type + "): " + p.Description);
            return SelectedControl.Id + " · " + SelectedControl.DesiredState + "\n" + SelectedControl.DocumentationNotes + "\n"
                + string.Join("\n", inputs);
        }
    }
    public string PreviewText => _plan is not null ? ToolkitJson.Serialize(_plan) : _lapsPlan is not null ? ToolkitJson.Serialize(_lapsPlan) : _packagePlan is not null ? ToolkitJson.Serialize(_packagePlan) : "Preview a change to inspect exact before/after settings, targets, tenant and consequences.";
    private void ClearApproval() { _plan = null; _lapsPlan = null; _packagePlan = null; Approved = false; TypedTenant = ""; OnPropertyChanged(nameof(PreviewText)); OnPropertyChanged(nameof(ApplyGuidance)); }
    private static IEnumerable<string> Ids(string text) => text.Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    private void SaveInputs()
    {
        var profile = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(Workspace.Profile!));
        var edits = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

        foreach (var field in InputFields)
        {
            if (Reserved(field.Key)) continue;
            var parameter = Workspace.RequireStandard().Parameters.Single(p => p.Key == field.Key);
            var required = parameter.RequiredForControls?.Contains(SelectedControl?.Id ?? "") == true
                || parameter.Required && !parameter.HasDefault && ParameterUsage.Keys(SelectedControl?.Payload).Contains(field.Key);
            edits[field.Key] = field.TryRead(out var node, required) ? node : null;
        }

        var problems = InputFields.Where(f => f.HasProblem).Select(f => f.Label + ": " + f.Problem).ToList();
        if (problems.Count > 0)
            throw new ConfigurationException("Correct these inputs and save again.\n" + string.Join("\n", problems));

        var values = PolicyInputParser.MergeInputs(profile.Parameters.PolicyInputs ?? new Dictionary<string, JsonNode?>(), edits);
        profile.Parameters.PolicyInputs = values;
        if (Workspace.RequireStandard().SchemaVersion >= 5)
        {
            profile.Parameters.OfficeLocations = Offices.Read();
            if (SelectedControl?.RepeatFor == "officeLocations" && profile.Parameters.OfficeLocations.Count == 0)
                throw new ConfigurationException("Add a named office with at least one public CIDR range before saving this control.");
        }
        Workspace.SaveProfile(profile); Workspace.InvalidatePolicyState(); ClearApproval();
        PolicyInputs = ToolkitJson.Serialize(values);
        BuildInputFields();
        _inputContext = InputContext();
        var supplied = values.Count;
        var missing = InputFields.Count(f => f.IsRequired && f.Value.Trim().Length == 0);
        Result = $"Saved {supplied} client input(s) to the tenant profile."
            + (missing > 0 ? $" {missing} required input(s) are still empty; the controls that use them cannot be created yet." : "")
            + " Capture and review a new plan.";
    }

    /// <summary>Identity inputs are held on the profile itself and set through the exclusion workflow, not here.</summary>
    private static bool Reserved(string key) =>
        key is "emergencyAccountIds" or "emergencyAndGuestIds" or "officeLocationId" or "mamGroupId"
            or "pilotGroupId" or "caExclusionGroupId" or "tenantId";

    private void BuildInputFields()
    {
        InputFields.Clear();
        var standard = Workspace.Standard;
        if (standard is null) return;
        var current = Workspace.Profile?.Parameters.PolicyInputs ?? new();
        var now = DateTimeOffset.UtcNow;
        foreach (var parameter in standard.Parameters.Where(p => !Reserved(p.Key)))
        {
            current.TryGetValue(parameter.Key, out var value);
            InputFields.Add(new PolicyInputField(parameter, value, now));
        }
    }

    // Tenant ID alone does not identify a form: switching release or reloading a profile can change its fields and values.
    private string InputContext() => CanonicalJson.Sha256Value(new
    {
        Catalogue = Workspace.Standard?.Parameters,
        Release = Workspace.Standard?.Release,
        Profile = Workspace.Profile
    });

    private void ChooseImport()
    {
        var dialog = new OpenFileDialog { Filter = "Graph policy JSON (*.json)|*.json", Title = "Select a policy export" };
        if (dialog.ShowDialog() == true) ImportFile = dialog.FileName;
    }
    private void Import()
    {
        if (string.IsNullOrWhiteSpace(ImportFile)) throw new ConfigurationException("Choose a policy export file first.");
        var file = new FileInfo(ImportFile);
        if (!file.Exists || file.Length > 1024 * 1024) throw new ConfigurationException("Select a policy JSON file up to 1 MiB.");
        var map = ToolkitJson.Deserialize<Dictionary<string, string>>(Replacements);
        var import = PolicyImporter.Import(Workspace.RequireStandard(), SelectedControl!.Id, File.ReadAllText(file.FullName), CandidateName, map);
        Workspace.UseImportedStandard(import); ClearApproval();
        Result = "Local candidate loaded: " + import.Standard.Release + ". Removed metadata: " + string.Join(", ", import.RemovedProperties) + ". Reconnect, capture and review its settings before creating.";
    }
    private async Task Preview()
    {
        ClearApproval(); Result = "";
        await Workspace.RunExclusiveAsync("Previewing reviewed policy change", async _ =>
        {
            var c = Workspace.RequireConnection();
            _plan = await new ReviewedChangeService(Workspace.Evidence, SystemClock.Instance).PreviewAsync(c.Graph, c.Session, Workspace.Profile!,
                Workspace.RequireStandard(), Workspace.Snapshot ?? throw new ToolkitException("Capture tenant configuration first."), Kind,
                SelectedControl?.Id ?? Kind.ToString(), ObjectId.Trim(), Ids(IncludedGroups), Ids(ExcludedGroups), Workspace.OperationToken, Ids(DeviceIds),
                population: IsAssignmentAction ? SelectedPopulation : AssignmentPopulation.Groups);
        });
        OnPropertyChanged(nameof(PreviewText));
    }
    private async Task Execute()
    {
        Workspace.RequireExperimentalChanges(BuildStandard.App.Services.ExperimentalOperation.ReviewedChange);
        var p = _plan ?? throw new ToolkitException("Preview first."); var typed = TypedTenant; ClearApproval();
        await Workspace.RunExclusiveAsync("Applying the approved policy change", async _ =>
        {
            Workspace.InvalidatePolicyState(); var c = Workspace.RequireConnection();
            var run = await new ReviewedChangeService(Workspace.Evidence, SystemClock.Instance).ExecuteAsync(c.Graph, c.Session, Workspace.Profile!,
                Workspace.RequireStandard(), p.Id, p.IntegrityDigest, typed, Workspace.OperationToken);
            _lastRunId = run.Id; Result = ToolkitJson.Serialize(run);
        });
    }
    private async Task PreviewLaps()
    {
        ClearApproval();
        await Workspace.RunExclusiveAsync("Previewing Entra LAPS enablement", async _ =>
        {
            var c = Workspace.RequireConnection();
            _lapsPlan = await new EntraLapsService(Workspace.Evidence, SystemClock.Instance).PreviewAsync(c.Graph, c.Session,
                Workspace.RequireStandard(), Workspace.Snapshot ?? throw new ToolkitException("Capture tenant configuration first."), Workspace.OperationToken);
        });
        OnPropertyChanged(nameof(PreviewText));
    }
    private async Task ExecuteLaps()
    {
        Workspace.RequireExperimentalChanges(BuildStandard.App.Services.ExperimentalOperation.EntraLaps);
        var p = _lapsPlan ?? throw new ToolkitException("Preview LAPS first.");
        if (!BuildStandard.Core.Safety.TenantConfirmation.Matches(TypedTenant, p.TenantId)) throw new SafetyViolationException("Type the exact tenant ID from the preview.");
        ClearApproval();
        await Workspace.RunExclusiveAsync("Enabling reviewed Entra LAPS prerequisite", async _ =>
        {
            Workspace.InvalidatePolicyState(); var c = Workspace.RequireConnection();
            var run = await new EntraLapsService(Workspace.Evidence, SystemClock.Instance).ExecuteAsync(c.Graph, c.Session,
                Workspace.RequireStandard(), p.Id, p.IntegrityDigest, Workspace.OperationToken);
            _lastLapsRunId = run.Id; Result = ToolkitJson.Serialize(run);
        });
    }
    private Task Reverify() => Workspace.RunExclusiveAsync("Re-verifying policy change (read only)", async _ =>
    {
        var c = Workspace.RequireConnection();
        Result = ToolkitJson.Serialize(await new ReviewedChangeService(Workspace.Evidence, SystemClock.Instance).ReverifyAsync(c.Graph, c.Session, Workspace.RequireStandard(), _lastRunId, Workspace.OperationToken));
    });
    private Task ReverifyLaps() => Workspace.RunExclusiveAsync("Re-verifying Entra LAPS (read only)", async _ =>
    {
        var c = Workspace.RequireConnection();
        Result = ToolkitJson.Serialize(await new EntraLapsService(Workspace.Evidence, SystemClock.Instance).ReverifyAsync(c.Graph, c.Session, _lastLapsRunId, Workspace.OperationToken));
    });
    private async Task PreviewPackage()
    {
        ClearApproval();
        await Workspace.RunExclusiveAsync("Inspecting encrypted package and previewing publication", async _ =>
        {
            var c = Workspace.RequireConnection();
            _packagePlan = await new ApplicationPackageService(Workspace.Evidence, SystemClock.Instance).PreviewAsync(c.Graph, c.Session,
                Workspace.RequireStandard(), Workspace.Snapshot ?? throw new ToolkitException("Capture tenant configuration first."),
                SelectedControl!.Id, PackageFile, Workspace.OperationToken);
        });
        OnPropertyChanged(nameof(PreviewText));
    }
    private async Task PublishPackage()
    {
        Workspace.RequireExperimentalChanges(BuildStandard.App.Services.ExperimentalOperation.PublishPackage);
        var p = _packagePlan ?? throw new ToolkitException("Preview the package first."); var typed = TypedTenant; var file = PackageFile; ClearApproval();
        await Workspace.RunExclusiveAsync("Publishing the reviewed application package", async _ =>
        {
            Workspace.InvalidatePolicyState(); var c = Workspace.RequireConnection();
            var run = await new ApplicationPackageService(Workspace.Evidence, SystemClock.Instance).ExecuteAsync(c.Graph, c.Session, Workspace.RequireStandard(),
                p.Id, p.IntegrityDigest, typed, file, Workspace.OperationToken);
            _lastPackageRunId = run.Id; Result = ToolkitJson.Serialize(run);
        });
    }
    private Task ReverifyPackage() => Workspace.RunExclusiveAsync("Re-verifying package publication (read only)", async _ =>
    {
        var c = Workspace.RequireConnection();
        var pass = await new ApplicationPackageService(Workspace.Evidence, SystemClock.Instance).ReverifyAsync(c.Graph, c.Session, Workspace.RequireStandard(), _lastPackageRunId, Workspace.OperationToken);
        Result = pass ? "Package content version is published and the app remains unassigned." : "Package publication is still incomplete. No write was repeated.";
    });
    private Task CheckReadiness() => Workspace.RunExclusiveAsync("Checking identity and external-service readiness", async _ =>
    {
        var c = Workspace.RequireConnection();
        var rows = await new ServiceReadinessService().CheckAsync(c.Graph, Workspace.Profile!, Workspace.OperationToken);
        Readiness.Clear(); foreach (var row in rows) Readiness.Add(row);
        Workspace.Evidence.WriteJsonAtomic(Path.Combine(Workspace.Paths.TenantDirectory(c.Session.TenantId), "readiness", Guid.NewGuid() + ".json"),
            new { c.Session.TenantId, operatorId = c.Session.OperatorObjectId, checkedAt = DateTimeOffset.UtcNow, results = rows });
    });
    public override void Refresh()
    {
        var tenant = Workspace.Profile?.TenantId ?? "";
        if (_tenant != tenant)
        {
            _tenant = tenant; ClearApproval(); Result = ""; Readiness.Clear();
            _lastRunId = _lastLapsRunId = _lastPackageRunId = "";
            try
            {
                _lastRunId = tenant.Length == 0 ? "" : Workspace.Evidence.LoadReviewedChangeRuns(tenant).FirstOrDefault(r => r.WriteAcceptance == WriteAcceptance.Accepted)?.Id ?? "";
                _lastLapsRunId = tenant.Length == 0 ? "" : Workspace.Evidence.LoadEntraLapsRuns(tenant).FirstOrDefault(r => r.WriteAcceptance == WriteAcceptance.Accepted)?.Id ?? "";
                _lastPackageRunId = tenant.Length == 0 ? "" : Workspace.Evidence.LoadPackageRuns(tenant).FirstOrDefault()?.Id ?? "";
            }
            catch (Exception ex)
            {
                _lastRunId = _lastLapsRunId = _lastPackageRunId = "";
                Result = "Local automation evidence could not be loaded. Keep the evidence files and review Activity before continuing.";
                Workspace.Logger.Error("Automation", Result, ex, tenant);
            }
        }
        var inputContext = InputContext();
        if (_inputContext != inputContext)
        {
            _inputContext = inputContext;
            PolicyInputs = ToolkitJson.Serialize(Workspace.Profile?.Parameters.PolicyInputs ?? new());
            Offices.Load(Workspace.Profile?.Parameters.OfficeLocations); SelectedOffice = null; OnPropertyChanged(nameof(OfficeLocations));
            BuildInputFields();
            ClearApproval();
        }
        if (!Workspace.IsConnected) ClearApproval();
        var local = Workspace.LocalCandidates();
        if (!SavedCandidates.SequenceEqual(local))
        {
            SavedCandidates.Clear(); foreach (var candidate in local) SavedCandidates.Add(candidate);
            SavedCandidate = SavedCandidates.FirstOrDefault();
        }
        var current = Workspace.Standard?.Controls ?? new();
        if (!Controls.SequenceEqual(current))
        {
            var selectedId = _control?.Id; Controls.Clear();
            foreach (var control in current) Controls.Add(control);
            _control = Controls.FirstOrDefault(c => c.Id == selectedId) ?? Controls.FirstOrDefault();
        }
        OnPropertyChanged(nameof(SelectedControl)); OnPropertyChanged(nameof(Requirements)); RefreshInputScope();
        OnPropertyChanged(nameof(ImportGuidance)); OnPropertyChanged(nameof(PreviewGuidance)); OnPropertyChanged(nameof(ApplyGuidance));
    }
}
