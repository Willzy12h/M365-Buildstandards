using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Planning;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public class PlannerTests
{
    private static readonly FixedClock Clock = new();
    private static readonly DeploymentPlanner Planner = new(Clock, "test");

    private static DeploymentPlan Build(StandardCatalogue standard, TenantSnapshot snapshot, TenantProfile? profile = null, ManagedObjectMappings? mappings = null,
        TenantSession? session = null, IEnumerable<string>? ids = null, IReadOnlyList<Deviation>? deviations = null) =>
        Planner.Build(new PlanRequest
        {
            Profile = profile ?? TestData.Profile(),
            Standard = standard,
            Snapshot = snapshot,
            Mappings = mappings ?? TestData.Mappings(),
            Deviations = deviations ?? Array.Empty<Deviation>(),
            SelectedControlIds = (ids ?? new[] { "CA-001", "CA-003", "CMP-WIN-001", "ID-001" }).ToList(),
            Session = session ?? TestData.Session()
        });

    private static PlanValidationContext Context(StandardCatalogue standard, TenantSnapshot snapshot, TenantProfile? profile = null, ManagedObjectMappings? mappings = null,
        TenantSession? session = null, string? acknowledged = null, DateTimeOffset? now = null) => new()
    {
        Profile = profile ?? TestData.Profile(),
        Standard = standard,
        Snapshot = snapshot,
        Mappings = mappings ?? TestData.Mappings(),
        Session = session ?? TestData.Session(),
        AcknowledgedSnapshotId = acknowledged ?? snapshot.Id,
        Now = now ?? Clock.UtcNow
    };

    [Fact]
    public void Conditional_access_candidates_are_disabled_with_emergency_and_operator_excluded_once()
    {
        var standard = TestData.Standard();
        var plan = Build(standard, TestData.Snapshot(standard));
        foreach (var row in plan.Rows.Where(r => r.Collection == "conditionalAccess"))
        {
            Assert.Equal(PlanAction.Create, row.Action);
            Assert.Equal("disabled", ConditionalAccessSafety.State(row.Payload!));
            var excluded = ConditionalAccessSafety.ExcludedUsers(row.Payload!);
            Assert.Contains(TestData.Emergency, excluded);
            Assert.Equal(1, excluded.Count(u => u == TestData.Operator));
            Assert.Equal(TestData.Operator, row.OperatorExclusion!.ObjectId);
            Assert.Contains(row.Warnings, w => w.Contains("does not expire", StringComparison.Ordinal));
        }
        Assert.Equal(PlanAction.Create, plan.Rows.First(r => r.ControlId == "CMP-WIN-001").Action);
        Assert.False(plan.Rows.First(r => r.ControlId == "CMP-WIN-001").Payload!.ContainsKey("assignments"));
        Assert.Equal(PlanAction.Manual, plan.Rows.First(r => r.ControlId == "ID-001").Action);
    }

    [Fact]
    public void Operator_already_listed_as_emergency_is_not_duplicated()
    {
        var standard = TestData.Standard();
        var profile = ProfileValidator.Validate(new TenantProfile { Company = "c", TenantId = TestData.TenantA, Parameters = new TenantParameters { EmergencyAccountIds = new List<string> { TestData.Operator }, OfficeLocationId = TestData.Office } }, Clock.UtcNow);
        var plan = Build(standard, TestData.Snapshot(standard), profile, ids: new[] { "CA-001" });
        var excluded = ConditionalAccessSafety.ExcludedUsers(plan.Rows[0].Payload!);
        Assert.Single(excluded);
        Assert.Equal(TestData.Operator, excluded[0]);
    }

    [Fact]
    public void Unverified_or_missing_operator_blocks_conditional_access_but_not_intune()
    {
        var standard = TestData.Standard();
        var plan = Build(standard, TestData.Snapshot(standard), session: TestData.Session(operatorVerified: false));
        Assert.Equal(PlanAction.Blocked, plan.Rows.First(r => r.ControlId == "CA-001").Action);
        Assert.Equal(PlanAction.Create, plan.Rows.First(r => r.ControlId == "CMP-WIN-001").Action);

        var none = Build(standard, TestData.Snapshot(standard), session: TestData.Session(operatorId: null));
        Assert.Equal(PlanAction.Blocked, none.Rows.First(r => r.ControlId == "CA-001").Action);
        Assert.Null(none.Rows.First(r => r.ControlId == "CA-001").Payload);
    }

    [Fact]
    public void Missing_emergency_accounts_block_conditional_access()
    {
        var standard = TestData.Standard();
        var plan = Build(standard, TestData.Snapshot(standard), TestData.Profile(emergency: false), ids: new[] { "CA-001" });
        Assert.Equal(PlanAction.Blocked, plan.Rows[0].Action);
        Assert.Contains("emergencyAccountIds", plan.Rows[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Catalogue_tampering_cannot_produce_an_enabled_candidate()
    {
        var standard = TestData.Standard();
        standard.FindControl("CA-001")!.Payload!["state"] = "enabled";
        var plan = Build(standard, TestData.Snapshot(standard), ids: new[] { "CA-001" });
        Assert.Equal(PlanAction.Create, plan.Rows[0].Action);
        Assert.Equal("disabled", ConditionalAccessSafety.State(plan.Rows[0].Payload!));
        var tampered = (JsonObject)plan.Rows[0].Payload!.DeepClone();
        tampered["state"] = "enabled";
        Assert.Throws<SafetyViolationException>(() => ConditionalAccessSafety.AssertSafeCandidate(tampered));
    }

    [Fact]
    public void Incomplete_collection_blocks_creation()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Status = CaptureStatus.Error;
        var plan = Build(standard, snapshot, ids: new[] { "CA-001" });
        Assert.Equal(PlanAction.Blocked, plan.Rows[0].Action);
    }

    [Fact]
    public void Unmanaged_same_name_or_overlapping_object_is_a_conflict_never_adopted()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy("p1", "BDIT - CA-001 - Require MFA", "enabled", Array.Empty<string>(), withLocation: false));
        Assert.Equal(PlanAction.Conflict, Build(standard, snapshot, ids: new[] { "CA-001" }).Rows[0].Action);

        var overlap = TestData.Snapshot(standard);
        overlap.Collections["conditionalAccess"].Items.Add(TestData.ConditionalAccessPolicy("p2", "Old MFA", "enabled", new[] { TestData.Emergency }));
        var row = Build(standard, overlap, ids: new[] { "CA-001" }).Rows[0];
        Assert.Equal(PlanAction.Conflict, row.Action);
        Assert.Contains("Old MFA", row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Owned_object_lifecycle_no_change_update_drift_active_and_missing()
    {
        var standard = TestData.Standard();
        var first = Build(standard, TestData.Snapshot(standard), ids: new[] { "CA-001" });
        var applied = first.Rows[0].Payload!;
        var mappings = TestData.Mappings();
        mappings.ByControl["CA-001"] = new ManagedObjectMapping { ControlId = "CA-001", ObjectId = "owned-1", Collection = "conditionalAccess", LastApplied = (JsonObject)applied.DeepClone(), OperatorExclusion = first.Rows[0].OperatorExclusion };

        var live = (JsonObject)applied.DeepClone();
        live["id"] = "owned-1";
        live["createdDateTime"] = "2026-01-01T00:00:00Z";
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["conditionalAccess"].Items.Add(live);
        Assert.Equal(PlanAction.NoChange, Build(standard, snapshot, mappings: mappings, ids: new[] { "CA-001" }).Rows[0].Action);

        var updated = (JsonObject)mappings.ByControl["CA-001"].LastApplied!.DeepClone();
        updated["conditions"]!["clientAppTypes"] = new JsonArray("browser");
        var staleMappings = TestData.Mappings();
        staleMappings.ByControl["CA-001"] = new ManagedObjectMapping { ControlId = "CA-001", ObjectId = "owned-1", Collection = "conditionalAccess", LastApplied = updated };
        var stale = TestData.Snapshot(standard);
        var liveOld = (JsonObject)updated.DeepClone();
        liveOld["id"] = "owned-1";
        stale.Collections["conditionalAccess"].Items.Add(liveOld);
        var updateRow = Build(standard, stale, mappings: staleMappings, ids: new[] { "CA-001" }).Rows[0];
        Assert.Equal(PlanAction.Update, updateRow.Action);
        Assert.Equal("owned-1", updateRow.ObjectId);
        Assert.Equal("disabled", ConditionalAccessSafety.State(updateRow.Payload!));

        var drifted = TestData.Snapshot(standard);
        var liveDrift = (JsonObject)live.DeepClone();
        liveDrift["conditions"]!["users"]!["excludeUsers"] = new JsonArray();
        drifted.Collections["conditionalAccess"].Items.Add(liveDrift);
        Assert.Equal(PlanAction.Drift, Build(standard, drifted, mappings: mappings, ids: new[] { "CA-001" }).Rows[0].Action);

        var active = TestData.Snapshot(standard);
        var liveActive = (JsonObject)live.DeepClone();
        liveActive["state"] = "enabled";
        active.Collections["conditionalAccess"].Items.Add(liveActive);
        Assert.Equal(PlanAction.Manual, Build(standard, active, mappings: mappings, ids: new[] { "CA-001" }).Rows[0].Action);

        Assert.Equal(PlanAction.Conflict, Build(standard, TestData.Snapshot(standard), mappings: mappings, ids: new[] { "CA-001" }).Rows[0].Action);
    }

    [Fact]
    public void Assigned_owned_intune_object_is_manual()
    {
        var standard = TestData.Standard();
        var first = Build(standard, TestData.Snapshot(standard), ids: new[] { "CMP-WIN-001" });
        var mappings = TestData.Mappings();
        mappings.ByControl["CMP-WIN-001"] = new ManagedObjectMapping { ControlId = "CMP-WIN-001", ObjectId = "c-1", Collection = "compliance", LastApplied = (JsonObject)first.Rows[0].Payload!.DeepClone() };
        var live = (JsonObject)first.Rows[0].Payload!.DeepClone();
        live["id"] = "c-1";
        live["_assignments"] = new JsonArray(new JsonObject { ["id"] = "a" });
        var snapshot = TestData.Snapshot(standard);
        snapshot.Collections["compliance"].Items.Add(live);
        Assert.Equal(PlanAction.Manual, Build(standard, snapshot, mappings: mappings, ids: new[] { "CMP-WIN-001" }).Rows[0].Action);
    }

    [Fact]
    public void Deviation_excludes_control_from_automation()
    {
        var standard = TestData.Standard();
        var deviation = new Deviation { Id = "d", TenantId = TestData.TenantA, ControlId = "CA-001", Reason = "Approved", ApprovedBy = "x" };
        var plan = Build(standard, TestData.Snapshot(standard), ids: new[] { "CA-001" }, deviations: new[] { deviation });
        Assert.Equal(PlanAction.Deviation, plan.Rows[0].Action);
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, TestData.Snapshot(standard))));
    }

    [Fact]
    public void Plan_digest_is_deterministic_and_detects_modification()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        var plan = Build(standard, snapshot);
        Assert.Equal(plan.PlanDigest, DeploymentPlanner.ComputeDigest(plan));
        Assert.Equal(DeploymentPlanner.ComputeDigest(plan), DeploymentPlanner.ComputeDigest(plan));
        DeploymentPlanner.Validate(plan, Context(standard, snapshot));

        plan.Rows[0].Payload!["state"] = "enabled";
        var ex = Assert.ThrowsAny<ToolkitException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot)));
        Assert.True(ex is PlanValidationException or SafetyViolationException);
    }

    [Fact]
    public void Plan_invalidated_by_profile_standard_snapshot_mapping_session_age_and_acknowledgement()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        var plan = Build(standard, snapshot);

        var changedProfile = TestData.Profile();
        changedProfile.Company = "Renamed";
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot, profile: changedProfile)));

        var changedStandard = TestData.Standard();
        changedStandard.Controls[0].Name = "Changed";
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(changedStandard, snapshot)));

        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, TestData.Snapshot(standard))));

        var changedMappings = TestData.Mappings();
        changedMappings.ByControl["X"] = new ManagedObjectMapping { ControlId = "X", ObjectId = "o" };
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot, mappings: changedMappings)));

        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot, session: TestData.Session(mode: SessionMode.Assessment))));
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot, session: TestData.Session(operatorId: TestData.Mam))));
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot, acknowledged: "other")));
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot, now: Clock.UtcNow.AddHours(2))));

        DeploymentPlanner.Validate(plan, Context(standard, snapshot));
    }

    [Fact]
    public void Plan_with_only_manual_rows_is_not_executable()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard);
        var plan = Build(standard, snapshot, ids: new[] { "ID-001" });
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot)));
    }

    [Fact]
    public void Write_guard_rejects_assignments_and_unsafe_state_before_any_write()
    {
        var standard = TestData.Standard();
        var ca = standard.Collections["conditionalAccess"];
        var payload = ToolkitJson.ParseObject("""{"displayName":"x","state":"enabledForReportingButNotEnforced","conditions":{}}""");
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(ca, payload));
        var compliance = standard.Collections["compliance"];
        var assigned = ToolkitJson.ParseObject("""{"displayName":"x","assignments":[]}""");
        Assert.Throws<SafetyViolationException>(() => WritePayloadGuard.Assert(compliance, assigned));
        WritePayloadGuard.Assert(compliance, ToolkitJson.ParseObject("""{"displayName":"x"}"""));
    }

    [Fact]
    public void Missing_required_licence_blocks_candidate_creation()
    {
        var standard = TestData.Standard();
        var snapshot = TestData.Snapshot(standard, withLicence: false);
        var row = Assert.Single(Build(standard, snapshot, ids: new[] { "CA-001" }).Rows);
        Assert.Equal(PlanAction.Blocked, row.Action);
        Assert.Contains("AAD_PREMIUM", row.Reason, StringComparison.Ordinal);
        Assert.Null(row.Payload);
    }

    [Fact]
    public void Missing_creation_reference_blocks_but_activation_prerequisite_remains_a_warning()
    {
        var standard = TestData.Standard();
        standard.FindControl("CA-001")!.Dependencies.Add("ID-001");
        var snapshot = TestData.Snapshot(standard);
        var row = Assert.Single(Build(standard, snapshot, ids: new[] { "CA-001" }).Rows);
        Assert.Equal(PlanAction.Create, row.Action);
        Assert.Contains(row.Warnings, w => w.Contains("Activation prerequisite ID-001", StringComparison.Ordinal));
        snapshot.Collections["namedLocations"].Items.Clear();
        snapshot.Collections["namedLocations"].Count = 0;
        row = Assert.Single(Build(standard, snapshot, ids: new[] { "CA-001" }).Rows);
        Assert.Equal(PlanAction.Blocked, row.Action);
        Assert.Contains("Creation prerequisite", row.Reason, StringComparison.Ordinal);
        Assert.Contains(TestData.Office, row.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Additional_exclusions_are_injected_and_must_resolve_in_the_tenant()
    {
        var standard = TestData.Standard();
        // Even a recipe without a template must preserve the reviewed exclusion accounts.
        standard.FindControl("CA-003")!.Payload!["conditions"]!["users"]!["excludeUsers"] = new JsonArray();
        var profile = TestData.Profile();
        profile.Parameters.AdditionalExclusionAccountIds.Add(TestData.Mam);
        var snapshot = TestData.Snapshot(standard);
        Assert.Equal(PlanAction.Blocked, Assert.Single(Build(standard, snapshot, profile, ids: new[] { "CA-003" }).Rows).Action);
        snapshot.Collections["users"].Items.Add(new JsonObject { ["id"] = TestData.Mam, ["displayName"] = "Chosen exclusion" });
        snapshot.Collections["users"].Count++;
        var row = Assert.Single(Build(standard, snapshot, profile, ids: new[] { "CA-003" }).Rows);
        Assert.Equal(PlanAction.Create, row.Action);
        Assert.Contains(TestData.Mam, ConditionalAccessSafety.ExcludedUsers(row.Payload!));
        profile.Parameters.EmergencyAccountIds.Clear();
        Assert.Equal(PlanAction.Blocked, Assert.Single(Build(standard, snapshot, profile, ids: new[] { "CA-003" }).Rows).Action);
    }

    [Fact]
    public void Cached_source_digest_does_not_hide_in_memory_standard_changes()
    {
        var standard = TestData.Standard();
        standard.IntegrityDigest = "unchanged-source-file-digest";
        var snapshot = TestData.Snapshot(standard);
        var plan = Build(standard, snapshot);
        standard.Controls[0].Payload!["description"] = "Changed after review";
        Assert.Throws<PlanValidationException>(() => DeploymentPlanner.Validate(plan, Context(standard, snapshot)));
    }
}
