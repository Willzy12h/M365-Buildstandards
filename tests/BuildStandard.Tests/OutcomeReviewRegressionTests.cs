using BuildStandard.App.ViewModels;
using BuildStandard.Core.Models;
using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Engine.Evidence;
using Xunit;

namespace BuildStandard.Tests;

public sealed class OutcomeReviewRegressionTests
{
    [Theory]
    [InlineData("future-value")]
    [InlineData("")]
    [InlineData("accepted")]
    public void Unrecognised_recorded_acceptance_is_counted_as_uncertain(string value)
    {
        var run = new DeploymentRun { Results = [new() { WriteAcceptance = value }] };
        var text = OutcomePresentation.ForRun(run, null);
        Assert.Contains("1 uncertain", text);
        Assert.DoesNotContain("All recorded writes were not attempted", text);
        Assert.Equal(value, run.Results[0].WriteAcceptance);
    }

    [Fact]
    public void Mixed_known_and_unrecognised_results_preserve_every_recorded_row()
    {
        var run = new DeploymentRun { Results = [new() { WriteAcceptance = WriteAcceptance.Accepted },
            new() { WriteAcceptance = WriteAcceptance.Unknown }, new() { WriteAcceptance = "future-value" },
            new() { WriteAcceptance = WriteAcceptance.Rejected }, new() { WriteAcceptance = WriteAcceptance.NotAttempted }] };
        Assert.Contains("1 accepted · 2 uncertain · 1 rejected · 1 not attempted", OutcomePresentation.ForRun(run, true));
    }

    [Fact]
    public void Unrecognised_valid_historical_outcome_also_blocks_completion_without_rewriting_evidence()
    {
        using var root = new TempRoot(); var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            StartedAt = "2026-10-13T12:00:00Z", Status = RunStatus.ReviewRequired,
            Results = [new() { ControlId = "CA-001", Status = ResultStatus.Error, PlannedAction = "Create", WriteAcceptance = "future-value" }] };
        store.SaveRun(run); var original = File.ReadAllBytes(store.RunFile(run));
        Assert.Throws<PlanValidationException>(() => store.AssertCompletionWritesResolved(TestData.TenantA, ["CA-001"]));
        Assert.Throws<PlanValidationException>(() => store.AssertPlanHasNotRun(new() { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA,
            Rows = [new() { ControlId = "CA-001", Action = PlanAction.Create }] }));
        Assert.Equal(original, File.ReadAllBytes(store.RunFile(run)));
    }

    [Fact]
    public void Viewing_unclassified_history_does_not_clear_the_shared_uncertainty_guidance()
    {
        var context = new HomeTaskContext { Profile = TestData.Profile(), Standard = TestData.Standard(), RunReviewed = true,
            LastRun = new() { TenantId = TestData.TenantA, Status = RunStatus.ReviewRequired,
                Results = [new() { WriteAcceptance = "future-value" }] } };
        Assert.Equal("recovery", HomeTaskProjection.Create(context).NextPageKey);
    }
}
