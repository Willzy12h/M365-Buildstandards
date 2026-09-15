using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Prerequisites;
using BDIT.TenantToolkit.Engine.Standards;
using Microsoft.Win32;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class AutomationViewModel : PageViewModel
{
    private string _inputs = "{}", _importFile = "", _name = "", _replacements = "{}", _objectId = "", _include = "", _exclude = "", _typedTenant = "", _result = "", _tenant = "";
    private ControlDefinition? _control;
    private ReviewedChangeKind _kind = ReviewedChangeKind.SecureCompliance;
    private ReviewedChangePlan? _plan;
    private EntraLapsPlan? _lapsPlan;
    private bool _approved;
    private string _lastRunId = "", _lastLapsRunId = "";
    private string _devices = "";
    private string _packageFile = "", _lastPackageRunId = "";
    private string? _savedCandidate;
    public string? SavedCandidate { get => _savedCandidate; set => SetProperty(ref _savedCandidate, value); }
    public ObservableCollection<string> SavedCandidates { get; } = new();
    private PackagePublishPlan? _packagePlan;
    public string PackageFile { get => _packageFile; set { if (SetProperty(ref _packageFile, value)) ClearApproval(); } }
    public string DeviceIds { get => _devices; set { if (SetProperty(ref _devices, value)) ClearApproval(); } }
    public AutomationViewModel(ShellViewModel shell) : base(shell, "Policy automation")
    {
        SaveInputsCommand = Sync(SaveInputs, () => Workspace.Profile is not null && Workspace.Idle);
        ChooseImportCommand = Sync(ChooseImport, () => Workspace.Idle);
        ImportCommand = Sync(Import, () => SelectedControl is not null && Workspace.Profile is not null && Workspace.Idle && !Workspace.IsConnected);
        PreviewCommand = Command(Preview, () => Workspace.IsConnected && Workspace.Idle);
        ExecuteCommand = Command(Execute, () => Workspace.IsDeploymentSession && Workspace.Idle && _plan is not null && Approved);
        PreviewLapsCommand = Command(PreviewLaps, () => Workspace.IsConnected && Workspace.Idle);
        ExecuteLapsCommand = Command(ExecuteLaps, () => Workspace.IsDeploymentSession && Workspace.Idle && _lapsPlan is not null && Approved);
        ReverifyCommand = Command(Reverify, () => Workspace.IsConnected && Workspace.Idle && _lastRunId.Length > 0);
        ReverifyLapsCommand = Command(ReverifyLaps, () => Workspace.IsConnected && Workspace.Idle && _lastLapsRunId.Length > 0);
        CaptureCommand = Command(Workspace.CaptureAsync, () => Workspace.IsConnected && Workspace.Idle);
        ChoosePackageCommand = Sync(() => { var d = new OpenFileDialog { Filter = "Intune Windows package (*.intunewin)|*.intunewin" }; if (d.ShowDialog() == true) PackageFile = d.FileName; }, () => Workspace.Idle);
        PreviewPackageCommand = Command(PreviewPackage, () => Workspace.IsConnected && Workspace.Idle && SelectedControl is not null);
        PublishPackageCommand = Command(PublishPackage, () => Workspace.IsDeploymentSession && Workspace.Idle && _packagePlan is not null && Approved);
        ReverifyPackageCommand = Command(ReverifyPackage, () => Workspace.IsConnected && Workspace.Idle && _lastPackageRunId.Length > 0);
        CheckReadinessCommand = Command(CheckReadiness, () => Workspace.IsConnected && Workspace.Idle);
        LoadCandidateCommand = Sync(() => Workspace.LoadLocalCandidate(SavedCandidate!), () => SavedCandidate is not null && Workspace.Idle && !Workspace.IsConnected);
        Refresh();
    }
    public ObservableCollection<ControlDefinition> Controls { get; } = new();
    public IReadOnlyList<ReviewedChangeKind> Kinds { get; } = Enum.GetValues<ReviewedChangeKind>();
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
    public ControlDefinition? SelectedControl { get => _control; set { if (SetProperty(ref _control, value)) { ClearApproval(); OnPropertyChanged(nameof(Requirements)); } } }
    public ReviewedChangeKind Kind { get => _kind; set { if (SetProperty(ref _kind, value)) ClearApproval(); } }
    public string PolicyInputs { get => _inputs; set => SetProperty(ref _inputs, value); }
    public string ImportFile { get => _importFile; set => SetProperty(ref _importFile, value); }
    public string CandidateName { get => _name; set => SetProperty(ref _name, value); }
    public string Replacements { get => _replacements; set => SetProperty(ref _replacements, value); }
    public string ObjectId { get => _objectId; set { if (SetProperty(ref _objectId, value)) ClearApproval(); } }
    public string IncludedGroups { get => _include; set { if (SetProperty(ref _include, value)) ClearApproval(); } }
    public string ExcludedGroups { get => _exclude; set { if (SetProperty(ref _exclude, value)) ClearApproval(); } }
    public string TypedTenant { get => _typedTenant; set => SetProperty(ref _typedTenant, value); }
    public bool Approved { get => _approved; set => SetProperty(ref _approved, value); }
    public string Result { get => _result; private set => SetProperty(ref _result, value); }
    public string Requirements => SelectedControl is null ? "Select a control to see its required inputs." :
        SelectedControl.Id + " · " + SelectedControl.DesiredState + "\n" + SelectedControl.DocumentationNotes + "\n" +
        string.Join("\n", Workspace.RequireStandard().Parameters.Where(p => SelectedControl.Payload?.ToJsonString().Contains("{{" + p.Key + "}}", StringComparison.Ordinal) == true)
            .Select(p => p.Key + " (" + p.Type + "): " + p.Description));
    public string PreviewText => _plan is not null ? ToolkitJson.Serialize(_plan) : _lapsPlan is not null ? ToolkitJson.Serialize(_lapsPlan) : _packagePlan is not null ? ToolkitJson.Serialize(_packagePlan) : "Preview a change to inspect exact before/after settings, targets, tenant and consequences.";
    private void ClearApproval() { _plan = null; _lapsPlan = null; _packagePlan = null; Approved = false; TypedTenant = ""; OnPropertyChanged(nameof(PreviewText)); }
    private static IEnumerable<string> Ids(string text) => text.Split(new[] { ',', ';', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
    private void SaveInputs()
    {
        var profile = ToolkitJson.Deserialize<TenantProfile>(ToolkitJson.Serialize(Workspace.Profile!));
        var values = ToolkitJson.ParseObject(PolicyInputs);
        foreach (var (key, _) in values)
            if (!Workspace.RequireStandard().Parameters.Any(p => p.Key == key) || key is "emergencyAccountIds" or "officeLocationId" or "mamGroupId" or "pilotGroupId" or "caExclusionGroupId" or "tenantId")
                throw new ConfigurationException("Unknown or reserved policy input: " + key);
        profile.Parameters.PolicyInputs = values.ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.Ordinal);
        Workspace.SaveProfile(profile); Workspace.InvalidatePolicyState(); ClearApproval();
        Result = "Policy inputs saved to the tenant profile. Capture and review a new plan.";
    }
    private void ChooseImport()
    {
        var dialog = new OpenFileDialog { Filter = "Graph policy JSON (*.json)|*.json", Title = "Select a policy export" };
        if (dialog.ShowDialog() == true) ImportFile = dialog.FileName;
    }
    private void Import()
    {
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
                SelectedControl?.Id ?? Kind.ToString(), ObjectId.Trim(), Ids(IncludedGroups), Ids(ExcludedGroups), Workspace.OperationToken, Ids(DeviceIds));
        });
        OnPropertyChanged(nameof(PreviewText));
    }
    private async Task Execute()
    {
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
        var p = _lapsPlan ?? throw new ToolkitException("Preview LAPS first.");
        if (TypedTenant.Trim() != p.TenantId) throw new SafetyViolationException("Type the exact tenant ID from the preview.");
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
            PolicyInputs = ToolkitJson.Serialize(Workspace.Profile?.Parameters.PolicyInputs ?? new());
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
        OnPropertyChanged(nameof(SelectedControl)); OnPropertyChanged(nameof(Requirements));
    }
}
