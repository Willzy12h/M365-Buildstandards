using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
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
    public void Recording_guidance_explains_the_next_step_until_a_requirement_is_selected()
    {
        Assert.False(_page.RecordCommand.CanExecute(null));
        Assert.Contains("Select a job", _page.RecordGuidance);
        OpenJob();
        _page.SelectedRequirement = null;
        Assert.False(_page.RecordCommand.CanExecute(null));
        Assert.Contains("Select a requirement", _page.RecordGuidance);
        Select("CA-001");
        Assert.True(_page.RecordCommand.CanExecute(null));
        Assert.Contains("does not perform", _page.RecordGuidance);
    }

    [Fact]
    public void Requirement_details_lead_with_the_name_and_preserve_the_exact_reference()
    {
        OpenJob(); Select("CA-001");
        Assert.StartsWith(_page.SelectedRequirement!.Name + "\n", _page.RequirementText);
        Assert.Contains("Requirement ID: CA-001", _page.RequirementText);
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

    [Fact]
    public void A_cutover_case_is_opened_at_review_and_the_form_then_offers_the_next_stage()
    {
        const string oldPolicy = "aaaaaaaa-1111-4111-8111-000000000031";
        var standard = _shell.Workspace.RequireStandard();
        var capture = TestData.Snapshot(standard);
        capture.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy(oldPolicy, "Legacy MFA", "enabled", new[] { TestData.Emergency }));
        _shell.Workspace.Evidence.SaveSnapshot(capture);
        _shell.Workspace.LoadStoredSnapshot(capture.Id);
        OpenJob();
        Select("CA-001");

        _page.RecordKind = JobsViewModel.CutoverKind;
        Assert.Equal(JobsViewModel.NewCase, _page.SelectedCase);
        Assert.Equal(CutoverStage.Review, _page.Stage);
        _page.DecisionOwner = "Client security lead";
        _page.Reason = "Replace the legacy policy.";
        _page.ObjectIds = oldPolicy;
        _page.RecoveryLimits = "The legacy policy stays enabled until retirement.";
        _page.RecordCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);

        var revision = Assert.Single(_shell.Workspace.Evidence.LoadCutovers(TestData.TenantA).Revisions);
        Assert.Equal(CutoverStage.Review, revision.Stage);
        Assert.Equal(revision.CaseId, _page.SelectedCase);
        Assert.Equal(CutoverStage.CandidateCreated, _page.Stage);
        Assert.Equal(oldPolicy, _page.ObjectIds);
        Assert.Equal("review", _page.Requirements.Single(r => r.InstanceKey == "CA-001").Cutover);

        // The candidate stage needs a saved run; without one the engine refuses and nothing more is written.
        _page.NewObjectIds = "aaaaaaaa-1111-4111-8111-000000000032";
        _page.Reason = "Candidate created.";
        _page.RecordCommand.Execute(null);
        Assert.NotEqual("", _shell.ErrorMessage);
        Assert.Single(_shell.Workspace.Evidence.LoadCutovers(TestData.TenantA).Revisions);
    }

    [Fact]
    public void An_outcome_cites_a_stored_assessment_of_the_capture_in_view_and_verifies()
    {
        var standard = _shell.Workspace.RequireStandard();
        var capture = TestData.Snapshot(standard);
        _shell.Workspace.Evidence.SaveSnapshot(capture);
        _shell.Workspace.LoadStoredSnapshot(capture.Id);
        var assessment = new AssessmentEngine(new FixedClock(), "test").Assess(capture, standard, TestData.Profile(), TestData.Mappings(), [], "engineer@test.example");
        assessment.Findings.Single(f => f.ControlId == "CA-003").Status = FindingStatus.Compliant;
        _shell.Workspace.Evidence.SaveAssessment(assessment);
        OpenJob();
        Select("CA-003");

        Assert.Contains("Compliant", Assert.Single(_page.Assessments, a => a.Key == assessment.Id).Label);
        _page.AssessmentId = assessment.Id;
        _page.Reason = "Matches the stored assessment.";
        _page.RecordCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);

        var observation = Assert.Single(_shell.Workspace.Evidence.LoadObservations(TestData.TenantA).Observations);
        Assert.Contains(observation.Evidence, e => e.Kind == EvidenceKind.Assessment);
        Assert.Equal(RequirementState.Verified, _page.Requirements.Single(r => r.InstanceKey == "CA-003").State);
        Assert.Contains("cites a stored assessment", _page.RequirementText);
        Assert.Equal("", _page.AssessmentId);
    }

    [Fact]
    public void Cutover_lists_read_back_as_they_are_written_and_an_unknown_result_is_refused()
    {
        var prerequisites = CutoverText.Prerequisites("[x] Licences assigned\n[ ] Helpdesk briefed\nRollback tested");
        Assert.Equal(new[] { true, false, false }, prerequisites.Select(p => p.Met));
        Assert.Equal("Helpdesk briefed", prerequisites[1].Description);
        Assert.Equal(prerequisites.Select(p => p.Description), CutoverText.Prerequisites(CutoverText.Prerequisites(prerequisites)).Select(p => p.Description));

        var criteria = CutoverText.Criteria("Passed | Pilot users prompted for MFA | Sign-in logs 7 Oct\nnot run | Guests unaffected");
        Assert.Equal(new[] { CriterionResult.Passed, CriterionResult.NotRun }, criteria.Select(c => c.Result));
        Assert.Equal("Sign-in logs 7 Oct", criteria[0].Detail);
        Assert.Equal(criteria.Select(c => c.Result), CutoverText.Criteria(CutoverText.Criteria(criteria)).Select(c => c.Result));

        Assert.Throws<BDIT.TenantToolkit.Core.ConfigurationException>(() => CutoverText.Criteria("Probably | Something"));
        Assert.Throws<BDIT.TenantToolkit.Core.ConfigurationException>(() => CutoverText.Criteria("Passed without a separator"));
        Assert.Null(CutoverText.Approval(" ", ""));
        Assert.Null(CutoverText.Escalation("", "Owner", "Path"));
    }
}
