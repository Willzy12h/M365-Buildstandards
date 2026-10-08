using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Checks;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ScopedAssessmentTests
{
    [Fact]
    public void One_control_uses_the_existing_assessor_and_remains_partial_without_modifying_source_evidence()
    {
        var standard = TestData.Standard(); var profile = TestData.Profile();
        var capture = TestData.Snapshot(standard);
        var original = ToolkitJson.Serialize(capture);
        var engine = new AssessmentEngine(new FixedClock(), "test");
        var full = engine.Assess(capture, standard, profile, TestData.Mappings(), [], "synthetic engineer");
        var selected = engine.AssessSelected(capture, standard, profile, TestData.Mappings(), [], "synthetic engineer",
            CheckSelection.ForControl(standard, profile, "CA-001"));
        var finding = Assert.Single(selected.Findings);
        Assert.Equal("CA-001", finding.ControlId);
        Assert.Equal(full.Findings.Single(f => f.ControlId == "CA-001").Status, finding.Status);
        Assert.False(selected.SnapshotComplete);
        Assert.True(capture.Complete);
        Assert.Equal(original, ToolkitJson.Serialize(capture));
        Assert.Contains(selected.Limitations, l => l.StartsWith("PARTIAL CHECK:", StringComparison.Ordinal));
        Assert.Equal(new[] { "Conditional Access policies", "Subscribed licences" }, selected.CollectionStatus.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Area_selection_assesses_only_registered_controls_in_that_area()
    {
        var standard = TestData.Standard(); var profile = TestData.Profile();
        foreach (var control in standard.Controls) control.Area = control.Collection == "conditionalAccess" ? "Entra" : "Intune";
        var selection = CheckSelection.ForArea(standard, profile, "entra");
        var result = new AssessmentEngine(new FixedClock(), "test").AssessSelected(TestData.Snapshot(standard), standard,
            profile, TestData.Mappings(), [], "synthetic engineer", selection);
        Assert.Equal(selection.ControlIds, result.Findings.Select(f => f.ControlId).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(result.Findings, f => f.ControlId == "CMP-WIN-001");
        Assert.False(result.SnapshotComplete);
    }

    [Fact]
    public void Missing_required_licence_dependency_is_unable_to_check_not_an_empty_or_matching_result()
    {
        var standard = TestData.Standard(); var profile = TestData.Profile();
        var capture = TestData.Snapshot(standard); capture.Collections.Remove("licences");
        var result = new AssessmentEngine(new FixedClock(), "test").AssessSelected(capture, standard, profile,
            TestData.Mappings(), [], "synthetic engineer", CheckSelection.ForControl(standard, profile, "CA-001"));
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingStatus.UnableToAssess, finding.Status);
        Assert.Contains("licences", finding.Reason);
        Assert.Contains("Not present", result.CollectionStatus["Subscribed licences"]);
    }

    [Fact]
    public void Changed_client_scope_requires_a_new_selection_before_assessment()
    {
        var standard = TestData.Standard(); var profile = TestData.Profile();
        var selection = CheckSelection.ForControl(standard, profile, "CA-001");
        profile.Parameters.OfficeLocationId = TestData.Operator;
        Assert.Throws<ConfigurationException>(() => new AssessmentEngine(new FixedClock(), "test").AssessSelected(
            TestData.Snapshot(standard), standard, profile, TestData.Mappings(), [], "synthetic engineer", selection));
    }
}
