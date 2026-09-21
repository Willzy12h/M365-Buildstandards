using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class ApplicationDeploymentAssessmentTests
{
    [Theory]
    [InlineData("uninstall", "allDevicesAssignmentTarget", false)]
    [InlineData("available", "allDevicesAssignmentTarget", false)]
    [InlineData("availableWithoutEnrollment", "allDevicesAssignmentTarget", false)]
    [InlineData("required", "exclusionGroupAssignmentTarget", false)]
    [InlineData("required", "missing", false)]
    [InlineData("required", "groupAssignmentTarget", false)]
    [InlineData("required", "allLicensedUsersAssignmentTarget", false)]
    [InlineData("required", "allDevicesAssignmentTarget", true)]
    public void Assessment_requires_relevant_intent_and_established_scope(string intent, string type, bool compliant)
    {
        var standard = TestData.Standard();
        standard.Collections["applicationTest"] = new CollectionDefinition { Path = "/deviceAppManagement/mobileApps", Assignments = true };
        var control = new ControlDefinition { Id = "APP-TEST", Name = "Synthetic app", Collection = "applicationTest",
            Assessment = new AssessmentRule { Mode = AssessmentMode.Settings },
            Payload = new JsonObject { ["displayName"] = "Synthetic app", ["@odata.type"] = "#microsoft.graph.win32LobApp", ["installExperience"] = new JsonObject { ["runAsAccount"] = "system" } },
            ExpectedProduction = new ExpectedProduction { State = "assigned", ApplicationDeployment = new ApplicationDeploymentExpectation { Population = AssignmentPopulation.AllDevices } } };
        standard.Controls.Add(control);
        var item = (JsonObject)control.Payload.DeepClone(); item["id"] = "synthetic-app";
        var row = new JsonObject { ["intent"] = intent };
        if (type != "missing") row["target"] = new JsonObject { ["@odata.type"] = "#microsoft.graph." + type };
        item[TenantCollector.AssignmentsKey] = new JsonArray(row);
        var snapshot = TestData.Snapshot(standard); snapshot.Collections["applicationTest"].Items.Add(item);
        var finding = new AssessmentEngine(new FixedClock(), "test").Assess(snapshot, standard, TestData.Profile(), TestData.Mappings(), [], "test").Findings.Single(f => f.ControlId == control.Id);
        Assert.True(Assert.Single(finding.Candidates).SettingsMatch);
        Assert.Equal(compliant, finding.Status == FindingStatus.Compliant);
        if (type is "missing" or "exclusionGroupAssignmentTarget" or "groupAssignmentTarget") Assert.Equal(FindingStatus.RequiresManualReview, finding.Status);
    }

    [Theory]
    [InlineData("missingExpectation")]
    [InlineData("expectedExclusion")]
    [InlineData("filter")]
    [InlineData("unknownTargetProperty")]
    [InlineData("mixedIntent")]
    [InlineData("incomplete")]
    public void Unresolved_application_scope_remains_unknown(string variation)
    {
        ApplicationDeploymentExpectation? expected = new() { Population = AssignmentPopulation.AllDevices };
        var target = new JsonObject { ["@odata.type"] = "#microsoft.graph.allDevicesAssignmentTarget" };
        var row = new JsonObject { ["intent"] = "required", ["target"] = target };
        var app = new JsonObject { [TenantCollector.AssignmentsKey] = new JsonArray(row) };
        switch (variation)
        {
            case "missingExpectation": expected = null; break;
            case "expectedExclusion": expected.ExclusionControlId = "PRE-010"; break;
            case "filter": target["deviceAndAppManagementAssignmentFilterId"] = TestData.Mam; break;
            case "unknownTargetProperty": target["futureTarget"] = true; break;
            case "mixedIntent": var other = (JsonObject)row.DeepClone(); other["intent"] = "uninstall"; app[TenantCollector.AssignmentsKey]!.AsArray().Add(other); break;
            case "incomplete": app[TenantCollector.AssignmentsUnknownKey] = true; break;
        }
        Assert.Null(ApplicationDeploymentAssessment.Evaluate(app, expected).Established);
    }

    [Fact]
    public void Shipped_apps_declare_scope_and_old_models_omit_new_null_metadata()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards/2026.09.11.json"));
        var standard = StandardsLoader.Parse(File.ReadAllText(path), path);
        var apps = standard.Controls.Where(c => c.Collection == "applications").ToArray(); Assert.Equal(8, apps.Length);
        foreach (var control in apps)
        {
            var scope = control.ExpectedProduction.ApplicationDeployment; Assert.NotNull(scope);
            Assert.Equal("required", scope.Intent);
            Assert.Equal(control.Id == "APP-WIN-001" ? AssignmentPopulation.AllUsers : AssignmentPopulation.AllDevices, scope.Population);
            Assert.Equal(control.Id == "APP-WIN-001" ? "PRE-009" : "PRE-010", scope.ExclusionControlId);
        }
        Assert.DoesNotContain("applicationDeployment", ToolkitJson.Serialize(new ExpectedProduction()));
    }
}
