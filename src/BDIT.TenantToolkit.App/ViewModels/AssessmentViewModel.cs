using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Checks;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class FindingRow
{
    public ControlFinding Finding { get; init; } = new();
    public string Area { get; init; } = "";
    public string ControlId => Finding.ControlId;
    public string Name => Finding.Name;
    public string Category => Finding.Category;
    public string Severity => Finding.Severity;
    public string Status => StatusLabels.For(Finding.Status);
    public string StatusKey => Finding.Status.ToString();
    public string Reason => Finding.Reason;
    public string BestMatch => Finding.BestCandidate is { } c ? $"{c.Name} ({StatusLabels.For(c.Enforcement)})" : "";
}

/// <summary>A choice in a filter list: <see cref="Key"/> is compared, <see cref="Label"/> is shown and announced.</summary>
public sealed record FilterOption(string Key, string Label)
{
    public override string ToString() => Label;
}

public sealed class AssessmentViewModel : PageViewModel
{
    private string _filterStatus = "Actionable";
    private string _filterCategory = "All";
    private string _filterArea = "All areas";
    private string _search = "";
    private FindingRow? _selected;
    private CandidateMatch? _selectedCandidate;
    private string _lastExport = "";
    private string _lastExportFile = "";
    private string _checkArea = "Entra";
    private string? _checkControl;
    private bool _checkStored;
    private ScopedCheckEvidence? _partialCheck;
    private string _partialFile = "";

    public AssessmentViewModel(ShellViewModel shell) : base(shell, "Assessment")
    {
        ReassessCommand = Sync(Workspace.RunAssessment, () => Workspace.Snapshot is not null && Workspace.Idle);
        ExportHtmlCommand = Command(() => Export(ExportFormat.Html), () => Workspace.Assessment is not null);
        ExportMarkdownCommand = Command(() => Export(ExportFormat.Markdown), () => Workspace.Assessment is not null);
        ExportJsonCommand = Command(() => Export(ExportFormat.Json), () => Workspace.Assessment is not null);
        ExportCsvCommand = Command(() => Export(ExportFormat.Csv), () => Workspace.Assessment is not null);
        ExportXlsxCommand = Command(() => Export(ExportFormat.Xlsx), () => Workspace.Assessment is not null);
        ExportClientCommand = Command(() => Export(ExportFormat.ClientHtml), () => Workspace.Assessment is not null);
        CopyDetailCommand = CopyText(() => SelectedDetail + Environment.NewLine + string.Join(Environment.NewLine, Notes));
        OpenExportCommand = Sync(() => Infrastructure.ShellFolders.RevealFile(_lastExportFile), () => _lastExportFile.Length > 0);
        CheckAreaCommand = Command(() => RunCheck(false), () => CanCheck && AreaAvailable);
        CheckControlCommand = Command(() => RunCheck(true), () => CanCheck && CheckControls.Any(c => c.Key == CheckControl));
        CopyPartialCommand = CopyText(() => PartialCheckText);
        ExportUpgradeImpactCommand = Command(ExportUpgradeImpact,
            () => Workspace.Idle && Workspace.Snapshot is not null && Workspace.Profile is not null && Workspace.Standard is not null && CompareRelease is not null);
        Refresh();
    }

    public ICommand ReassessCommand { get; }
    public ICommand ExportHtmlCommand { get; }
    public ICommand ExportMarkdownCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportXlsxCommand { get; }
    public ICommand ExportClientCommand { get; }
    public ICommand CopyDetailCommand { get; }
    public ICommand OpenExportCommand { get; }
    public ICommand CheckAreaCommand { get; }
    public ICommand CheckControlCommand { get; }
    public ICommand CopyPartialCommand { get; }
    public IReadOnlyList<string> CheckAreas => CheckSelection.Areas;
    public ObservableCollection<FilterOption> CheckControls { get; } = new();
    public string CheckArea { get => _checkArea; set { if (SetProperty(ref _checkArea, value)) OnPropertyChanged(nameof(CheckGuidance)); } }
    public string? CheckControl { get => _checkControl; set => SetProperty(ref _checkControl, value); }
    public bool CheckStored { get => _checkStored; set { if (SetProperty(ref _checkStored, value)) OnPropertyChanged(nameof(CheckGuidance)); } }
    private bool CanCheck => Workspace.Idle && Workspace.Profile is not null && Workspace.Standard is not null
        && (CheckStored ? Workspace.Snapshot is not null : Workspace.Session is { TenantVerified: true });
    private bool AreaAvailable => Workspace.Standard is { } standard && Workspace.Profile is { } profile
        && ControlInstances.All(standard, profile).Any(c => ControlAreas.For(c) == CheckArea);
    public string CheckGuidance => !Workspace.Idle ? "Wait for the current operation to finish."
        : Workspace.Profile is null || Workspace.Standard is null ? "Select a saved client and standard first."
        : CheckControls.Count == 0 ? "This standard has no requirements to check. Choose another verified standard."
        : !AreaAvailable ? "This standard has no requirements in that area. Choose another area, or check an individual requirement."
        : CheckStored ? Workspace.Snapshot is null ? "Open a stored configuration first, or untick stored evidence and connect."
            : "Stored evidence only: this filters the original capture; it does not refresh tenant data."
        : Workspace.Session is not { TenantVerified: true } ? "Connect to the selected tenant, or choose stored evidence."
        : "Read-only live check: reads only the selected requirements' dependencies, using the current connection.";
    public string PartialCheckText => _partialCheck is not { } e ? "No partial check yet. Choose an area or requirement, then Check this. Partial checks cannot authorise deployment."
        : $"PARTIAL CHECK · {e.SourceMode} · captured {e.Capture.CapturedAt}\nNot complete before-evidence; the full assessment and plan are unchanged.\n"
            + string.Join("\n", e.Assessment.Findings.Select(f => $"{f.Name} [{f.ControlId}]: {StatusLabels.For(f.Status)} — {f.Reason}"))
            + "\n" + string.Join("\n", e.Assessment.Limitations) + "\nEvidence: " + _partialFile;

    private async Task RunCheck(bool control)
    {
        var catalogue = Workspace.RequireStandard();
        var profile = Workspace.Profile ?? throw new ToolkitException("Select a client first.");
        var selection = control ? CheckSelection.ForControl(catalogue, profile, CheckControl ?? "")
            : CheckSelection.ForArea(catalogue, profile, CheckArea);
        var result = await Workspace.RunScopedCheckAsync(selection, CheckStored);
        _partialCheck = result.Evidence; _partialFile = result.File;
        OnPropertyChanged(nameof(PartialCheckText));
    }

    public ObservableCollection<FindingRow> Findings { get; } = new();
    /// <summary>
    /// The result filter, worded as the findings table words each status. The key is what the filter compares; the
    /// label is what the engineer reads and what a screen reader announces, so neither sees an internal enum name.
    /// </summary>
    public IReadOnlyList<FilterOption> StatusFilters { get; } = new[] { new FilterOption("Actionable", "Actionable"), new FilterOption("All", "All results") }
        .Concat(Enum.GetValues<FindingStatus>().Select(s => new FilterOption(s.ToString(), StatusLabels.For(s)))).ToList();
    public ObservableCollection<string> CategoryFilters { get; } = new();
    public IReadOnlyList<string> AreaFilters => ControlAreas.Filters;
    public ObservableCollection<CandidateMatch> Candidates { get; } = new();
    public ObservableCollection<PropertyDifference> Differences { get; } = new();
    public ObservableCollection<string> Limitations { get; } = new();
    public ObservableCollection<string> Notes { get; } = new();

    private readonly List<FindingRow> _all = new();

    public string FilterStatus { get => _filterStatus; set { if (SetProperty(ref _filterStatus, value)) ApplyFilter(); } }
    public string FilterCategory { get => _filterCategory; set { if (SetProperty(ref _filterCategory, value)) ApplyFilter(); } }
    public string FilterArea { get => _filterArea; set { if (SetProperty(ref _filterArea, value)) ApplyFilter(); } }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }
    public string LastExport { get => _lastExport; private set => SetProperty(ref _lastExport, value); }

    // ---- upgrade impact (INT-051) -----------------------------------------------------------------------------------

    public ICommand ExportUpgradeImpactCommand { get; }
    /// <summary>Releases other than the loaded one, to compare the current capture against.</summary>
    public ObservableCollection<string> CompareReleases { get; } = new();
    private string? _compareRelease;
    public string? CompareRelease { get => _compareRelease; set => SetProperty(ref _compareRelease, value); }
    public string CompareGuidance => Workspace.Standard is null ? "Load a standard first."
        : $"Shows how each requirement changes between the release you choose and {Workspace.Standard.Release}, for the capture in hand. It reassesses that one capture under both releases; nothing in the tenant or in this workspace's records changes.";

    private async Task ExportUpgradeImpact()
    {
        var target = Workspace.RequireStandard();
        var snapshot = Workspace.Snapshot ?? throw new ToolkitException("Capture or open a configuration first.");
        var profile = Workspace.Profile ?? throw new ToolkitException("Select a client first.");
        var choice = Workspace.Releases.FirstOrDefault(r => r.Release == CompareRelease) ?? throw new ToolkitException("Choose a release to compare with.");
        _lastExportFile = await Workspace.ExportAsync(() =>
        {
            var source = Workspace.Standards.Load(choice.FileName);
            var standards = Workspace.Paths.StandardsDirectory;
            var lineage = BDIT.TenantToolkit.Engine.Standards.ReleaseLineage.Load(standards, BDIT.TenantToolkit.Engine.Standards.StandardsManifest.Load(standards), target.Release);
            var report = BDIT.TenantToolkit.Engine.Assessment.UpgradeImpactAnalyser.Analyse(Workspace.Engine, snapshot, source, target, lineage, profile,
                Workspace.Evidence.LoadMappings(profile.TenantId), Workspace.Evidence.LoadDeviations(profile.TenantId), DateTimeOffset.UtcNow);
            return Workspace.Exporter.ExportUpgradeImpact(report, ExportFormat.Html);
        }, "Writing upgrade impact", $"Assessing the capture under {choice.Release} and {target.Release}.");
        LastExport = $"Upgrade impact {choice.Release} → {target.Release} written: {_lastExportFile}";
        RaiseAll();
    }

    public FindingRow? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            Candidates.Clear();
            Notes.Clear();
            if (value is not null)
            {
                foreach (var c in value.Finding.Candidates) Candidates.Add(c);
                foreach (var n in value.Finding.Notes) Notes.Add(n);
                if (value.Finding.Deviation is { } d) Notes.Add($"Deviation ({d.Kind}): {d.Reason} - approved by {d.ApprovedBy}, review by {(d.ReviewBy.Length == 0 ? "not set" : d.ReviewBy)}");
                foreach (var o in value.Finding.ObservedObjects) Notes.Add("Observed: " + o);
            }
            SelectedCandidate = Candidates.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDetail));
            OnPropertyChanged(nameof(ProposedJson));
        }
    }

    public CandidateMatch? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            SetProperty(ref _selectedCandidate, value);
            Differences.Clear();
            if (value is not null) foreach (var d in value.Differences) Differences.Add(d);
            OnPropertyChanged(nameof(CandidateText));
        }
    }

    public string CandidateText => SelectedCandidate is null ? "" : $"{SelectedCandidate.Name} [{SelectedCandidate.ObjectId}] · {StatusLabels.For(SelectedCandidate.Enforcement)} · {SelectedCandidate.AssignmentSummary} · {SelectedCandidate.Matched}/{SelectedCandidate.Total} settings match{(SelectedCandidate.ToolkitManaged ? " · created by the toolkit" : "")}";

    public string SelectedDetail
    {
        get
        {
            var f = Selected?.Finding;
            if (f is null) return "Select a finding to see the reason, matching objects and property-level differences.";
            var lines = new List<string>
            {
                $"{f.ControlId} {f.Name} · {f.Category} · severity {f.Severity}",
                "Status: " + StatusLabels.For(f.Status),
                "Finding: " + f.Reason,
                "Desired state: " + f.DesiredState
            };
            if (f.ExpectedProductionState.Length > 0) lines.Add($"Expected production state: {f.ExpectedProductionState} · {f.ExpectedProductionAssignment}");
            if (f.BusinessImpact.Length > 0) lines.Add("Business impact: " + f.BusinessImpact);
            if (f.EngineerAction.Length > 0) lines.Add("Engineer action: " + f.EngineerAction);
            if (f.ManualInstructions.Length > 0) lines.Add("How to review: " + f.ManualInstructions);
            if (f.Owned)
            {
                var owned = f.Candidates.FirstOrDefault(c => string.Equals(c.ObjectId, f.OwnedObjectId, StringComparison.OrdinalIgnoreCase))?.Name;
                lines.Add("Toolkit-managed object: " + (string.IsNullOrWhiteSpace(owned) ? f.OwnedObjectId : $"{owned} [{f.OwnedObjectId}]"));
            }
            foreach (var e in f.Equivalence)
            {
                lines.Add("");
                lines.Add($"Equivalent configuration test - {e.Name} [{e.ObjectId}] · {StatusLabels.For(e.Enforcement)} · {(e.Covered ? "satisfies every required condition" : "partial")}");
                foreach (var s in e.Signals)
                    lines.Add($"   [{(s.Matched ? "met" : "not met")}] {s.Label} ({s.Path}) · expected {s.Expected} · observed {s.Observed}");
                foreach (var caveat in e.Caveats) lines.Add("   Caveat: " + caveat);
            }
            return string.Join(Environment.NewLine, lines);
        }
    }

    public string ProposedJson => Selected?.Finding.Proposed?.ToJsonString(ToolkitJson.Options) ?? "";

    public string SummaryText
    {
        get
        {
            var a = Workspace.Assessment;
            if (a is null) return Workspace.Snapshot is null ? "Read the tenant configuration to run an assessment." : "No assessment yet.";
            var s = a.Summary;
            return $"{a.TenantName} · standard {a.Release} · assessed {a.AssessedAt} · snapshot {(a.SnapshotComplete ? "complete" : "INCOMPLETE")} · evidence integrity {MarkdownReports.IntegrityText(a.SnapshotIntegrity)}\n" +
                   $"Compliant {s.Compliant} (+{s.CompliantWithDeviation} with deviation) · Match not enforced {s.SettingsMatchNotEnforced} · Partial {s.PartialMatch} · Missing {s.Missing} · Manual review {s.RequiresManualReview} · Unable to assess {s.UnableToAssess} · Licence {s.LicenceUnavailable} · N/A {s.NotApplicable}\n" +
                   $"Actionable: {s.CriticalActionable} critical, {s.HighActionable} high, {s.MediumActionable} medium, {s.LowActionable} low.";
        }
    }

    private async Task Export(ExportFormat format)
    {
        var a = Workspace.Assessment ?? throw new ToolkitException("Run an assessment first.");
        _lastExportFile = await Workspace.ExportAsync(() => Workspace.Exporter.ExportAssessment(a, format));
        LastExport = "Exported: " + _lastExportFile;
        RaiseAll();
    }

    public override void Refresh()
    {
        var selectedCheck = CheckControl;
        CheckControls.Clear();
        if (Workspace.Standard is { } checkStandard && Workspace.Profile is { } checkProfile)
            foreach (var c in ControlInstances.All(checkStandard, checkProfile).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                CheckControls.Add(new(c.Id, c.Name + " [" + c.Id + "]"));
        CheckControl = CheckControls.Any(c => c.Key == selectedCheck) ? selectedCheck : CheckControls.FirstOrDefault()?.Key;
        if (_partialCheck is { } partial && (Workspace.Profile is not { } current || Workspace.Standard is not { } catalogue
            || partial.TenantId != current.TenantId || partial.ClientScopeDigest != ReviewedClientScope.Digest(current)
            || partial.CatalogueDigest != catalogue.IntegrityDigest))
        { _partialCheck = null; _partialFile = ""; }
        OnPropertyChanged(nameof(CheckGuidance));
        OnPropertyChanged(nameof(PartialCheckText));
        var keep = CompareRelease;
        CompareReleases.Clear();
        foreach (var r in Workspace.Releases.Where(r => !string.Equals(r.Release, Workspace.Standard?.Release, StringComparison.OrdinalIgnoreCase))) CompareReleases.Add(r.Release);
        CompareRelease = keep is not null && CompareReleases.Contains(keep) ? keep : null;
        OnPropertyChanged(nameof(CompareGuidance));
        _all.Clear();
        CategoryFilters.Clear();
        CategoryFilters.Add("All");
        Limitations.Clear();
        var a = Workspace.Assessment;
        if (a is not null)
        {
            var controls = Workspace.Standard is { } standard ? ControlInstances.All(standard, Workspace.Profile).ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase) : new();
            foreach (var f in a.Findings) _all.Add(new FindingRow { Finding = f, Area = ControlAreas.For(controls.GetValueOrDefault(f.ControlId) ?? new ControlDefinition { Id = f.ControlId }) });
            foreach (var c in a.Findings.Select(f => f.Category).Distinct().OrderBy(c => c, StringComparer.OrdinalIgnoreCase)) CategoryFilters.Add(c);
            foreach (var l in a.Limitations) Limitations.Add(l);
        }
        if (!CategoryFilters.Contains(_filterCategory)) _filterCategory = "All";
        ApplyFilter();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(FilterCategory));
    }

    private void ApplyFilter()
    {
        Findings.Clear();
        var q = Search.Trim();
        foreach (var row in _all.OrderBy(r => r.Finding.IsActionable ? 0 : 1).ThenBy(r => HtmlReports.SeverityRank(r.Severity)).ThenBy(r => r.ControlId, StringComparer.OrdinalIgnoreCase))
        {
            if (FilterStatus == "Actionable" && !row.Finding.IsActionable) continue;
            if (FilterStatus != "All" && FilterStatus != "Actionable" && row.StatusKey != FilterStatus) continue;
            if (FilterCategory != "All" && row.Category != FilterCategory) continue;
            if (FilterArea != "All areas" && row.Area != FilterArea) continue;
            if (q.Length > 0 && !row.ControlId.Contains(q, StringComparison.OrdinalIgnoreCase) && !row.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !row.Reason.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Findings.Add(row);
        }
        if (Selected is not null && !Findings.Contains(Selected)) Selected = null;
    }
}
