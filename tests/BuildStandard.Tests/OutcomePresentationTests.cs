using BuildStandard.App.ViewModels;
using BuildStandard.Core.Models;
using System.Text.Json.Nodes;
using Xunit;
namespace BuildStandard.Tests;
public sealed class OutcomePresentationTests
{
    [Fact]
    public void Intended_disabled_state_is_not_verified_protection_and_accepted_earlier_rows_preclude_nothing_sent()
    {
        var first = new RunResult { WriteAcceptance = WriteAcceptance.Accepted, Configuration = ConfigurationVerification.Unknown,
            WrittenPayload = new JsonObject { ["state"] = "disabled" } };
        var row = OutcomePresentation.ForResult(first);
        Assert.Contains("not verified", row.Protection); Assert.Contains("Unknown", row.Readback);
        var run = new DeploymentRun { Status = RunStatus.Error, Results = [first, new() { WriteAcceptance = WriteAcceptance.NotAttempted }] };
        var summary = OutcomePresentation.ForRun(run, saved: null);
        Assert.DoesNotContain("Nothing sent", summary); Assert.Contains("accepted", summary); Assert.Contains("Save status unknown", summary);
    }
    [Fact]
    public void Observed_disabled_and_unassigned_candidates_are_qualified_and_functional_acceptance_remains_separate()
    {
        var disabled = new RunResult { WriteAcceptance = WriteAcceptance.Accepted, Configuration = ConfigurationVerification.Pass,
            AfterObject = new JsonObject { ["state"] = "disabled" } };
        Assert.Contains("Disabled candidate observed", OutcomePresentation.ForResult(disabled).Protection);
        var unassigned = new RunResult { WriteAcceptance = WriteAcceptance.Accepted, Configuration = ConfigurationVerification.Pass,
            AfterObject = new JsonObject { ["assignments"] = new JsonArray() } };
        Assert.Contains("Unassigned candidate observed", OutcomePresentation.ForResult(unassigned).Protection);
        Assert.Contains("Functional", OutcomePresentation.ForResult(disabled).Functional);
        disabled.Configuration = ConfigurationVerification.Unknown;
        Assert.Contains("not verified", OutcomePresentation.ForResult(disabled).Protection);
    }
}
