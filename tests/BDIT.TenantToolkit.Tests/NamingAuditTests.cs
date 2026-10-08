using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Naming;
using BDIT.TenantToolkit.Engine.Standards;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class NamingAuditTests
{
    [Theory]
    [InlineData("GRP - Pilot devices", "groups", NamingConvention.Conforming)]
    [InlineData("grp - Pilot devices", "groups", NamingConvention.NonConforming)]
    [InlineData("GRP - ", "groups", NamingConvention.NonConforming)]
    [InlineData("GRP - Pilot\nDevices", "groups", NamingConvention.NonConforming)]
    [InlineData("GRP - Pilot  devices", "groups", NamingConvention.NonConforming)]
    [InlineData(null, "groups", NamingConvention.Unknown)]
    [InlineData("Unknown name", "unregistered", NamingConvention.Unknown)]
    [InlineData("CA - Require MFA", "conditionalAccess", NamingConvention.Conforming)]
    public void Naming_results_follow_internal_policy_without_inventing_service_limits(string? name, string collection, string state)
    {
        var result = NamingConvention.CheckName(name, collection);
        Assert.Equal(state, result.State);
        if (collection == "conditionalAccess") { Assert.Null(result.MicrosoftMaximum); Assert.Contains("unverified", result.Reason); }
    }

    [Fact]
    public void Documented_group_limit_and_explicit_platform_suffix_are_checked()
    {
        Assert.Equal(NamingConvention.NonConforming, NamingConvention.CheckName("GRP - " + new string('x', 251), "groups").State);
        Assert.Equal(NamingConvention.Conforming, NamingConvention.CheckName("GRP - " + new string('x', 250), "groups").State);
        var c = new ControlDefinition { Id = "CMP-WIN-001", Collection = "compliance" };
        Assert.Equal(NamingConvention.NonConforming, NamingConvention.CheckAuthored(c, "CMP - Core compliance").State);
        Assert.Equal(NamingConvention.Conforming, NamingConvention.CheckAuthored(c, "CMP - Core compliance - Windows").State);
        Assert.Throws<ConfigurationException>(() => NamingConvention.RequireAuthored(c, "M365 - Windows compliance"));
        NamingConvention.RequireAuthored(c, "CMP - Core compliance - Windows");
    }

    [Fact]
    public void Names_cannot_establish_ownership_and_mapping_without_creation_proof_is_unknown()
    {
        var (source, mappings, _) = Inputs(); mappings.ByControl.Clear();
        var first = Assert.Single(NamingAudit.Review(source, mappings, []).Objects);
        Assert.Equal(NamingAudit.Unmapped, first.Ownership); Assert.Equal(NamingConvention.Conforming, first.Naming.State);
        (_, mappings, _) = Inputs();
        Assert.Equal(NamingAudit.OwnershipUnknown, Assert.Single(NamingAudit.Review(source, mappings, []).Objects).Ownership);
    }

    [Theory]
    [InlineData("valid", NamingAudit.Managed)]
    [InlineData("unhashed-source", NamingAudit.OwnershipUnknown)]
    [InlineData("modified-run", NamingAudit.OwnershipUnknown)]
    [InlineData("unknown-write", NamingAudit.OwnershipUnknown)]
    [InlineData("after-capture", NamingAudit.OwnershipUnknown)]
    [InlineData("duplicate-mapping", NamingAudit.OwnershipUnknown)]
    public void Management_requires_bound_integrity_checked_creation_and_readback(string defect, string ownership)
    {
        var (source, mappings, run) = Inputs();
        switch (defect)
        {
            case "unhashed-source": source.IntegrityDigest = ""; break;
            case "modified-run": run.Results[0].ObjectId = TestData.Operator; break;
            case "unknown-write": run.Results[0].RecordedWriteAcceptance = null; run.IntegrityDigest = EvidenceIntegrity.Compute(run); break;
            case "after-capture": run.Results[0].WrittenAt = "2026-09-12T10:00:00Z"; run.IntegrityDigest = EvidenceIntegrity.Compute(run); break;
            case "duplicate-mapping": mappings.ByControl["DUP-001"] = mappings.ByControl["PRE-001"]; break;
        }
        Assert.Equal(ownership, Assert.Single(NamingAudit.Review(source, mappings, [run]).Objects).Ownership);
    }

    [Fact]
    public void Empty_success_missing_and_failed_reads_are_distinct_and_inputs_are_unchanged()
    {
        var (source, mappings, run) = Inputs();
        source.Collections["groups"].Items.Clear(); source.Collections["groups"].Count = 0;
        source.Collections["conditionalAccess"] = new CollectionCapture { Status = CaptureStatus.Error, Error = "Synthetic denied read" };
        source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        var before = ToolkitJson.Serialize(source); var mappingBytes = ToolkitJson.Serialize(mappings); var runBytes = ToolkitJson.Serialize(run);
        var result = NamingAudit.Review(source, mappings, [run]);
        Assert.Empty(result.Objects);
        Assert.Contains("Checked successfully; no objects", result.Collections.Single(c => c.Collection == "groups").Detail);
        Assert.Equal(CaptureStatus.Error, result.Collections.Single(c => c.Collection == "conditionalAccess").ReadState);
        Assert.Equal(CaptureStatus.NotAttempted, result.Collections.Single(c => c.Collection == "configuration").ReadState);
        Assert.Equal(before, ToolkitJson.Serialize(source)); Assert.Equal(mappingBytes, ToolkitJson.Serialize(mappings)); Assert.Equal(runBytes, ToolkitJson.Serialize(run));
    }

    [Fact]
    public void Duplicate_ids_do_not_get_clean_conforming_or_managed_results()
    {
        var (source, mappings, run) = Inputs();
        source.Collections["groups"].Items.Add((JsonObject)source.Collections["groups"].Items[0].DeepClone());
        source.Collections["groups"].Count = 2; source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        Assert.All(NamingAudit.Review(source, mappings, [run]).Objects, o =>
        { Assert.Equal(NamingConvention.Unknown, o.Naming.State); Assert.Equal(NamingAudit.OwnershipUnknown, o.Ownership); });
    }

    [Fact]
    public void Cross_tenant_and_modified_evidence_are_refused()
    {
        var (source, mappings, run) = Inputs(); mappings.TenantId = TestData.TenantB;
        Assert.Throws<TenantMismatchException>(() => NamingAudit.Review(source, mappings, [run]));
        mappings.TenantId = source.TenantId; source.TenantName = "Changed";
        Assert.Throws<IntegrityException>(() => NamingAudit.Review(source, mappings, [run]));
    }

    [Fact]
    public void Published_apps_collection_has_an_application_rule_for_authoring()
    {
        var rule = NamingConvention.ForCollection("apps");
        Assert.NotNull(rule);
        Assert.Equal("APP", rule.Prefix); Assert.Null(rule.MicrosoftMaximum);
        Assert.Equal("https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-mobileapp?view=graph-rest-1.0", rule.MicrosoftReference);
        var control = new ControlDefinition { Id = "APP-IOS-001", Collection = "apps" };
        Assert.Equal(NamingConvention.Conforming, NamingConvention.CheckAuthored(control, "APP - Company portal - iOS").State);
        Assert.Equal(NamingConvention.NonConforming, NamingConvention.CheckAuthored(control, "APP - Company portal").State);
        NamingConvention.RequireAuthored(control, "APP - Company portal - iOS");
    }

    [Fact]
    public void Apps_only_capture_from_older_releases_is_audited()
    {
        var item = new JsonObject { ["id"] = TestData.Mam, ["displayName"] = "Company portal" };
        var source = Capture(("apps", item));
        var result = NamingAudit.Review(source, new ManagedObjectMappings { TenantId = source.TenantId }, []);
        Assert.Equal(CaptureStatus.Collected, result.Collections.Single(c => c.Collection == "apps").ReadState);
        var row = Assert.Single(result.Objects);
        Assert.Equal("apps", row.Collection); Assert.Equal(NamingAudit.Unmapped, row.Ownership);
        Assert.Equal(NamingConvention.NonConforming, row.Naming.State);
    }

    [Theory]
    [InlineData("endpointProtection", "configuration", "CFG - Endpoint protection - Windows")]
    [InlineData("configuration", "endpointProtection", "CFG - Device restrictions - Windows")]
    [InlineData("extendedCompliance", "compliance", "CMP - Core compliance - Windows")]
    [InlineData("apps", "applications", "APP - Company portal - iOS")]
    [InlineData("iosProtection", "appProtection", "MAM - Managed applications - iOS")]
    [InlineData("appProtection", "androidProtection", "MAM - Managed applications - Android")]
    public void Overlapping_collection_reports_object_mapped_elsewhere_as_unknown_not_unmapped(string mappedCollection, string otherCollection, string name)
    {
        var (_, run, mappings) = CreationEvidence(mappedCollection, name);
        var readback = run.Results[0].AfterObject!;
        var rows = NamingAudit.Review(Capture((mappedCollection, readback), (otherCollection, readback)), mappings, [run]).Objects;
        Assert.Equal(NamingAudit.Managed, rows.Single(o => o.Collection == mappedCollection).Ownership);
        var other = rows.Single(o => o.Collection == otherCollection);
        Assert.Equal(NamingAudit.OwnershipUnknown, other.Ownership);
        Assert.Contains("Mapped under " + mappedCollection, other.OwnershipReason);
        Assert.Equal(NamingConvention.Conforming, other.Naming.State);
    }

    [Fact]
    public void Mapping_under_overlapping_collection_cannot_establish_management_of_another_capture()
    {
        var (_, run, mappings) = CreationEvidence("endpointProtection", "CFG - Endpoint protection - Windows");
        var row = Assert.Single(NamingAudit.Review(Capture(("configuration", run.Results[0].AfterObject!)), mappings, [run]).Objects);
        Assert.Equal(NamingAudit.OwnershipUnknown, row.Ownership);
        Assert.Contains("endpointProtection", row.OwnershipReason);
    }

    [Fact]
    public void Same_object_mapped_under_two_overlapping_collections_is_not_managed()
    {
        var (payload, run, mappings) = CreationEvidence("endpointProtection", "CFG - Endpoint protection - Windows");
        mappings.ByControl["CFG-WIN-010"] = new ManagedObjectMapping { ControlId = "CFG-WIN-010", Collection = "configuration", ObjectId = TestData.Mam,
            RunId = run.Id, LastApplied = payload, LastAppliedDigest = CanonicalJson.Sha256(payload) };
        var readback = run.Results[0].AfterObject!;
        var rows = NamingAudit.Review(Capture(("endpointProtection", readback), ("configuration", readback)), mappings, [run]).Objects;
        Assert.Equal(2, rows.Count);
        Assert.All(rows, o => { Assert.Equal(NamingAudit.OwnershipUnknown, o.Ownership); Assert.Contains("ambiguous", o.OwnershipReason); });
    }

    [Fact]
    public void Graph_families_match_the_published_catalogue_routes()
    {
        var standards = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));
        Assert.True(Directory.Exists(standards), "Shipped standards folder not found at " + standards);
        var checkedCollections = 0;
        foreach (var file in Directory.EnumerateFiles(standards, "*.json").Where(f => !f.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            var catalogue = StandardsLoader.Parse(File.ReadAllText(file), Path.GetFileName(file));
            var ruled = catalogue.Collections.Where(c => NamingConvention.ForCollection(c.Key) is not null).ToList();
            foreach (var (key, definition) in ruled)
            {
                var family = NamingConvention.ForCollection(key)!.GraphFamily;
                // iOS and Android protections are managedAppPolicy subtypes, also returned by the appProtection read.
                var expected = key is "iosProtection" or "androidProtection" ? catalogue.Collections["appProtection"].BasePath : definition.BasePath;
                Assert.True(family == expected, Path.GetFileName(file) + ": " + key + " reads " + definition.BasePath + " but its naming family is " + family);
                foreach (var (otherKey, otherDefinition) in ruled.Where(o => o.Key != key && o.Value.BasePath == definition.BasePath))
                    Assert.Contains(otherKey, NamingConvention.OverlappingCollections(key));
                checkedCollections++;
            }
        }
        Assert.True(checkedCollections > 0);
    }

    [Fact]
    public void Endpoint_protection_cites_the_device_configuration_resource_it_reads()
    {
        var rule = NamingConvention.ForCollection("endpointProtection")!;
        Assert.Equal("https://learn.microsoft.com/en-us/graph/api/resources/intune-deviceconfig-deviceconfiguration?view=graph-rest-1.0", rule.MicrosoftReference);
        Assert.Equal(NamingConvention.ForCollection("configuration")!.MicrosoftReference, rule.MicrosoftReference);
    }

    [Fact]
    public void Character_rule_reads_unicode_scalars_and_attached_marks()
    {
        // Built in code: xUnit theory data cannot carry an unpaired surrogate reliably.
        (string Name, string State)[] cases =
        [
            ("GRP - Cafe\u0301 team", NamingConvention.Conforming),
            ("GRP - Caf\u00e9 team", NamingConvention.Conforming),
            ("GRP - \U0001D400lpha team", NamingConvention.Conforming),
            ("GRP - \U00010437 team", NamingConvention.Conforming),
            ("GRP - \u0301Pilot", NamingConvention.NonConforming),
            ("GRP - Pilot \u0301devices", NamingConvention.NonConforming),
            ("GRP - Pilot\uD800 devices", NamingConvention.NonConforming),
            ("GRP - Pilot \U0001F600", NamingConvention.NonConforming)
        ];
        foreach (var (name, state) in cases)
            Assert.True(state == NamingConvention.CheckName(name, "groups").State, "Unexpected result for " + string.Join(" ", name.Select(c => ((int)c).ToString("X4"))));
    }

    [Fact]
    public void Group_limit_still_counts_utf16_units_before_normalisation()
    {
        var composed = "GRP - " + new string('\u00e9', 250);
        Assert.Equal(NamingConvention.Conforming, NamingConvention.CheckName(composed, "groups").State);
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        Assert.Equal(NamingConvention.NonConforming, NamingConvention.CheckName(decomposed, "groups").State);
        Assert.Contains("256", NamingConvention.CheckName(decomposed, "groups").Reason);
    }

    private static (TenantSnapshot, ManagedObjectMappings, DeploymentRun) Inputs()
    {
        var payload = new JsonObject { ["displayName"] = "GRP - Pilot devices" };
        var readback = (JsonObject)payload.DeepClone(); readback["id"] = TestData.Mam;
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, Results = [new RunResult
        {
            ControlId = "PRE-001", Collection = "groups", ObjectId = TestData.Mam, PlannedAction = "Create", WriteAcceptance = WriteAcceptance.Accepted,
            Configuration = ConfigurationVerification.Pass, WrittenPayload = payload, AfterObject = readback,
            PayloadDigest = CanonicalJson.Sha256(payload), ReadbackDigest = CanonicalJson.Sha256(readback), WrittenAt = "2026-09-11T09:00:00Z"
        }] };
        run.IntegrityDigest = EvidenceIntegrity.Compute(run);
        var source = new TenantSnapshot { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, CapturedAt = "2026-09-11T10:00:00Z",
            Collections = new() { ["groups"] = new CollectionCapture { Status = CaptureStatus.Collected, Count = 1, Items = [readback] } } };
        source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        var mappings = new ManagedObjectMappings { TenantId = source.TenantId, ByControl = new() { ["PRE-001"] = new ManagedObjectMapping
        { ControlId = "PRE-001", Collection = "groups", ObjectId = TestData.Mam, RunId = run.Id, LastApplied = payload, LastAppliedDigest = CanonicalJson.Sha256(payload) } } };
        return (source, mappings, run);
    }

    private static TenantSnapshot Capture(params (string Collection, JsonObject Item)[] items)
    {
        var source = new TenantSnapshot { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, CapturedAt = "2026-09-11T10:00:00Z",
            Collections = items.ToDictionary(i => i.Collection, i => new CollectionCapture { Status = CaptureStatus.Collected, Count = 1, Items = [(JsonObject)i.Item.DeepClone()] }) };
        source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        return source;
    }

    private static (JsonObject Payload, DeploymentRun Run, ManagedObjectMappings Mappings) CreationEvidence(string collection, string name)
    {
        var payload = new JsonObject { ["displayName"] = name };
        var readback = (JsonObject)payload.DeepClone(); readback["id"] = TestData.Mam;
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, Results = [new RunResult
        {
            ControlId = "CTL-001", Collection = collection, ObjectId = TestData.Mam, PlannedAction = "Create", WriteAcceptance = WriteAcceptance.Accepted,
            Configuration = ConfigurationVerification.Pass, WrittenPayload = payload, AfterObject = readback,
            PayloadDigest = CanonicalJson.Sha256(payload), ReadbackDigest = CanonicalJson.Sha256(readback), WrittenAt = "2026-09-11T09:00:00Z"
        }] };
        run.IntegrityDigest = EvidenceIntegrity.Compute(run);
        var mappings = new ManagedObjectMappings { TenantId = TestData.TenantA, ByControl = new() { ["CTL-001"] = new ManagedObjectMapping
        { ControlId = "CTL-001", Collection = collection, ObjectId = TestData.Mam, RunId = run.Id, LastApplied = payload, LastAppliedDigest = CanonicalJson.Sha256(payload) } } };
        return (payload, run, mappings);
    }
}
