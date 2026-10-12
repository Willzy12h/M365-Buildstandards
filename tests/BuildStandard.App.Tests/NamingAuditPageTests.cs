using System.Text.Json.Nodes;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Json;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Naming;
using BuildStandard.Engine.Evidence;
using Xunit;

namespace BuildStandard.Tests;

public sealed class NamingAuditPageTests
{
    [Fact]
    public void Failed_repeat_review_notifies_cleared_acceptance_and_validates_presentation_filters()
    {
        using var f = new ConnectedReportsFixture(false);
        var snapshot = TestData.Snapshot(f.Workspace.RequireStandard());
        f.Set(nameof(f.Workspace.Snapshot), snapshot);
        var shell = new ShellViewModel(f.Workspace); var page = new NamingAuditViewModel(shell);
        page.Review(); Assert.Contains(snapshot.Id, page.Provenance);
        page.CollectionFilter = null!; page.OwnershipFilter = "not-a-filter";
        Assert.Equal("All", page.CollectionFilter); Assert.Equal("All", page.OwnershipFilter);
        var notifications = new List<string?>();
        page.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        var mappingFile = Path.Combine(f.Root.Paths.TenantDirectory(TestData.TenantA), "managed-objects.json");
        Directory.CreateDirectory(Path.GetDirectoryName(mappingFile)!); File.WriteAllText(mappingFile, "{ damaged mapping");
        page.ReviewCommand.Execute(null);
        Assert.True(shell.HasError); Assert.Empty(page.Rows); Assert.Empty(page.Collections);
        Assert.Equal("No reviewed capture.", page.Provenance);
        Assert.Equal("No naming audit accepted for the current capture.", page.Summary);
        Assert.Contains(nameof(NamingAuditViewModel.Summary), notifications);
        Assert.Contains(nameof(NamingAuditViewModel.Provenance), notifications);
        Assert.Equal("All", Assert.Single(page.CollectionFilters));
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }

    [Theory]
    [InlineData(true, true, NamingAudit.Managed)]
    [InlineData(false, true, NamingAudit.OwnershipUnknown)]
    [InlineData(true, false, NamingAudit.OwnershipUnknown)]
    public void Ownership_display_requires_corroborated_creation_and_intact_source(bool includeRun, bool hashSource, string expected)
    {
        using var f = new ConnectedReportsFixture(false);
        var payload = new JsonObject { ["displayName"] = "GRP - Synthetic pilot" };
        var readback = (JsonObject)payload.DeepClone(); readback["id"] = TestData.Mam;
        var run = new DeploymentRun { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, StartedAt = "2026-09-11T09:00:00Z", Results = [new RunResult
        {
            ControlId = "PRE-001", Collection = "groups", ObjectId = TestData.Mam, PlannedAction = "Create", WriteAcceptance = WriteAcceptance.Accepted,
            Configuration = ConfigurationVerification.Pass, WrittenPayload = payload, AfterObject = readback,
            PayloadDigest = CanonicalJson.Sha256(payload), ReadbackDigest = CanonicalJson.Sha256(readback), WrittenAt = "2026-09-11T09:00:00Z"
        }] };
        if (includeRun) f.Workspace.Evidence.SaveRun(run);
        f.Workspace.Evidence.SaveMappings(new ManagedObjectMappings { TenantId = TestData.TenantA, ByControl = new() { ["PRE-001"] = new ManagedObjectMapping
        { ControlId = "PRE-001", Collection = "groups", ObjectId = TestData.Mam, RunId = run.Id, LastApplied = payload, LastAppliedDigest = CanonicalJson.Sha256(payload) } } });
        var source = new TenantSnapshot { Id = Guid.NewGuid().ToString(), TenantId = TestData.TenantA, CapturedAt = "2026-09-11T10:00:00Z",
            Collections = new() { ["groups"] = new CollectionCapture { Status = CaptureStatus.Collected, Count = 1, Items = [readback] } } };
        if (hashSource) source.IntegrityDigest = EvidenceIntegrity.Compute(source);
        f.Set(nameof(f.Workspace.Snapshot), source);
        var page = new NamingAuditViewModel(new ShellViewModel(f.Workspace)); page.Review();
        var row = Assert.Single(page.Rows); Assert.Equal(expected, row.Ownership); Assert.NotEmpty(row.OwnershipReason);
        page.OwnershipFilter = expected; Assert.Single(page.Rows); page.OwnershipFilter = NamingAudit.Unmapped; Assert.Empty(page.Rows);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
    }
    [Fact]
    public void Audit_reviews_exact_offline_snapshot_without_collection_or_evidence_mutation()
    {
        using var f = new ConnectedReportsFixture(false);
        var snapshot = TestData.Snapshot(f.Workspace.RequireStandard());
        snapshot.Collections["groups"].Items.Add(new JsonObject { ["id"] = TestData.Emergency, ["displayName"] = "Synthetic observed name" });
        snapshot.Collections["groups"].Count = snapshot.Collections["groups"].Items.Count;
        snapshot.Collections["conditionalAccess"].Status = CaptureStatus.Error;
        snapshot.Collections["conditionalAccess"].Error = "Synthetic permission refusal";
        snapshot.IntegrityDigest = "";
        f.Set(nameof(f.Workspace.Snapshot), snapshot);
        var before = ToolkitJson.Serialize(snapshot);
        var page = new NamingAuditViewModel(new ShellViewModel(f.Workspace)); page.OnNavigatedTo();
        Assert.Empty(page.Rows); page.Review();
        var row = Assert.Single(page.Rows, r => r.Collection == "groups" && r.ObjectId == TestData.Emergency);
        Assert.Equal(NamingAudit.Unmapped, row.Ownership); Assert.Contains("name cannot establish ownership", row.OwnershipReason);
        page.SelectedRow = row; Assert.Contains(TestData.Emergency, page.SelectedDetails);
        Assert.Contains(page.Collections, c => c.ReadState == CaptureStatus.Error && c.Detail == "Synthetic permission refusal");
        Assert.Contains(snapshot.Id, page.Provenance); Assert.Contains(snapshot.CapturedAt, page.Summary);
        page.Search = "nothing-matches-this"; Assert.Empty(page.Rows);
        Assert.Equal(before, ToolkitJson.Serialize(snapshot)); Assert.Null(f.Workspace.Plan);
        Assert.Equal(0, f.Handler.Calls); Assert.Equal(0, f.Tokens.Calls);
        f.Workspace.ApplyProfileToSession(TestData.Profile(TestData.TenantB), save: false); page.Refresh();
        Assert.Empty(page.Rows); Assert.Contains("No naming audit", page.Summary);
    }

    [Fact]
    public void Audit_direct_guards_refuse_wrong_tenant_and_clear_results_after_capture_changes()
    {
        using var f = new ConnectedReportsFixture(false);
        f.Set(nameof(f.Workspace.Snapshot), TestData.Snapshot(f.Workspace.RequireStandard()));
        var page = new NamingAuditViewModel(new ShellViewModel(f.Workspace)); page.Review();
        f.Set(nameof(f.Workspace.Snapshot), TestData.Snapshot(f.Workspace.RequireStandard(), TestData.TenantB));
        Assert.ThrowsAny<ToolkitException>(page.Review); page.Refresh();
        Assert.Empty(page.Rows); Assert.Throws<TenantMismatchException>(page.Review);
        Assert.Equal(0, f.Handler.Calls);
    }
}
