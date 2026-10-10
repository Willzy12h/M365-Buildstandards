using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Assessment;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// INT-051 initial slice and CLA-20261006-07: the shipped lineage into 2026.09.30 is verified, agrees with the
/// published catalogues, and explains ownership records made under earlier releases without moving them.
/// </summary>
public sealed class ReleaseLineageTests
{
    private static readonly string Standards = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../standards"));
    private static StandardCatalogue Catalogue(string release) => StandardsLoader.Parse(File.ReadAllText(Path.Combine(Standards, release + ".json")), release + ".json");
    private static ReleaseLineage Shipped() => ReleaseLineage.Load(Standards, StandardsManifest.Load(Standards), "2026.09.30")!;

    [Fact]
    public void Shipped_lineage_is_verified_and_agrees_with_every_published_catalogue_it_names()
    {
        var lineage = Shipped();
        var target = Catalogue("2026.09.30");
        Assert.Equal(new[] { "2026.09.9", "2026.09.10", "2026.09.11", "2026.09.12" }, lineage.Sources.Select(s => s.Release));
        foreach (var source in lineage.Sources)
        {
            var catalogue = Catalogue(source.Release);
            foreach (var relation in source.Relations)
            {
                Assert.Equal(catalogue.FindControl(relation.SourceControl)?.Name, relation.SourceName);
                Assert.Equal(relation.TargetControls.Select(id => target.FindControl(id)?.Name), relation.TargetNames);
            }
            foreach (var control in catalogue.Controls)
            {
                // "Unlisted keeps the same requirement" must never cover a control that left the target, or an ID
                // whose name changed: each of those is listed with its real relation.
                var listed = source.Find(control.Id) is not null;
                var same = target.FindControl(control.Id) is { } now && now.Name == control.Name;
                Assert.True(listed || same, $"{source.Release} {control.Id} ({control.Name}) needs an explicit lineage relation.");
            }
        }
    }

    [Fact]
    public void Changed_unlisted_or_mis_pinned_lineage_is_refused()
    {
        using var root = new TempRoot();
        CopyStandards(root);
        var lineageFile = Path.Combine(root.Paths.StandardsDirectory, "lineage", "lineage-2026.09.30.json");
        var original = File.ReadAllText(lineageFile);
        var manifest = StandardsManifest.Load(root.Paths.StandardsDirectory);
        Assert.NotNull(ReleaseLineage.Load(root.Paths.StandardsDirectory, manifest, "2026.09.30"));

        File.WriteAllText(lineageFile, original.Replace("\"Renamed\"", "\"Changed\"", StringComparison.Ordinal));
        Assert.Throws<IntegrityException>(() => ReleaseLineage.Load(root.Paths.StandardsDirectory, manifest, "2026.09.30"));
        File.WriteAllText(lineageFile, original);

        manifest.Files["2026.09.10.json"] = new string('0', 64);
        Assert.Throws<IntegrityException>(() => ReleaseLineage.Load(root.Paths.StandardsDirectory, manifest, "2026.09.30"));
        Assert.Null(ReleaseLineage.Load(root.Paths.StandardsDirectory, StandardsManifest.Load(root.Paths.StandardsDirectory), "2026.09.12"));

        File.WriteAllText(Path.Combine(root.Paths.StandardsDirectory, "lineage", "manifest.json"), "{ not json");
        Assert.Throws<IntegrityException>(() => ReleaseLineage.Load(root.Paths.StandardsDirectory, StandardsManifest.Load(root.Paths.StandardsDirectory), "2026.09.30"));
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2")]
    [InlineData("\"unlistedControls\": \"sameRequirement\"", "\"unlistedControls\": \"sameRequirement\", \"extra\": true")]
    [InlineData("\"targetControls\": [\n            \"PRE-009\"", "\"targetControls\": [\n            \"PRE-010\"")]
    [InlineData("\"sourceControl\": \"PRE-002\"", "\"sourceControl\": \"PRE-001\"")]
    [InlineData("\"relation\": \"Renamed\"", "\"relation\": \"Retired\"")]
    [InlineData("\"cardinality\": \"Retired\"", "\"cardinality\": \"Added\"")]
    [InlineData("\"relation\": \"Renamed\"", "\"relation\": \"Added\"")]
    public void Malformed_or_conflicting_lineage_is_rejected(string find, string replace)
    {
        var json = File.ReadAllText(Path.Combine(Standards, "lineage", "lineage-2026.09.30.json"));
        Assert.Contains(find, json, StringComparison.Ordinal);
        Assert.Throws<IntegrityException>(() => ReleaseLineage.Parse(json.Replace(find, replace, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("\"target\": null, \"sources\": []")]
    [InlineData("\"target\": { \"release\": \"2026.09.30\", \"sha256\": \"\" }, \"sources\": null")]
    [InlineData("\"target\": { \"release\": \"2026.09.30\", \"sha256\": \"\" }, \"sources\": [ null ]")]
    [InlineData("\"target\": { \"release\": \"2026.09.30\", \"sha256\": \"\" }, \"sources\": [ { \"release\": \"2026.09.10\", \"sha256\": \"\", \"unlistedControls\": \"sameRequirement\", \"relations\": null } ]")]
    [InlineData("\"target\": { \"release\": \"2026.09.30\", \"sha256\": \"\" }, \"sources\": [ { \"release\": \"2026.09.10\", \"sha256\": \"\", \"unlistedControls\": \"sameRequirement\", \"relations\": [ { \"sourceControl\": \"PRE-004\", \"sourceName\": \"a\", \"relation\": \"Renamed\", \"cardinality\": \"OneToOne\", \"targetControls\": [ \"PRE-005\" ], \"targetNames\": null, \"semanticId\": \"s\", \"reason\": \"r\" } ] } ]")]
    public void Null_members_are_an_integrity_failure(string body)
    {
        Assert.Throws<IntegrityException>(() => ReleaseLineage.Parse("{ \"schemaVersion\": 1, \"description\": \"\", " + body + " }"));
    }

    [Fact]
    public void Release_10_records_under_release_30_are_explained_without_moving_them()
    {
        var current = Catalogue("2026.09.30");
        var mappings = new ManagedObjectMappings { TenantId = TestData.TenantA };
        mappings.ByControl["PRE-004"] = new ManagedObjectMapping { ControlId = "PRE-004", ObjectId = "aaaaaaaa-0000-4000-8000-000000000004", Collection = "groups", Release = "2026.09.10" };
        mappings.ByControl["PRE-005"] = new ManagedObjectMapping { ControlId = "PRE-005", ObjectId = "aaaaaaaa-0000-4000-8000-000000000005", Collection = "namedLocations", Release = "2026.09.10" };
        mappings.ByControl["ID-001"] = new ManagedObjectMapping { ControlId = "ID-001", ObjectId = "aaaaaaaa-0000-4000-8000-000000000006", Collection = "x", Release = "2026.09.10" };
        mappings.ByControl["ID-002"] = new ManagedObjectMapping { ControlId = "ID-002", ObjectId = "aaaaaaaa-0000-4000-8000-000000000007", Collection = "x", Release = "2026.09.30" };
        var before = ToolkitJson.Serialize(mappings);

        var notes = LineageReview.Review(mappings, current, Shipped());
        Assert.Equal(new[] { "PRE-004", "PRE-005" }, notes.Select(n => n.MappedControlId));
        var pilot = notes.Single(n => n.MappedControlId == "PRE-004");
        Assert.Contains("PRE-005 (GRP - Pilot Devices)", pilot.Message);
        Assert.Contains("PRE-004 now means GRP - MAM Only Users", pilot.Message);
        Assert.Contains("has not been moved", pilot.Message);
        var office = notes.Single(n => n.MappedControlId == "PRE-005");
        Assert.Contains("cannot be attributed to an instance of PRE-008", office.Message);
        Assert.Contains("PRE-005 now means GRP - Pilot Devices", office.Message);
        Assert.Equal(before, ToolkitJson.Serialize(mappings));
    }

    [Fact]
    public void A_record_from_a_release_without_lineage_is_flagged_for_review()
    {
        var mappings = new ManagedObjectMappings { TenantId = TestData.TenantA };
        mappings.ByControl["PRE-001"] = new ManagedObjectMapping { ControlId = "PRE-001", ObjectId = "aaaaaaaa-0000-4000-8000-000000000001", Release = "2026.09.8" };
        var note = Assert.Single(LineageReview.Review(mappings, Catalogue("2026.09.30"), Shipped()));
        Assert.Equal("Unknown", note.Relation);
        Assert.Contains("no verified lineage from 2026.09.8 to 2026.09.30 is available", note.Message);
        Assert.Single(LineageReview.Review(mappings, Catalogue("2026.09.30"), lineage: null));

        // Many records from one such release give one limitation line, not one each.
        mappings.ByControl["ID-001"] = new ManagedObjectMapping { ControlId = "ID-001", ObjectId = "aaaaaaaa-0000-4000-8000-000000000002", Release = "2026.09.8" };
        var result = new AssessmentResult { Release = "2026.09.30" };
        LineageReview.Annotate(result, LineageReview.Review(mappings, Catalogue("2026.09.30"), Shipped()));
        var line = Assert.Single(result.Limitations);
        Assert.Contains("2 ownership record(s) were made under 2026.09.8", line);
    }

    [Fact]
    public void Shared_assessment_explains_reused_ids_but_changes_no_status()
    {
        using var root = new TempRoot();
        CopyStandards(root);
        var current = new StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.30.json");
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var profile = TestData.Profile();
        store.SaveProfiles([profile]);
        var mappings = new ManagedObjectMappings { TenantId = profile.TenantId };
        mappings.ByControl["PRE-004"] = new ManagedObjectMapping { ControlId = "PRE-004", ObjectId = "aaaaaaaa-0000-4000-8000-000000000004", Collection = "groups", Release = "2026.09.10",
            LastApplied = new JsonObject(), LastAppliedDigest = CanonicalJson.Sha256(new JsonObject()) };
        store.SaveMappings(mappings);
        var snapshot = TestData.Snapshot(current);
        var engine = new AssessmentEngine(new FixedClock(), "test");

        var plain = engine.Assess(snapshot, current, profile, store.LoadMappings(profile.TenantId), store.LoadDeviations(profile.TenantId), "test");
        var shared = AssessmentContext.Assess(engine, store, snapshot, current, profile, "test");
        Assert.Equal(plain.Findings.Select(f => (f.ControlId, f.Status, f.Owned)), shared.Findings.Select(f => (f.ControlId, f.Status, f.Owned)));
        // The renamed-requirement wording comes only from the shipped lineage, so this proves it was loaded and used,
        // not that the no-lineage fallback ran.
        var reason = shared.Findings.Single(f => f.ControlId == "PRE-004").Reason;
        Assert.StartsWith("Release lineage: Review needed", reason);
        Assert.Contains("PRE-005 (GRP - Pilot Devices)", reason);
        Assert.DoesNotContain("no verified lineage", reason);
        Assert.Contains(shared.Limitations, l => l.StartsWith("Release lineage: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Lineage_for_other_catalogue_bytes_is_reported_and_not_used()
    {
        using var root = new TempRoot();
        CopyStandards(root);
        var current = new StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.30.json");
        current.IntegrityDigest = new string('0', 64);
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var profile = TestData.Profile();
        store.SaveProfiles([profile]);
        var mappings = new ManagedObjectMappings { TenantId = profile.TenantId };
        mappings.ByControl["PRE-004"] = new ManagedObjectMapping { ControlId = "PRE-004", ObjectId = "aaaaaaaa-0000-4000-8000-000000000004", Collection = "groups", Release = "2026.09.10" };
        store.SaveMappings(mappings);

        var result = AssessmentContext.Assess(new AssessmentEngine(new FixedClock(), "test"), store, TestData.Snapshot(current), current, profile, "test");
        Assert.Contains(result.Limitations, l => l.Contains("the loaded catalogue's bytes differ", StringComparison.Ordinal));
        Assert.Contains("no verified lineage from 2026.09.10 to 2026.09.30", result.Findings.Single(f => f.ControlId == "PRE-004").Reason);
    }

    [Theory]
    [InlineData("{ \"algorithm\": \"SHA-256\", \"files\": { \"lineage-2026.09.30.json\": 42 } }")]
    [InlineData("{ \"algorithm\": 1, \"files\": {} }")]
    [InlineData("{ \"algorithm\": \"SHA-256\", \"files\": [] }")]
    public void A_malformed_lineage_manifest_is_an_integrity_failure_and_does_not_stop_assessment(string manifestJson)
    {
        using var root = new TempRoot();
        CopyStandards(root);
        File.WriteAllText(Path.Combine(root.Paths.StandardsDirectory, "lineage", "manifest.json"), manifestJson);
        Assert.Throws<IntegrityException>(() => ReleaseLineage.Load(root.Paths.StandardsDirectory, StandardsManifest.Load(root.Paths.StandardsDirectory), "2026.09.30"));

        var current = new StandardsLoader(root.Paths, NullLog.Instance).Load("2026.09.30.json");
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var profile = TestData.Profile();
        store.SaveProfiles([profile]);
        var result = AssessmentContext.Assess(new AssessmentEngine(new FixedClock(), "test"), store, TestData.Snapshot(current), current, profile, "test");
        Assert.Contains(result.Limitations, l => l.StartsWith("Release lineage could not be verified and was not used", StringComparison.Ordinal));
    }

    private static void CopyStandards(TempRoot root)
    {
        foreach (var file in Directory.GetFiles(Standards, "*.json")) File.Copy(file, Path.Combine(root.Paths.StandardsDirectory, Path.GetFileName(file)), overwrite: true);
        var lineage = Path.Combine(root.Paths.StandardsDirectory, "lineage");
        Directory.CreateDirectory(lineage);
        foreach (var file in Directory.GetFiles(Path.Combine(Standards, "lineage"))) File.Copy(file, Path.Combine(lineage, Path.GetFileName(file)));
    }
}
