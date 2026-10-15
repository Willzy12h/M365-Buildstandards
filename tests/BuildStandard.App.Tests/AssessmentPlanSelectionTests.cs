using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Graph;
using Xunit;

namespace BuildStandard.Tests;
public sealed class AssessmentPlanSelectionTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly FakeGraphClient _graph;
    private readonly AssessmentViewModel _page;
    public AssessmentPlanSelectionTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        _log = new(_root.Paths.LogsDirectory, LogLevel.Debug); _workspace = new(_root.Paths, new ToolkitSettings(), _log, true); _workspace.Initialise();
        _workspace.ApplyProfileToSession(TestData.Profile(), save: false);
        _graph = new(_workspace.RequireStandard()) { Mode = SessionMode.Assessment };
        Set(nameof(Workspace.Connection), new ConnectedTenant(TestData.Session(mode: SessionMode.Assessment), _graph, null));
        var snapshot = TestData.Snapshot(_workspace.RequireStandard(), capturedAt: DateTimeOffset.UtcNow);
        _workspace.Evidence.SaveSnapshot(snapshot); Set(nameof(Workspace.Snapshot), snapshot); Set(nameof(Workspace.SnapshotIsLive), true);
        _workspace.RunAssessment(); _shell = new(_workspace); _page = _shell.Page<AssessmentViewModel>(); _page.FilterStatus = "All";
        _page.Selected = _page.Findings.Single(x => x.ControlId == "CA-001");
    }
    private void Set(string name, object? value) => typeof(Workspace).GetProperty(name)!.SetValue(_workspace, value);
    [Fact]
    public void Add_selects_exact_control_and_preserves_other_choices_without_build_or_approval()
    {
        var plan = _shell.Page<PlanViewModel>(); plan.FilterArea = "Intune";
        plan.Controls.Single(x => x.ControlId == "CMP-WIN-001").IsSelected = true;
        Assert.True(_page.CanAddToPlan); _page.AddToPlanCommand.Execute(null);
        Assert.Same(plan, _shell.CurrentPage); Assert.Equal("CA-001", plan.SelectedControl!.ControlId);
        Assert.True(plan.Controls.Single(x => x.ControlId == "CMP-WIN-001").IsSelected);
        Assert.True(plan.Controls.Single(x => x.ControlId == "CA-001").IsSelected);
        Assert.Null(_workspace.Plan); Assert.False(_workspace.ExperimentalChangesEnabled); Assert.Empty(_graph.Reads); Assert.Empty(_graph.Writes);
        Assert.Empty(_shell.ErrorMessage);
    }
    [Theory]
    [InlineData("historical")][InlineData("incomplete")][InlineData("changed-client")][InlineData("unknown")]
    public void Unavailable_or_changed_source_cannot_select_a_control(string scenario)
    {
        switch (scenario)
        {
            case "historical": Set(nameof(Workspace.SnapshotIsLive), false); break;
            case "incomplete": _workspace.Snapshot!.Collections.First().Value.Status = CaptureStatus.Error; break;
            case "changed-client": _workspace.Profile!.TenantId = TestData.TenantB; break;
            case "unknown": _page.Selected!.Finding.Status = FindingStatus.UnableToAssess; break;
        }
        Assert.False(_page.CanAddToPlan); _page.AddToPlanCommand.Execute(null);
        Assert.DoesNotContain(_shell.Page<PlanViewModel>().Controls, x => x.IsSelected);
        Assert.Null(_workspace.Plan); Assert.Empty(_graph.Writes);
    }
    [Fact]
    public void Changed_capture_bytes_are_rechecked_even_when_recorded_ID_and_digest_are_unchanged()
    {
        _workspace.Snapshot!.TenantName = "Modified synthetic evidence";
        _page.AddToPlanCommand.Execute(null);
        Assert.DoesNotContain(_shell.Page<PlanViewModel>().Controls, x => x.IsSelected);
        Assert.Contains("Capture bytes changed", _shell.ErrorMessage); Assert.Null(_workspace.Plan);
    }
    [Fact]
    public void Selecting_a_different_client_or_assessment_drops_previous_control_choices()
    {
        var plan = _shell.Page<PlanViewModel>(); plan.Controls.First(x => x.Eligible).IsSelected = true;
        _workspace.Assessment!.Id = Guid.NewGuid().ToString(); plan.Refresh();
        Assert.DoesNotContain(plan.Controls, x => x.IsSelected);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }
}
