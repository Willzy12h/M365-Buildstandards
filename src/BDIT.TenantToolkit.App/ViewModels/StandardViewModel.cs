using System.Collections.ObjectModel;
using System.Windows.Input;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Reports;
using BDIT.TenantToolkit.Engine.Standards;

namespace BDIT.TenantToolkit.App.ViewModels;

public sealed class StandardViewModel : PageViewModel
{
    private StandardRelease? _selectedRelease;
    private ControlDefinition? _selected;
    private string _search = "";
    private string _category = "All";
    private string _clientName = "";
    private string _lastExport = "";

    public StandardViewModel(ShellViewModel shell) : base(shell, "Build Standard")
    {
        SelectReleaseCommand = Sync(() => { if (SelectedRelease is not null) Workspace.SelectRelease(SelectedRelease.FileName); }, () => SelectedRelease is not null && Workspace.Idle);
        ExportDocumentCommand = Command(() => ExportDocument(ExportFormat.Html), () => Workspace.Standard is not null && Workspace.Idle);
        ExportDocumentMarkdownCommand = Command(() => ExportDocument(ExportFormat.Markdown), () => Workspace.Standard is not null && Workspace.Idle);
        ExportEngineerHtmlCommand = Command(() => ExportEngineer(EngineerDocumentKind.BuildStandard, ExportFormat.Html), () => Workspace.Standard is not null && Workspace.Idle);
        ExportEngineerMarkdownCommand = Command(() => ExportEngineer(EngineerDocumentKind.BuildStandard, ExportFormat.Markdown), () => Workspace.Standard is not null && Workspace.Idle);
        ExportManualHtmlCommand = Command(() => ExportEngineer(EngineerDocumentKind.ManualGuide, ExportFormat.Html), () => ManualGuideAvailable && Workspace.Idle);
        ExportManualMarkdownCommand = Command(() => ExportEngineer(EngineerDocumentKind.ManualGuide, ExportFormat.Markdown), () => ManualGuideAvailable && Workspace.Idle);
        Refresh();
    }

    public ICommand SelectReleaseCommand { get; }
    public ICommand ExportDocumentCommand { get; }
    public ICommand ExportDocumentMarkdownCommand { get; }
    public ICommand ExportEngineerHtmlCommand { get; }
    public ICommand ExportEngineerMarkdownCommand { get; }
    public ICommand ExportManualHtmlCommand { get; }
    public ICommand ExportManualMarkdownCommand { get; }
    public bool ManualGuideAvailable => Workspace.Standard is { Controls.Count: > 0 } standard && standard.Controls.All(EngineerStandardDocuments.HasCompleteManual);
    public string EngineerDocumentHint => ManualGuideAvailable
        ? "Both engineer documents include every control in the loaded catalogue. They contain settings and named inputs, with no client profile or tenant evidence."
        : "The Build Standard exports for this release. Select 2026.09.12 for the full manual guide; older catalogues do not contain complete manual sections.";
    public ObservableCollection<StandardRelease> Releases => Workspace.Releases;
    public ObservableCollection<ControlDefinition> Controls { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public StandardRelease? SelectedRelease { get => _selectedRelease; set => SetProperty(ref _selectedRelease, value); }

    /// <summary>Name the document is prepared for. Defaults to the selected client so the common case needs no typing.</summary>
    public string ClientName
    {
        get => _clientName.Length > 0 ? _clientName : Workspace.Profile?.Company ?? "";
        set => SetProperty(ref _clientName, value);
    }

    public string LastExport { get => _lastExport; private set => SetProperty(ref _lastExport, value); }

    /// <summary>
    /// Writes the proposed catalogue for the loaded release; tenant assessment and deployment evidence are separate.
    /// </summary>
    private async Task ExportDocument(ExportFormat format)
    {
        var standard = Workspace.RequireStandard();
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportBuildStandard(standard, ClientName, DateTimeOffset.UtcNow, format));
        LastExport = "Build standard document written: " + file;
    }
    private async Task ExportEngineer(EngineerDocumentKind kind, ExportFormat format)
    {
        var standard = Workspace.RequireStandard();
        var file = await Workspace.ExportAsync(() => Workspace.Exporter.ExportEngineerStandard(standard, kind, format));
        LastExport = EngineerStandardDocuments.Title(kind) + " written: " + file;
    }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) ApplyFilter(); } }
    public string Category { get => _category; set { if (SetProperty(ref _category, value)) ApplyFilter(); } }

    public ControlDefinition? Selected
    {
        get => _selected;
        set { SetProperty(ref _selected, value); OnPropertyChanged(nameof(Detail)); OnPropertyChanged(nameof(PayloadJson)); }
    }

    public string HeaderText => Workspace.Standard is null
        ? "No Build Standard loaded. " + (Workspace.StandardError ?? "")
        : $"{Workspace.Standard.Release} · {Workspace.Standard.Status} · {Workspace.Standard.Controls.Count} controls · {Workspace.Standard.Controls.Count(c => c.HasRecipe)} automated recipes · integrity digest {Workspace.Standard.IntegrityDigest} (SHA-256 manifest check; not a signature)";

    public string Detail
    {
        get
        {
            var c = Selected;
            if (c is null) return "Select a control.";
            var lines = new List<string>
            {
                $"{c.Id} {c.Name} · {c.Category} · severity {c.Severity}",
                "Purpose: " + c.Purpose,
                "Desired state: " + c.DesiredState,
                $"Expected production state: {c.ExpectedProduction.State} · {c.ExpectedProduction.Assignment}{(c.ExpectedProduction.Notes.Length > 0 ? " · " + c.ExpectedProduction.Notes : "")}",
                $"Toolkit safe deployment: {(c.HasRecipe ? c.SafeDeployment.State + " · " + c.SafeDeployment.Assignment : "not automated")}{(c.SafeDeployment.Notes.Length > 0 ? " · " + c.SafeDeployment.Notes : "")}",
                "Business impact: " + c.BusinessImpact,
                "Engineer action: " + c.EngineerAction,
                "Assessment: " + (c.HasRecipe ? "settings comparison" : "manual - " + c.Assessment.ManualInstructions),
                "Collection: " + (c.Collection ?? "none"),
                "Licence: " + (c.Licence.ServicePlans.Count == 0 ? "no specific service plan" : string.Join(", ", c.Licence.ServicePlans)) + (c.Licence.Note.Length > 0 ? " · " + c.Licence.Note : "")
            };
            if (c.Dependencies.Count > 0) lines.Add("Depends on: " + string.Join(", ", c.Dependencies));
            if (c.References.Microsoft.Length > 0) lines.Add("Microsoft: " + c.References.Microsoft);
            if (c.References.Cis.Length > 0) lines.Add("CIS: " + c.References.Cis);
            if (c.References.CyberEssentials.Length > 0) lines.Add("Cyber Essentials: " + c.References.CyberEssentials);
            if (c.DocumentationNotes.Length > 0) lines.Add("Notes: " + c.DocumentationNotes);
            return string.Join(Environment.NewLine, lines);
        }
    }

    public string PayloadJson => Selected?.Payload?.ToJsonString(ToolkitJson.Options) ?? "No automated recipe. {{placeholders}} are replaced with client profile values at assessment and planning time.";

    public override void Refresh()
    {
        if (SelectedRelease is null && Workspace.Standard is not null)
            _selectedRelease = Releases.FirstOrDefault(r => r.FileName == Workspace.Standard.SourceFileName);
        Categories.Clear();
        Categories.Add("All");
        if (Workspace.Standard is not null)
            foreach (var c in Workspace.Standard.Controls.Select(c => c.Category).Distinct()) Categories.Add(c);
        if (!Categories.Contains(_category)) _category = "All";
        ApplyFilter();
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(SelectedRelease));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(ManualGuideAvailable));
        OnPropertyChanged(nameof(EngineerDocumentHint));
    }

    private void ApplyFilter()
    {
        var selectedId = Selected?.Id;
        Controls.Clear();
        if (Workspace.Standard is null) { Selected = null; return; }
        var q = Search.Trim();
        foreach (var c in Workspace.Standard.Controls)
        {
            if (Category != "All" && c.Category != Category) continue;
            if (q.Length > 0 && !c.Id.Contains(q, StringComparison.OrdinalIgnoreCase) && !c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && !c.Purpose.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Controls.Add(c);
        }
        Selected = Controls.FirstOrDefault(c => c.Id == selectedId);
    }
}
