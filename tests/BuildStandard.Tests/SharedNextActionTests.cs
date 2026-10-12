using BuildStandard.App.ViewModels;
using Xunit;
namespace BuildStandard.Tests;
public sealed class SharedNextActionTests
{
    [Fact]
    public void Uncertainty_without_client_or_snapshot_precedes_setup_and_evidence()
    {
        var result = HomeTaskProjection.Create(new HomeTaskContext { InterruptedNotice = "Uncertain write: preserve evidence", ApplicationSetupActive = true });
        Assert.Equal("recovery", result.NextPageKey);
        Assert.Contains("Uncertain write", result.Guidance);
    }
    [Fact]
    public void Busy_guidance_cannot_offer_another_navigation_action()
    {
        var result = HomeTaskProjection.Create(new HomeTaskContext { Busy = true, BusyMessage = "Synthetic operation" });
        Assert.Equal("", result.NextPageKey);
        Assert.Contains("Synthetic operation", result.Guidance);
    }

    [Fact]
    public void Reviewed_completed_run_does_not_trap_the_engineer_in_outcome_review()
    {
        var standard = TestData.Standard(); var profile = TestData.Profile();
        var context = new HomeTaskContext { Standard = standard, Profile = profile,
            LastRun = new() { TenantId = profile.TenantId, Status = BuildStandard.Core.Models.RunStatus.Completed } };
        Assert.Equal("deploy", HomeTaskProjection.Create(context).NextPageKey);
        Assert.Equal("configuration", HomeTaskProjection.Create(context with { RunReviewed = true }).NextPageKey);
        Assert.Equal(BuildStandard.Core.Models.RunStatus.Completed, context.LastRun.Status);
    }
}
