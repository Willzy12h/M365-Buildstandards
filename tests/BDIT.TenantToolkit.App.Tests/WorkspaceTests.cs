using System.IO;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The workspace decides which client is selected, which capture is in hand and whether that capture may be deployed
/// from. Those decisions are safety decisions, and until this project existed none of them were tested: the engine had
/// 542 tests and the application had none, because the test project targets net8.0 and cannot reference WPF.
///
/// The invariant these protect is that evidence loaded for review is inert. An engineer opening yesterday's capture to
/// answer a question must not be able to plan or acknowledge a deployment from it, because the tenant has moved on and
/// the plan would be built against a tenant that no longer exists in that shape.
/// </summary>
public class WorkspaceTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly Workspace _workspace;

    public WorkspaceTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        _workspace.Initialise();
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Selecting the client is the first step; nothing downstream may assume one without it.</summary>
    [Fact]
    public void Assessment_refuses_before_a_client_is_selected()
    {
        var error = Assert.Throws<ToolkitException>(() => _workspace.RunAssessment());

        Assert.Contains("Select a client first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Assessment_refuses_before_a_capture_is_in_hand()
    {
        _workspace.ApplyProfileToSession(TestData.Profile(), save: false);

        var error = Assert.Throws<ToolkitException>(() => _workspace.RunAssessment());

        Assert.Contains("Read the tenant configuration first", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_this_installation_does_not_hold_is_refused_rather_than_guessed_at()
    {
        _workspace.ApplyProfileToSession(TestData.Profile(), save: false);

        var error = Assert.Throws<ToolkitException>(() => _workspace.LoadStoredSnapshot(Guid.NewGuid().ToString()));

        Assert.Contains("Snapshot not found", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The invariant. A stored capture is loaded for review, assessed, and is then not eligible to be acknowledged as
    /// the before-change snapshot for a deployment. Acknowledgement is what binds a deployment to the state the
    /// engineer actually reviewed, so accepting a stale capture there would let a write be approved against a tenant
    /// that has since changed.
    /// </summary>
    [Fact]
    public void A_capture_loaded_for_review_can_never_be_acknowledged_for_deployment()
    {
        var profile = TestData.Profile();
        _workspace.ApplyProfileToSession(profile, save: false);
        var standard = _workspace.Standard ?? throw new InvalidOperationException("The synthetic standard did not load.");
        var stored = TestData.Snapshot(standard);
        _workspace.Evidence.SaveSnapshot(stored);

        _workspace.LoadStoredSnapshot(stored.Id);

        Assert.False(_workspace.SnapshotIsLive);
        Assert.NotNull(_workspace.Assessment);
        var error = Assert.Throws<ToolkitException>(() => _workspace.AcknowledgeSnapshot());
        Assert.Contains("Only the live capture", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Loading a capture for review runs an assessment and keeps it as evidence, which is the offline path an engineer
    /// uses to answer a question about a client without connecting. It also clears any plan built earlier, so a plan
    /// can never outlive the capture it was built from.
    /// </summary>
    [Fact]
    public void Loading_a_stored_capture_assesses_it_and_records_the_assessment()
    {
        var profile = TestData.Profile();
        _workspace.ApplyProfileToSession(profile, save: false);
        var standard = _workspace.Standard!;
        var stored = TestData.Snapshot(standard);
        _workspace.Evidence.SaveSnapshot(stored);

        _workspace.LoadStoredSnapshot(stored.Id);

        var assessment = Assert.IsType<AssessmentResult>(_workspace.Assessment);
        Assert.Equal(stored.TenantId, assessment.TenantId);
        Assert.Null(_workspace.Plan);
        // The assessment is kept as evidence, not only held in memory, so the answer survives closing the application.
        var assessments = Path.Combine(_workspace.Evidence.TenantDirectory(profile.TenantId), "assessments");
        Assert.NotEmpty(Directory.EnumerateFiles(assessments, "*.json"));
    }

    /// <summary>
    /// Client evidence is partitioned by tenant and the store refuses to cross that line. Reaching it through the
    /// workspace proves the application cannot serve one client's capture while another client is selected.
    /// </summary>
    [Fact]
    public void A_capture_belonging_to_another_client_is_not_reachable()
    {
        var standard = TestData.Standard();
        var otherTenant = TestData.Snapshot(standard, TestData.TenantB);
        _workspace.Evidence.SaveSnapshot(otherTenant);
        _workspace.ApplyProfileToSession(TestData.Profile(), save: false);

        var error = Assert.Throws<ToolkitException>(() => _workspace.LoadStoredSnapshot(otherTenant.Id));

        Assert.Contains("Snapshot not found", error.Message, StringComparison.Ordinal);
    }
}
