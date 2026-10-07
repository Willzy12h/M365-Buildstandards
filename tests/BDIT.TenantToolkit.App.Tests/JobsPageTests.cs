using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Workflow;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>The Jobs page records through the engine workflow and shows the completion projection, never more.</summary>
public sealed class JobsPageTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;
    private readonly JobsViewModel _page;

    public JobsPageTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        if (workspace.Standard is null) throw new InvalidOperationException("The synthetic standard did not load: " + workspace.StandardError);
        _shell = new ShellViewModel(workspace);
        workspace.ApplyProfileToSession(TestData.Profile(), save: true);
        _page = _shell.Page<JobsViewModel>();
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OpenJob()
    {
        _page.NewOwner = "Owner Name";
        _page.OpenJobCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);
    }

    private void Select(string control) => _page.SelectedRequirement = _page.Requirements.Single(r => r.InstanceKey == control);

    [Fact]
    public void Opening_a_job_selects_it_and_lists_every_requirement_as_outstanding()
    {
        OpenJob();

        var job = Assert.Single(_page.Jobs);
        Assert.Same(job, _page.SelectedJob);
        Assert.Equal(4, _page.Requirements.Count);
        Assert.All(_page.Requirements, r => Assert.Equal(RequirementState.Outstanding, r.State));
        Assert.StartsWith("Not complete: 4 of 4", _page.ClaimText);
    }

    [Fact]
    public void A_second_outcome_supersedes_the_first_and_a_pass_without_a_capture_does_not_verify()
    {
        OpenJob();
        Select("CA-001");
        _page.Status = ObservationStatus.Fail;
        _page.Reason = "Policy is report-only.";
        _page.RecordCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);

        _page.Status = ObservationStatus.Pass;
        _page.Reason = "Policy now enforced.";
        _page.RecordCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);

        var requirement = _page.Requirements.Single(r => r.InstanceKey == "CA-001");
        Assert.Equal(ObservationStatus.Pass, requirement.Outcome);
        Assert.Equal(RequirementState.Outstanding, requirement.State);
        Assert.Contains(requirement.Reasons, r => r.Contains("no stored evidence"));
        Assert.Contains("Outcome history", _page.RequirementText);
        Assert.Equal(2, _shell.Workspace.Evidence.LoadObservations(TestData.TenantA).Observations.Count);
    }

    [Fact]
    public void An_approved_departure_without_a_deviation_is_refused_and_writes_nothing()
    {
        OpenJob();
        Select("CA-003");
        _page.RecordKind = JobsViewModel.DecisionKind;
        _page.Decision = DispositionDecision.ApprovedDeparture;
        _page.DecisionOwner = "Client security lead";
        _page.Reason = "Agreed exception.";
        _page.RecordCommand.Execute(null);

        Assert.NotEqual("", _shell.ErrorMessage);
        Assert.Empty(_shell.Workspace.Evidence.LoadDispositions(TestData.TenantA).Dispositions);
    }

    [Fact]
    public void A_review_date_in_the_wrong_format_is_refused()
    {
        OpenJob();
        Select("CA-001");
        _page.ReviewDue = "next year";
        _page.Reason = "Checked.";
        _page.RecordCommand.Execute(null);

        Assert.Contains("yyyy-MM-dd", _shell.ErrorMessage);
        Assert.Empty(_shell.Workspace.Evidence.LoadObservations(TestData.TenantA).Observations);
    }
}
