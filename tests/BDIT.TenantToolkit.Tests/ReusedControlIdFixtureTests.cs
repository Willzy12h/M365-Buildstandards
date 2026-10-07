using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// CLA-20261006-07 (evidence for R03): control IDs were reused between releases. In 2026.09.10 PRE-004 is the pilot
/// devices group and PRE-005 the office named location; in 2026.09.30 PRE-004 is the MAM-only users group and PRE-005
/// the pilot devices group. Ownership mappings are keyed by control ID, so a tenant built on .10 and reviewed under
/// .30 meets an ownership record for a different requirement. This fixture holds the safety outcome: no write, no
/// duplicate, no adoption. The explanation the engineer sees comes from release lineage (INT-051/057), tested in
/// <see cref="ReleaseLineageTests"/>.
/// </summary>
public sealed class ReusedControlIdFixtureTests
{
    private const string PilotGroup = "aaaaaaaa-0000-4000-8000-000000000004";
    private const string OfficeLocation = "aaaaaaaa-0000-4000-8000-000000000005";

    private static StandardCatalogue Load(string release) => StandardsLoader.Parse(
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, $"../../../../../standards/{release}.json"))), release + ".json");

    private static (TenantSnapshot Snapshot, ManagedObjectMappings Mappings) BuiltOnRelease10(StandardCatalogue current)
    {
        var old = Load("2026.09.10");
        var pilot = (JsonObject)old.FindControl("PRE-004")!.Payload!.DeepClone();
        var office = (JsonObject)old.FindControl("PRE-005")!.Payload!.DeepClone();
        office["ipRanges"] = new JsonArray(new JsonObject { ["@odata.type"] = "#microsoft.graph.iPv4CidrRange", ["cidrAddress"] = "192.0.2.0/24" });

        var snapshot = TestData.Snapshot(current);
        var groups = snapshot.Collections["groups"];
        var created = (JsonObject)pilot.DeepClone(); created["id"] = PilotGroup;
        groups.Items.Add(created); groups.Count = groups.Items.Count;
        var locations = snapshot.Collections["namedLocations"];
        var location = (JsonObject)office.DeepClone(); location["id"] = OfficeLocation;
        locations.Items.Add(location); locations.Count = locations.Items.Count;

        var mappings = TestData.Mappings();
        mappings.ByControl["PRE-004"] = new ManagedObjectMapping { ControlId = "PRE-004", ObjectId = PilotGroup, Collection = "groups", Release = old.Release, LastApplied = pilot, LastAppliedDigest = CanonicalJson.Sha256(pilot) };
        mappings.ByControl["PRE-005"] = new ManagedObjectMapping { ControlId = "PRE-005", ObjectId = OfficeLocation, Collection = "namedLocations", Release = old.Release, LastApplied = office, LastAppliedDigest = CanonicalJson.Sha256(office) };
        return (snapshot, mappings);
    }

    [Fact]
    public void Release_10_mappings_reviewed_under_release_30_cause_no_write_duplicate_or_adoption()
    {
        var current = Load("2026.09.30");
        var (snapshot, mappings) = BuiltOnRelease10(current);

        var plan = new DeploymentPlanner(new FixedClock(), "test").Build(new PlanRequest
        {
            Profile = TestData.Profile(), Standard = current, Snapshot = snapshot, Mappings = mappings,
            Deviations = Array.Empty<Deviation>(), SelectedControlIds = new[] { "PRE-004", "PRE-005" }, Session = TestData.Session()
        });
        foreach (var id in new[] { "PRE-004", "PRE-005" })
        {
            var row = plan.Rows.Single(r => r.ControlId == id);
            Assert.DoesNotContain(row.Action, new[] { PlanAction.Create, PlanAction.Update });
        }
        // No second group named for the new requirement is created.
        Assert.DoesNotContain(plan.Rows, r => r.Action == PlanAction.Create);
    }

    [Fact]
    public void Owned_group_drift_is_not_explained_as_a_conditional_access_operator_exclusion()
    {
        var current = Load("2026.09.30");
        var (snapshot, mappings) = BuiltOnRelease10(current);
        var result = new AssessmentEngine(new FixedClock(), "test").Assess(snapshot, current, TestData.Profile(), mappings, Array.Empty<Deviation>(), "test");
        var mam = result.Findings.Single(f => f.ControlId == "PRE-004");
        Assert.True(mam.Owned);
        Assert.DoesNotContain("operator", mam.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
