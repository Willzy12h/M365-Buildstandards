using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Models;
using Xunit;

namespace BuildStandard.Tests;

public sealed class HomeFreshnessReviewRegressionTests
{
    [Theory]
    [InlineData(30, 0, "configuration")]
    [InlineData(0, 30, "plan")]
    public void Expired_original_review_inputs_offer_refresh_instead_of_approval(int snapshotMinutes, int planMinutes, string page)
    {
        var context = Current(snapshotMinutes, planMinutes);
        var projection = HomeTaskProjection.Create(context);
        Assert.Equal(page, projection.NextPageKey);
        Assert.Contains("expired", projection.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Review exact approval", projection.NextActionLabel);
    }

    [Fact]
    public void Unparseable_plan_time_does_not_offer_approval()
    {
        var context = Current(0, 0); context.Plan!.CreatedAt = "not a recorded timestamp";
        Assert.Equal("plan", HomeTaskProjection.Create(context).NextPageKey);
    }

    [Fact]
    public void Fresh_inputs_only_offer_review_and_grant_no_execution_authority()
    {
        var context = Current(1, 1);
        Assert.Equal("Review exact approval", HomeTaskProjection.Create(context).NextActionLabel);
        Assert.Contains("execution-layer checks", HomeTaskProjection.Create(context).Guidance);
    }

    private static HomeTaskContext Current(int snapshotMinutes, int planMinutes)
    {
        var standard = TestData.Standard(); var snapshot = TestData.Snapshot(standard);
        snapshot.CapturedAt = Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(-snapshotMinutes));
        return new() { Profile = TestData.Profile(), Session = TestData.Session(mode: SessionMode.Deployment),
            Standard = standard, Snapshot = snapshot, SnapshotIsLive = true, ExperimentalEnabled = true,
            AcknowledgedSnapshotId = snapshot.Id,
            Plan = new() { CreatedAt = Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(-planMinutes)),
                Rows = [new() { ControlId = "CA-001", Action = PlanAction.Create }] } };
    }
}
