using System.Text.Json.Nodes;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Evidence;
using BDIT.TenantToolkit.Engine.Execution;
using BDIT.TenantToolkit.Engine.Planning;
using BDIT.TenantToolkit.Engine.Recovery;
using Xunit;
using Harness = BDIT.TenantToolkit.Tests.RecoveryTests.Harness;

namespace BDIT.TenantToolkit.Tests;

public sealed class HistoricalDigestTests
{
    // The pre-preview.9 wire shape had neither metadata member. This fixture projector is deliberately separate
    // from the production compatibility helper and produces the digest recorded by that historical model.
    private static string PrePreview9Digest(StandardCatalogue standard)
    {
        var node = ToolkitJson.ToNode(standard)!.AsObject();
        foreach (var parameter in node["parameters"]!.AsArray().OfType<JsonObject>())
        {
            parameter.Remove("default");
            parameter.Remove("reviewedOn");
        }
        foreach (var control in node["controls"]!.AsArray().OfType<JsonObject>())
        {
            control.Remove("exclusionRole");
            control.Remove("prerequisites");
        }
        return CanonicalJson.Sha256(node);
    }

    [Theory]
    [InlineData("2026.09.3")]
    [InlineData("2026.09.4")]
    [InlineData("2026.09.5")]
    [InlineData("2026.09.6")]
    [InlineData("2026.09.7")]
    [InlineData("2026.09.8")]
    public void Legacy_catalogue_digest_recognises_unchanged_shipped_releases_only(string release)
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "standards"));
        var standard = BDIT.TenantToolkit.Engine.Standards.StandardsLoader.Parse(
            File.ReadAllText(Path.Combine(directory, release + ".json")), release + ".json");
        var original = PrePreview9Digest(standard);
        Assert.NotEqual(original, CanonicalJson.Sha256Value(standard));
        Assert.True(StandardDigestCompatibility.MatchesForReadOnlyVerification(standard, original));
        Assert.True(StandardDigestCompatibility.MatchesForReadOnlyVerification(standard, CanonicalJson.Sha256Value(standard)));
        standard.Controls.First(c => c.Payload is not null).Payload!["displayName"] = "Changed after approval";
        Assert.False(StandardDigestCompatibility.MatchesForReadOnlyVerification(standard, original));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("reviewedOn")]
    [InlineData("exclusionRole")]
    [InlineData("prerequisites")]
    [InlineData("release")]
    [InlineData("schema")]
    public void Legacy_projection_never_hides_new_semantics_or_accepts_an_unknown_release(string change)
    {
        var standard = TestData.Standard();
        standard.Release = "2026.09.6";
        var original = PrePreview9Digest(standard);
        Assert.True(StandardDigestCompatibility.MatchesForReadOnlyVerification(standard, original));
        var node = ToolkitJson.ToNode(standard)!.AsObject();
        switch (change)
        {
            case "default": node["parameters"]![0]!["default"] = "A new default"; break;
            case "reviewedOn": node["parameters"]![0]!["reviewedOn"] = "2026-09-15"; break;
            case "exclusionRole": node["controls"]![0]!["exclusionRole"] = "users"; break;
            case "prerequisites": node["controls"]![0]!["prerequisites"] = new JsonArray(); break;
            case "release": node["release"] = "2026.09.9"; break;
            case "schema": node["schemaVersion"] = 4; break;
        }
        var changed = ToolkitJson.Deserialize<StandardCatalogue>(node.ToJsonString());
        Assert.False(StandardDigestCompatibility.MatchesForReadOnlyVerification(changed, original));
        // Even a digest recomputed by blindly dropping the new fields must not authorise their omission.
        Assert.False(StandardDigestCompatibility.MatchesForReadOnlyVerification(changed, PrePreview9Digest(changed)));
    }

    [Theory]
    [InlineData("2026.09.3", false)]
    [InlineData("2026.09.6", false)]
    [InlineData("2026.09.6", true)]
    public async Task Accepted_historical_recovery_is_reverified_without_rewriting_or_repeating_it(string release, bool changedStandard)
    {
        var standard = TestData.Standard(); standard.Release = release;
        using var h = new Harness(standard);
        var source = await h.Deploy();
        var preview = await h.Preview(source);
        // Seed a separate immutable historical preview/run, simulating an old accepted DELETE whose readback lagged.
        preview.Id = Guid.NewGuid().ToString(); preview.StandardDigest = PrePreview9Digest(standard);
        h.Evidence.SaveRecoveryPlan(preview);
        var run = new RecoveryRun
        {
            Id = preview.Id, TenantId = h.Session.TenantId, SourceRunId = source.Id,
            ControlId = preview.ControlId, ObjectId = preview.ObjectId, Action = preview.Action,
            PlanDigest = preview.IntegrityDigest, WriteAcceptance = WriteAcceptance.Accepted,
            Verification = ConfigurationVerification.Unknown, Status = RunStatus.ReviewRequired
        };
        h.Evidence.SaveRecoveryRun(run);
        var originalPlan = ToolkitJson.Serialize(h.Evidence.RequireRecoveryPlan(run.TenantId, run.Id));
        var originalRun = ToolkitJson.Serialize(h.Evidence.LoadRecoveryRuns(run.TenantId).Single());
        h.Graph.Collection(standard.Collections[preview.Collection].BasePath).Clear();
        h.Graph.Mode = SessionMode.Assessment; h.Session.Mode = SessionMode.Assessment;
        Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedRecovery(run.TenantId, new[] { run.ControlId }));
        var service = new WriteVerificationService(h.Evidence, h.Clock);
        if (changedStandard)
        {
            standard.Controls[0].Name += " changed";
            await Assert.ThrowsAsync<SafetyViolationException>(() => service.VerifyRecoveryAsync(h.Graph, h.Session, standard, run.Id, CancellationToken.None));
            Assert.Empty(h.Evidence.LoadVerifications(run.TenantId));
            Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedRecovery(run.TenantId, new[] { run.ControlId }));
        }
        else
        {
            Assert.True((await service.VerifyRecoveryAsync(h.Graph, h.Session, standard, run.Id, CancellationToken.None)).Verified);
            h.Evidence.AssertNoUnresolvedRecovery(run.TenantId, new[] { run.ControlId });
            Assert.Null(h.Evidence.LoadMappings(run.TenantId).Find(run.ControlId));
        }
        Assert.Equal(originalPlan, ToolkitJson.Serialize(h.Evidence.RequireRecoveryPlan(run.TenantId, run.Id)));
        Assert.Equal(originalRun, ToolkitJson.Serialize(h.Evidence.LoadRecoveryRuns(run.TenantId).Single()));
        Assert.Single(h.Graph.Writes); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Historical_tenant_change_keeps_acceptance_and_standard_gates(bool changedStandard, bool unknownAcceptance)
    {
        using var h = new Harness();
        h.Standard.Release = "2026.09.6";
        var plan = new ReviewedChangePlan
        {
            TenantId = h.Session.TenantId, OperatorId = h.Session.OperatorObjectId!, ClientId = h.Session.ClientId,
            StandardDigest = PrePreview9Digest(h.Standard), Kind = ReviewedChangeKind.DisableSms, ControlId = "ID-002",
            Api = GraphApi.V1, Path = ReviewedChangeSafety.AuthenticationPath + "Sms", Method = "PATCH",
            RequiredScope = "Policy.ReadWrite.AuthenticationMethod",
            Payload = new JsonObject { ["state"] = "disabled" }
        };
        h.Evidence.SaveReviewedChangePlan(plan);
        var run = new ReviewedChangeRun
        {
            Id = plan.Id, TenantId = plan.TenantId, ControlId = plan.ControlId, PlanDigest = plan.IntegrityDigest,
            WriteAcceptance = unknownAcceptance ? WriteAcceptance.Unknown : WriteAcceptance.Accepted,
            Verification = ConfigurationVerification.Unknown, Status = RunStatus.ReviewRequired
        };
        h.Evidence.SaveReviewedChangeRun(run);
        var originalPlan = ToolkitJson.Serialize(h.Evidence.RequireReviewedChangePlan(run.TenantId, run.Id));
        var originalRun = ToolkitJson.Serialize(h.Evidence.LoadReviewedChangeRuns(run.TenantId).Single());
        h.Graph.SetSingleton(plan.Path, new JsonObject { ["id"] = "Sms", ["state"] = "disabled" });
        h.Graph.Mode = SessionMode.Assessment; h.Session.Mode = SessionMode.Assessment;
        if (changedStandard) h.Standard.Controls[0].Name += " changed";
        var service = new ReviewedChangeService(h.Evidence, h.Clock);
        if (changedStandard || unknownAcceptance)
        {
            await Assert.ThrowsAsync<SafetyViolationException>(() => service.ReverifyAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None));
            Assert.Empty(h.Graph.Reads);
            Assert.Throws<SafetyViolationException>(() => h.Evidence.AssertNoUnresolvedReviewedChanges(run.TenantId));
        }
        else
        {
            Assert.True((await service.ReverifyAsync(h.Graph, h.Session, h.Standard, run.Id, CancellationToken.None)).Verified);
            h.Evidence.AssertNoUnresolvedReviewedChanges(run.TenantId);
        }
        Assert.Equal(originalPlan, ToolkitJson.Serialize(h.Evidence.RequireReviewedChangePlan(run.TenantId, run.Id)));
        Assert.Equal(originalRun, ToolkitJson.Serialize(h.Evidence.LoadReviewedChangeRuns(run.TenantId).Single()));
        Assert.Empty(h.Graph.Writes); Assert.Empty(h.Graph.RecoveryWrites);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void Historical_and_current_plan_rows_preserve_absent_false_and_true_digest_members(bool? flag)
    {
        var plan = new DeploymentPlan { Id = "historical-synthetic", ToolkitVersion = "1.1.0-preview.6",
            Rows = { new PlanRow { ControlId = "CA-001", RecordedUsesDefaultInputs = flag } } };
        var original = ToolkitJson.ToNode(plan)!.AsObject();
        if (flag is null) original["rows"]![0]!.AsObject().Remove("usesDefaultInputs");
        original["planDigest"] = "";
        var digest = CanonicalJson.Sha256(original);
        original["planDigest"] = digest;
        var loaded = ToolkitJson.Deserialize<DeploymentPlan>(original.ToJsonString());
        Assert.Equal(flag, loaded.Rows[0].RecordedUsesDefaultInputs);
        Assert.Equal(flag ?? false, loaded.Rows[0].UsesDefaultInputs);
        Assert.Equal(digest, DeploymentPlanner.ComputeDigest(loaded));
        Assert.Equal(CanonicalJson.Serialize(original), CanonicalJson.SerializeValue(loaded));
        loaded.Rows[0].UsesDefaultInputs = !(flag ?? false);
        Assert.NotEqual(digest, DeploymentPlanner.ComputeDigest(loaded));
    }
}
