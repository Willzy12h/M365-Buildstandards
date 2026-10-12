using BuildStandard.App.ViewModels;
using BuildStandard.Core.Models;
using Xunit;
namespace BuildStandard.Tests;
public sealed class AssessmentSummaryProjectionTests
{
    [Fact]
    public void All_nine_statuses_are_partitioned_once_without_merging_deviation_or_unknown_with_compliance()
    {
        var statuses = Enum.GetValues<FindingStatus>();
        var summary = AssessmentSummaryProjection.Create(statuses);
        Assert.Equal(9, summary.Total); Assert.Equal(9, summary.Groups.Sum(g => g.Count));
        Assert.Equal(1, summary.Compliant); Assert.Equal(4, summary.NeedsReview);
        Assert.Equal(1, summary.CompliantWithDeviation); Assert.Equal(1, summary.UnableToAssess);
        Assert.Equal(1, summary.SettingsMatchNotEnforced); Assert.Equal(1, summary.NotApplicable);
        Assert.All(summary.ReviewBreakdown, g => Assert.Equal(1, g.Count));
    }
    [Fact]
    public void Empty_and_unrecognised_rows_do_not_claim_a_successful_empty_check()
    {
        var empty = AssessmentSummaryProjection.Create(Array.Empty<FindingStatus>());
        Assert.Equal(0, empty.Total); Assert.Contains("No findings", empty.Description);
        var unknown = AssessmentSummaryProjection.Create(new[] { (FindingStatus)999 });
        Assert.Equal(1, unknown.Unclassified); Assert.Equal(1, unknown.Groups.Sum(g => g.Count));
        Assert.Equal(0, unknown.Compliant);
    }
}
