using BuildStandard.Core.Models;
using BuildStandard.Engine.Planning;
using Xunit;
namespace BuildStandard.Tests;
public sealed class ControlSelectionEligibilityTests
{
    [Theory]
    [InlineData(FindingStatus.Missing, false, true)]
    [InlineData(FindingStatus.PartialMatch, false, false)]
    [InlineData(FindingStatus.PartialMatch, true, true)]
    [InlineData(FindingStatus.UnableToAssess, true, false)]
    [InlineData(FindingStatus.RequiresManualReview, true, false)]
    [InlineData(FindingStatus.CompliantWithDeviation, true, false)]
    [InlineData((FindingStatus)999, true, false)]
    public void Selection_keeps_unknown_unmanaged_and_manual_findings_ineligible(FindingStatus status, bool owned, bool eligible)
    {
        var standard = TestData.Standard(); var control = standard.FindControl("CA-001")!;
        var result = ControlSelectionEligibility.For(control, standard.FindCollection(control.Collection), new ControlFinding { Status = status, Owned = owned });
        Assert.Equal(eligible, result.Eligible); Assert.NotEmpty(result.Explanation);
    }
    [Fact]
    public void Manual_recipe_and_absent_assessment_have_actionable_reasons()
    {
        var standard = TestData.Standard(); var control = standard.FindControl("ID-001")!;
        Assert.False(ControlSelectionEligibility.For(control, standard.FindCollection(control.Collection), new ControlFinding { Status = FindingStatus.Missing }).Eligible);
        control = standard.FindControl("CA-001")!;
        var result = ControlSelectionEligibility.For(control, standard.FindCollection(control.Collection), null);
        Assert.False(result.Eligible); Assert.Equal("Assess first", result.Status);
    }
}
