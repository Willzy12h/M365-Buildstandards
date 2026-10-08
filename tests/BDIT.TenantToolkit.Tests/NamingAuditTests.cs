using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Naming;
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
}
