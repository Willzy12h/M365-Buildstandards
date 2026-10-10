using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Collection;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class AssessmentReviewRegressionTests
{
    private static readonly FixedClock Clock = new();
    private static readonly AssessmentEngine Engine = new(Clock, "test");

    private static StandardCatalogue Catalogue()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards", "2026.09.11.json"));
        Assert.True(File.Exists(path), "The reviewed standard is required by these regression tests.");
        return StandardsLoader.Parse(File.ReadAllText(path), path);
    }

    private static ControlFinding Assess(StandardCatalogue standard, TenantSnapshot snapshot, string controlId,
        TenantProfile? profile = null, ManagedObjectMappings? mappings = null, params Deviation[] deviations) =>
        Engine.Assess(snapshot, standard, profile ?? TestData.Profile(), mappings ?? TestData.Mappings(), deviations, "test")
            .Findings.Single(f => f.ControlId == controlId);

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    [InlineData(4, true, false)]
    [InlineData(5, false, true)]
    public void Actual_administrator_recipe_counts_member_objects(int count, bool covered, bool excess)
    {
        var standard = Catalogue();
        var snapshot = TestData.Snapshot(standard);
        var members = new JsonArray();
        for (var i = 0; i < count; i++) members.Add(new JsonObject { ["id"] = $"synthetic-{i}", ["@odata.type"] = "#microsoft.graph.user" });
        snapshot.Collections["directoryRoles"].Items.Add(new JsonObject
        {
            ["id"] = "synthetic-role", ["displayName"] = "Global Administrator",
            ["roleTemplateId"] = "62e90394-69f5-4237-9190-012177145e10", ["members"] = members
        });

        var finding = Assess(standard, snapshot, "ID-003");
        var observation = Assert.Single(finding.Equivalence);
        Assert.Equal(covered, observation.Covered);
        Assert.Equal(excess, observation.Caveats.Count > 0);
        Assert.Equal(covered ? FindingStatus.PartialMatch : FindingStatus.RequiresManualReview, finding.Status);
        Assert.Contains(observation.Signals, s => s.Observed.StartsWith(count + " member object(s)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("2")]
    [InlineData("\"2\"")]
    [InlineData("{}")]
    public void Count_operators_do_not_interpret_non_array_or_unknown_values_as_members(string json)
    {
        var observed = ToolkitJson.ParseNode(json);
        foreach (var op in new[] { SignalOperator.CountAtLeast, SignalOperator.CountAtMost })
            Assert.False(EquivalenceEvaluator.Matches(new EquivalenceSignal { Operator = op, Value = 2 }, observed));
    }

    [Fact]
    public void Array_count_does_not_change_existing_numeric_comparison_contract()
    {
        var observed = new JsonArray("a", "b");
        Assert.True(EquivalenceEvaluator.Matches(new EquivalenceSignal { Operator = SignalOperator.CountAtLeast, Value = 2 }, observed));
        Assert.True(EquivalenceEvaluator.Matches(new EquivalenceSignal { Operator = SignalOperator.CountAtMost, Value = 2 }, observed));
        Assert.False(EquivalenceEvaluator.Matches(new EquivalenceSignal { Operator = SignalOperator.AtLeast, Value = 2 }, observed));
        Assert.False(EquivalenceEvaluator.Matches(new EquivalenceSignal { Operator = SignalOperator.CountAtLeast, Value = -1 }, observed));
        Assert.False(EquivalenceEvaluator.Matches(new EquivalenceSignal { Operator = SignalOperator.CountAtMost, Value = 2.5 }, observed));
    }

    public static IEnumerable<object[]> UnusableEvidenceCases()
    {
        foreach (var control in new[] { "ID-002", "ID-003", "ENR-001", "CMP-001" })
        foreach (var failure in new[] { "missing", "error", "partial", "notAttempted" })
        foreach (var deviation in new[] { false, true }) yield return new object[] { control, failure, deviation };
    }

    [Theory]
    [MemberData(nameof(UnusableEvidenceCases))]
    public void Evidence_backed_manual_controls_require_usable_reads_even_with_a_deviation(string controlId, string failure, bool approvedDeviation)
    {
        var standard = Catalogue();
        var snapshot = TestData.Snapshot(standard);
        var key = standard.FindControl(controlId)!.Collection!;
        var capture = snapshot.Collections[key];
        if (failure == "missing") snapshot.Collections.Remove(key);
        else if (failure == "partial") capture.DetailIncomplete = true;
        else capture.Status = failure == "error" ? CaptureStatus.Error : CaptureStatus.NotAttempted;
        var deviations = approvedDeviation
            ? new[] { new Deviation { TenantId = TestData.TenantA, ControlId = controlId, Reason = "Previously approved configuration" } }
            : Array.Empty<Deviation>();

        var finding = Assess(standard, snapshot, controlId, deviations: deviations);
        Assert.Equal(FindingStatus.UnableToAssess, finding.Status);
        Assert.Empty(finding.Equivalence);
        if (approvedDeviation) Assert.Contains(finding.Notes, n => n.Contains("cannot be applied", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CMP-AND-001", "androidMinimumVersion")]
    [InlineData("ENR-004", "espBlockingAppIds")]
    public void Assessment_uses_the_same_defaults_as_planning_but_requires_client_confirmation(string controlId, string parameter)
    {
        var standard = Catalogue();
        var control = standard.FindControl(controlId)!;
        var profile = TestData.Profile();
        var defaults = PolicyInputDefaults.Apply(control.Payload!, standard, profile.Parameters.ToTemplateValues(profile.TenantId), Clock.UtcNow);
        var actual = (JsonObject)PolicyInputDefaults.Resolve(control.Payload!, standard, defaults.Values)!;
        actual["id"] = "synthetic-policy";
        actual[TenantCollector.AssignmentsKey] = new JsonArray(new JsonObject { ["id"] = "synthetic-assignment" });
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections[control.Collection!].Items.Add(actual);

        var finding = Assess(standard, snapshot, controlId, profile);
        Assert.True(Assert.Single(finding.Candidates).SettingsMatch);
        Assert.Equal(FindingStatus.RequiresManualReview, finding.Status);
        Assert.Contains(finding.Notes, n => n.Contains("Review required", StringComparison.Ordinal) && n.Contains("shipped default", StringComparison.Ordinal));
        Assert.Null(profile.Parameters.PolicyInputs);

        profile.Parameters.PolicyInputs = new Dictionary<string, JsonNode?> { [parameter] = standard.Parameters.Single(p => p.Key == parameter).Default!.DeepClone() };
        finding = Assess(standard, snapshot, controlId, profile);
        Assert.Equal(FindingStatus.Compliant, finding.Status);
        Assert.DoesNotContain(finding.Notes, n => n.Contains("shipped default", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(120, false)]
    public void Tap_coverage_checks_maximum_lifetime_even_when_default_is_one_hour(int maximum, bool covered)
    {
        var standard = Catalogue();
        var snapshot = TestData.Snapshot(standard);
        var control = standard.FindControl("ID-002")!;
        snapshot.Collections[control.Collection!].Items.Add(new JsonObject
        {
            ["id"] = "authenticationMethodsPolicy",
            ["authenticationMethodConfigurations"] = new JsonArray(
                new JsonObject { ["id"] = "MicrosoftAuthenticator", ["state"] = "enabled" },
                new JsonObject { ["id"] = "Sms", ["state"] = "disabled" },
                new JsonObject { ["id"] = "Voice", ["state"] = "disabled" },
                new JsonObject { ["id"] = "TemporaryAccessPass", ["state"] = "enabled", ["isUsableOnce"] = true,
                    ["defaultLifetimeInMinutes"] = 60, ["maximumLifetimeInMinutes"] = maximum })
        });
        var observation = Assert.Single(Assess(standard, snapshot, control.Id).Equivalence);
        Assert.Equal(covered, observation.Covered);
        Assert.Equal(!covered, observation.Caveats.Any(c => c.Contains("maximum lifetime", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Matching_group_metadata_does_not_confirm_the_intended_population(int memberCount)
    {
        var standard = Catalogue();
        var control = standard.Controls.First(c => c.Collection == "groups" && c.ExpectedProduction.State == "populated");
        var actual = (JsonObject)control.Payload!.DeepClone();
        actual["id"] = "synthetic-group";
        var members = new JsonArray();
        for (var i = 0; i < memberCount; i++) members.Add(new JsonObject { ["id"] = "synthetic-member-" + i });
        actual["members"] = members;
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["groups"].Items.Add(actual);

        var finding = Assess(standard, snapshot, control.Id);
        Assert.True(Assert.Single(finding.Candidates).SettingsMatch);
        Assert.Equal(FindingStatus.RequiresManualReview, finding.Status);
        Assert.Contains("intended membership", finding.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("securityEnabled", "false")]
    [InlineData("mailEnabled", "true")]
    [InlineData("groupTypes", "[\"DynamicMembership\"]")]
    public void Same_name_group_with_changed_material_settings_is_not_compliant(string setting, string json)
    {
        var standard = Catalogue();
        var control = standard.Controls.First(c => c.Collection == "groups" && c.Payload is not null);
        var actual = (JsonObject)control.Payload!.DeepClone();
        actual["id"] = "synthetic-group";
        actual[setting] = ToolkitJson.ParseNode(json);
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["groups"].Items.Add(actual);

        var finding = Assess(standard, snapshot, control.Id);
        Assert.Equal(FindingStatus.PartialMatch, finding.Status);
        Assert.False(Assert.Single(finding.Candidates).SettingsMatch);
        Assert.Contains(finding.BestCandidate!.Differences, d => d.Setting == setting && !d.Match);
    }

    [Fact]
    public void Another_security_group_does_not_block_a_different_population_by_generic_shape()
    {
        var standard = Catalogue();
        var controls = standard.Controls.Where(c => c.Collection == "groups" && c.Payload is not null).Take(2).ToArray();
        var actual = (JsonObject)controls[0].Payload!.DeepClone();
        actual["id"] = "synthetic-other-group";
        actual["members"] = new JsonArray();
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["groups"].Items.Add(actual);
        snapshot.Collections["groups"].Count = 1;
        var finding = Assess(standard, snapshot, controls[1].Id);
        Assert.Equal(FindingStatus.Missing, finding.Status);
        Assert.Empty(finding.Candidates);
        var planner = new DeploymentPlanner(Clock, "test");
        var request = new PlanRequest
        {
            Profile = TestData.Profile(), Standard = standard, Snapshot = snapshot, Mappings = TestData.Mappings(),
            Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { controls[1].Id }, Session = TestData.Session()
        };
        Assert.Equal(PlanAction.Create, Assert.Single(planner.Build(request).Rows).Action);

        actual["displayName"] = "Renamed group";
        actual["mailNickname"] = controls[1].Payload!["mailNickname"]!.DeepClone();
        finding = Assess(standard, snapshot, controls[1].Id);
        Assert.Single(finding.Candidates);
        Assert.Equal(PlanAction.Conflict, Assert.Single(planner.Build(request).Rows).Action);
    }

    [Fact]
    public void Exact_owned_group_is_still_compared_when_its_name_and_nickname_change()
    {
        var standard = Catalogue();
        var control = standard.Controls.First(c => c.Collection == "groups" && c.Payload is not null);
        var actual = (JsonObject)control.Payload!.DeepClone();
        actual["id"] = "synthetic-owned-group";
        actual["displayName"] = "Renamed group";
        actual["mailNickname"] = "renamed";
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["groups"].Items.Add(actual);
        var mappings = TestData.Mappings();
        mappings.ByControl[control.Id] = new ManagedObjectMapping { ControlId = control.Id, Collection = "groups", ObjectId = "synthetic-owned-group" };
        var candidate = Assert.Single(Assess(standard, snapshot, control.Id, mappings: mappings).Candidates);
        Assert.True(candidate.ToolkitManaged);
        Assert.False(candidate.SettingsMatch);
    }

    [Theory]
    [InlineData("isTrusted", "true")]
    [InlineData("ipRanges", "[{\"@odata.type\":\"#microsoft.graph.iPv4CidrRange\",\"cidrAddress\":\"198.51.100.0/24\"}]")]
    public void Named_location_material_differences_are_visible(string setting, string json)
    {
        var standard = Catalogue();
        var control = standard.Controls.Single(c => c.Collection == "namedLocations" && c.Payload is not null);
        var profile = TestData.Profile();
        profile.Parameters.PolicyInputs = new Dictionary<string, JsonNode?>
        {
            ["officeIpRanges"] = ToolkitJson.ParseNode("""[{"@odata.type":"#microsoft.graph.iPv4CidrRange","cidrAddress":"192.0.2.0/24"}]""")
        };
        var actual = (JsonObject)CanonicalJson.Resolve(control.Payload, profile.Parameters.ToTemplateValues(profile.TenantId))!;
        actual["id"] = "synthetic-location";
        actual[setting] = ToolkitJson.ParseNode(json);
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["namedLocations"].Items.Clear();
        snapshot.Collections["namedLocations"].Items.Add(actual);

        var finding = Assess(standard, snapshot, control.Id, profile);
        Assert.Equal(FindingStatus.PartialMatch, finding.Status);
        Assert.Contains(Assert.Single(finding.Candidates).Differences, d => d.Setting == setting && !d.Match);

        actual["displayName"] = "Unrelated location";
        finding = Assess(standard, snapshot, control.Id, profile);
        if (setting == "ipRanges") Assert.Empty(finding.Candidates);
        else Assert.Single(finding.Candidates); // Same IP range remains a genuine overlap even when renamed.
    }
}
