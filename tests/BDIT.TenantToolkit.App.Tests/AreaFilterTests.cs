using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class AreaFilterTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;

    public AreaFilterTests()
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.12.json"));
        _root.WriteStandard("2026.09.12.json", File.ReadAllText(source));
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        Assert.NotNull(workspace.Standard);
        _shell = new ShellViewModel(workspace);
    }

    private void SetFindings(IEnumerable<ControlFinding> findings)
    {
        typeof(Workspace).GetProperty(nameof(Workspace.Assessment))!.SetValue(_shell.Workspace,
            new AssessmentResult { Findings = findings.ToList() });
    }

    [Fact]
    public void Assessment_combines_area_status_category_and_search_without_altering_evidence()
    {
        var controls = _shell.Workspace.RequireStandard().Controls;
        controls.Add(new ControlDefinition { Id = "EX-TEST", Area = "Exchange" });
        controls.Add(new ControlDefinition { Id = "PUR-TEST", Area = "Purview" });
        SetFindings(new[] {
            new ControlFinding { ControlId = "CA-001", Name = "Entra candidate", Category = "Policy", Status = FindingStatus.Missing },
            new ControlFinding { ControlId = "APP-IOS-001", Name = "Mobile candidate", Category = "App", Status = FindingStatus.Missing },
            new ControlFinding { ControlId = "EX-TEST", Name = "Exchange unknown", Category = "Mail", Status = FindingStatus.UnableToAssess },
            new ControlFinding { ControlId = "PUR-TEST", Name = "Audit review", Category = "Audit", Status = FindingStatus.RequiresManualReview }
        });
        var vm = _shell.Page<AssessmentViewModel>(); vm.Refresh(); vm.FilterStatus = "All";
        Assert.Equal(new[] { "All areas", "Entra", "Intune", "Exchange", "Purview" }, vm.AreaFilters);
        Assert.Equal(4, vm.Findings.Count);
        vm.FilterArea = "Exchange";
        Assert.Equal("EX-TEST", Assert.Single(vm.Findings).ControlId);
        vm.FilterStatus = nameof(FindingStatus.Missing); Assert.Empty(vm.Findings);
        vm.FilterStatus = "All"; vm.FilterCategory = "App"; Assert.Empty(vm.Findings);
        vm.FilterCategory = "All"; vm.Search = "not found"; Assert.Empty(vm.Findings);
        vm.Search = "unknown"; Assert.Single(vm.Findings);
        Assert.Equal(4, _shell.Workspace.Assessment!.Findings.Count);
        Assert.Null(_shell.Workspace.Plan);
    }

    [Fact]
    public void Plan_selects_only_visible_eligible_controls_and_clears_selection_on_area_change()
    {
        SetFindings(_shell.Workspace.RequireStandard().Controls.Select(c => new ControlFinding { ControlId = c.Id, Status = FindingStatus.Missing }));
        var vm = _shell.Page<PlanViewModel>(); vm.Refresh();
        vm.FilterArea = "Intune"; vm.SelectEligibleCommand.Execute(null);
        Assert.Contains(vm.Controls, c => c.IsSelected);
        Assert.All(vm.Controls.Where(c => c.IsSelected), c => { Assert.Equal("Intune", c.Area); Assert.True(c.Eligible); });
        Assert.All(vm.VisibleControls, c => Assert.Equal("Intune", c.Area));
        vm.Refresh(); Assert.Contains(vm.Controls, c => c.IsSelected);
        vm.FilterArea = "Entra"; Assert.DoesNotContain(vm.Controls, c => c.IsSelected);
        vm.SelectEligibleCommand.Execute(null);
        Assert.Contains(vm.Controls, c => c.IsSelected);
        Assert.All(vm.Controls.Where(c => c.IsSelected), c => Assert.Equal("Entra", c.Area));
        vm.FilterArea = "All areas"; Assert.Equal(vm.Controls.Count, vm.VisibleControls.Count);
        Assert.DoesNotContain(vm.Controls, c => c.IsSelected);
    }

    [Fact]
    public void Expanded_offices_keep_plan_guidance_and_separate_manual_check_records()
    {
        var profile = TestData.Profile();
        profile.Parameters.OfficeLocations = new() {
            new() { Key = "NORTH", Name = "Synthetic North", IpRanges = new() { "8.8.8.0/24" } },
            new() { Key = "SOUTH", Name = "Synthetic South", IpRanges = new() { "1.1.1.0/24" } }
        };
        _shell.Workspace.ApplyProfileToSession(profile, save: false);
        var plan = _shell.Page<PlanViewModel>(); plan.FilterArea = "Entra";
        plan.SelectedControl = plan.VisibleControls.Single(c => c.ControlId == "PRE-008-NORTH");
        Assert.NotNull(plan.SelectionPrerequisites);
        var checks = _shell.Page<ManualChecksViewModel>();
        Assert.Contains(checks.Rows, c => c.ControlId == "PRE-008-NORTH");
        Assert.Contains(checks.Rows, c => c.ControlId == "PRE-008-SOUTH");
        Assert.DoesNotContain(checks.Rows, c => c.ControlId == "PRE-008");
        _shell.Workspace.SaveManualCheck("PRE-008-NORTH", "Unknown", "Synthetic test evidence only.");
        Assert.Equal("Unknown", _shell.Workspace.LoadManualChecks().Checks["PRE-008-NORTH"].Status);
        Assert.False(_shell.Workspace.LoadManualChecks().Checks.ContainsKey("PRE-008-SOUTH"));
    }

    [Theory]
    [InlineData("CA-001", "Entra")]
    [InlineData("PRE-008", "Entra")]
    [InlineData("CFG-WIN-001", "Intune")]
    [InlineData("EX-001", "Exchange")]
    [InlineData("PUR-001", "Purview")]
    public void Older_catalogues_have_a_presentation_only_area_fallback(string id, string area) =>
        Assert.Equal(area, ControlAreas.For(new ControlDefinition { Id = id }));

    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
