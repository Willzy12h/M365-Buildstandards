using System.Diagnostics;
using System.Net.Http;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.Services;
using BuildStandard.App.ViewModels;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using BuildStandard.Graph;
using BuildStandard.Graph.Auth;
using BuildStandard.Graph.Setup;
using Xunit;

namespace BuildStandard.Tests;

public sealed class OfflineClientJourneyTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    private readonly TenantProfile _a = Profile(TestData.TenantA);
    private readonly TenantProfile _b = Profile(TestData.TenantB);
    private readonly TenantSnapshot _capture;

    public OfflineClientJourneyTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        var store = new EvidenceStore(_root.Paths, NullLog.Instance);
        store.SaveProfiles(new[] { _a, _b });
        _capture = TestData.Snapshot(TestData.Standard()); store.SaveSnapshot(_capture);
        store.SaveSnapshot(TestData.Snapshot(TestData.Standard(), TestData.TenantB));
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = NewWorkspace(); _shell = new ShellViewModel(_workspace);
    }

    private static TenantProfile Profile(string tenant)
    {
        var profile = TestData.Profile(tenant); profile.Id = tenant == TestData.TenantA ? TestData.Emergency : TestData.Office;
        profile.Company = tenant == TestData.TenantA ? "Synthetic saved A" : "Synthetic saved B";
        profile.Domain = tenant == TestData.TenantA ? "a.synthetic.invalid" : "b.synthetic.invalid";
        return profile;
    }
    private Workspace NewWorkspace()
    {
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise(); Assert.NotNull(workspace.Standard); return workspace;
    }
    private static async Task ExecuteAsync(System.Windows.Input.ICommand command)
    {
        var asyncCommand = Assert.IsType<AsyncCommand>(command); asyncCommand.Execute(null);
        var elapsed = Stopwatch.StartNew();
        while (asyncCommand.IsRunning && elapsed.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.False(asyncCommand.IsRunning, "Local evidence command did not finish within its test budget.");
    }
    private async Task SelectA(ShellViewModel? shell = null)
    {
        shell ??= _shell; var connect = shell.Page<ConnectViewModel>();
        connect.Selected = connect.Profiles.Single(p => p.Id == _a.Id);
        Assert.True(connect.ReviewOfflineCommand.CanExecute(null)); await ExecuteAsync(connect.ReviewOfflineCommand);
        Assert.Empty(shell.ErrorMessage); Assert.Equal(_a.TenantId, shell.Workspace.Profile?.TenantId);
    }
    private void OpenA()
    {
        var configuration = _shell.Page<ConfigurationViewModel>();
        configuration.SelectedStored = configuration.StoredSnapshots.Single(p => p.Id == _capture.Id);
        configuration.LoadStoredCommand.Execute(null); Assert.Empty(_shell.ErrorMessage);
    }

    [Fact]
    public async Task Cold_start_uses_the_saved_client_command_and_historical_evidence_without_sign_in()
    {
        Assert.Null(_workspace.Profile); Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
        var connect = _shell.Page<ConnectViewModel>(); connect.Selected = connect.Profiles.Single(p => p.Id == _a.Id);
        Assert.Null(_workspace.Profile); connect.EditTenantId = TestData.TenantB; connect.EditCompany = "Unsaved other target";
        await ExecuteAsync(connect.ReviewOfflineCommand);
        Assert.Empty(_shell.ErrorMessage); Assert.Equal(_a.Id, _workspace.Profile?.Id); Assert.Equal(_a.Company, _workspace.Profile?.Company);
        Assert.Equal(TestData.TenantA, connect.EditTenantId); Assert.IsType<ConfigurationViewModel>(_shell.CurrentPage);
        Assert.NotSame(connect.Selected, _workspace.Profile); Assert.Null(_workspace.Snapshot);
        Assert.False(_workspace.ExperimentalChangesEnabled); Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
        OpenA();
        Assert.Equal(_capture.Id, _workspace.Snapshot?.Id); Assert.Equal(_capture.CapturedAt, _workspace.Snapshot?.CapturedAt);
        Assert.NotNull(_workspace.Assessment); Assert.False(_workspace.SnapshotIsLive);
        Assert.Contains("offline", _shell.Page<ConfigurationViewModel>().SnapshotSummary, StringComparison.OrdinalIgnoreCase);
        _shell.Navigate("jobs"); Assert.Contains("Historical evidence", _shell.EvidenceStatus, StringComparison.Ordinal);
        Assert.Equal("No live connection", _shell.ModeBadge); Assert.True(_shell.HeaderHasDetails); Assert.False(_shell.HeaderHasConnection);
        Assert.Equal(TestData.TenantA, _shell.HeaderTenantId);
        Assert.Contains(_a.Domain, _shell.TenantSubtitle, StringComparison.Ordinal);
        Assert.Contains("saved reference", _shell.TenantSubtitle, StringComparison.Ordinal);
        Assert.Contains(_a.Id, _shell.DetailsText, StringComparison.Ordinal); Assert.Contains(TestData.TenantA, _shell.DetailsText, StringComparison.Ordinal);
        Assert.Throws<ToolkitException>(() => _workspace.AcknowledgeSnapshot());
        Assert.Throws<ToolkitException>(() => _workspace.BuildPlan(new[] { "CA-001" }));
        Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
    }

    [Fact]
    public async Task Restart_still_requires_explicit_local_activation()
    {
        await SelectA(); OpenA();
        var restarted = NewWorkspace(); var shell = new ShellViewModel(restarted);
        Assert.Null(restarted.Profile); Assert.Null(restarted.Snapshot); Assert.Null(restarted.Connection);
        await SelectA(shell); Assert.Single(shell.Page<ConfigurationViewModel>().StoredSnapshots);
        Assert.False(restarted.ExperimentalChangesEnabled); Assert.Null(restarted.Plan);
    }

    [Fact]
    public async Task Adopted_profiles_and_captures_are_opened_through_the_same_actual_command()
    {
        var archive = new WorkspaceBackup(_root.Paths).Create();
        using var receiver = new TempRoot(); receiver.WriteStandard("test.json", TestData.StandardJson); receiver.WriteManifest();
        using var logger = new ToolkitLogger(receiver.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(receiver.Paths, new ToolkitSettings(), logger, true); workspace.Initialise();
        new WorkspaceBackup(receiver.Paths).AdoptFromArchive(archive, WorkspaceBackup.ArchiveDigest(archive));
        workspace.ReloadAdoptedEvidence(); var shell = new ShellViewModel(workspace);
        Assert.Null(workspace.Profile); await SelectA(shell);
        var configuration = shell.Page<ConfigurationViewModel>(); configuration.SelectedStored = Assert.Single(configuration.StoredSnapshots);
        configuration.LoadStoredCommand.Execute(null);
        Assert.Empty(shell.ErrorMessage); Assert.Equal(_capture.Id, workspace.Snapshot?.Id); Assert.False(workspace.SnapshotIsLive);
        Assert.Null(workspace.Connection); Assert.Null(workspace.ApplicationSetup); Assert.Null(workspace.Plan);
    }

    [Fact]
    public async Task Active_connection_refuses_even_same_tenant_offline_selection_without_clearing_state()
    {
        await SelectA(); OpenA();
        var graph = new FakeGraphClient(_workspace.RequireStandard());
        var connection = new ConnectedTenant(TestData.Session(), graph, authenticator: null);
        Set(nameof(Workspace.Connection), connection);
        var before = _workspace.Profile; var capture = _workspace.Snapshot;
        var connect = _shell.Page<ConnectViewModel>(); Assert.False(connect.ReviewOfflineCommand.CanExecute(null));
        await Assert.ThrowsAsync<ToolkitException>(() => _workspace.SelectOfflineProfileAsync(_a.Id));
        await ExecuteAsync(connect.ReviewOfflineCommand);
        Assert.NotEmpty(_shell.ErrorMessage); Assert.Same(connection, _workspace.Connection);
        Assert.Same(before, _workspace.Profile); Assert.Same(capture, _workspace.Snapshot);
        Assert.Empty(graph.Reads); Assert.Empty(graph.Writes);
    }

    [Fact]
    public async Task Verified_setup_context_refuses_local_activation_without_tokens_or_requests()
    {
        var tokens = new NoTokens(); using var handler = new NoRequests(); using var http = new HttpClient(handler);
        var setup = new ApplicationSetupService(http, tokens, new SignInOutcome
        { TenantId = TestData.TenantA, AccountObjectId = TestData.Operator, Account = "synthetic@test.example" }, NullLog.Instance);
        Set(nameof(Workspace.ApplicationSetup), setup);
        var connect = _shell.Page<ConnectViewModel>(); connect.Selected = connect.Profiles.Single(p => p.Id == _a.Id);
        Assert.False(connect.ReviewOfflineCommand.CanExecute(null));
        await Assert.ThrowsAsync<ToolkitException>(() => _workspace.SelectOfflineProfileAsync(_a.Id));
        await ExecuteAsync(connect.ReviewOfflineCommand);
        Assert.Same(setup, _workspace.ApplicationSetup); Assert.Null(_workspace.Profile); Assert.Null(_workspace.Connection);
        Assert.NotEmpty(_shell.ErrorMessage); Assert.Equal(0, tokens.Calls); Assert.Equal(0, handler.Calls);
        await _workspace.DisconnectAsync();
    }

    [Fact]
    public async Task Busy_workspace_refuses_direct_offline_and_local_register_mutations()
    {
        await SelectA();
        await _workspace.RunExclusiveAsync("Synthetic operation", async _ =>
        {
            await Assert.ThrowsAsync<ToolkitException>(() => _workspace.SelectOfflineProfileAsync(_b.Id));
            Assert.Throws<ToolkitException>(() => _workspace.SaveManualCheck("CA-001", "Pass", "Synthetic valid evidence note"));
            Assert.Throws<ToolkitException>(() => _workspace.DeleteDeviation("CA-001"));
            Assert.Throws<ToolkitException>(() => _workspace.SaveDeviation(new Deviation()));
        });
        Assert.Equal(TestData.TenantA, _workspace.Profile?.TenantId); Assert.Empty(_workspace.LoadManualChecks().Checks);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Unknown_or_duplicate_persisted_profile_ID_refuses_before_state_changes(bool duplicate)
    {
        await SelectA(); OpenA(); var profile = _workspace.Profile; var capture = _workspace.Snapshot;
        if (duplicate) _workspace.Evidence.SaveProfiles(new[] { _a, _a, _b });
        await Assert.ThrowsAsync<ToolkitException>(() => _workspace.SelectOfflineProfileAsync(duplicate ? _a.Id : "missing"));
        Assert.Same(profile, _workspace.Profile); Assert.Same(capture, _workspace.Snapshot); Assert.NotNull(_workspace.Assessment);
    }

    [Fact]
    public async Task Persisted_profile_is_the_target_even_if_the_saved_list_object_was_mutated()
    {
        var connect = _shell.Page<ConnectViewModel>(); connect.Selected = connect.Profiles.Single(p => p.Id == _a.Id);
        connect.Selected.TenantId = TestData.TenantB;
        await ExecuteAsync(connect.ReviewOfflineCommand);
        Assert.Empty(_shell.ErrorMessage); Assert.Equal(TestData.TenantA, _workspace.Profile?.TenantId);
        Assert.Equal(TestData.TenantA, connect.EditTenantId);
        Assert.Equal(TestData.TenantA, connect.Selected?.TenantId);
    }

    [Fact]
    public async Task Switching_offline_clients_clears_previous_transient_evidence_but_keeps_durable_history()
    {
        await SelectA(); OpenA();
        Set(nameof(Workspace.SnapshotIsLive), true); Set(nameof(Workspace.ExchangeSnapshot), _capture);
        Set(nameof(Workspace.ExchangeCapturedByTool), true); Set(nameof(Workspace.LastRun), new DeploymentRun { Id = _a.Id, TenantId = TestData.TenantA });
        Set(nameof(Workspace.Plan), new DeploymentPlan()); Set(nameof(Workspace.AcknowledgedSnapshotId), _capture.Id);
        Set(nameof(Workspace.InterruptedNotice), "Synthetic A notice");
        var connect = _shell.Page<ConnectViewModel>(); connect.Selected = connect.Profiles.Single(p => p.Id == _b.Id);
        await ExecuteAsync(connect.ReviewOfflineCommand);
        Assert.Empty(_shell.ErrorMessage); Assert.Equal(TestData.TenantB, _workspace.Profile?.TenantId);
        Assert.Null(_workspace.Snapshot); Assert.Null(_workspace.ExchangeSnapshot); Assert.False(_workspace.ExchangeCapturedByTool);
        Assert.Null(_workspace.Assessment); Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
        Assert.Null(_workspace.Access); Assert.Null(_workspace.Licences); Assert.Null(_workspace.LastRun); Assert.Null(_workspace.Control);
        Assert.Empty(_workspace.InterruptedNotice); Assert.False(_workspace.SnapshotIsLive); Assert.False(_workspace.ExperimentalChangesEnabled);
        Assert.Single(_workspace.Evidence.ListSnapshots(TestData.TenantA));
        Assert.All(_shell.Page<ConfigurationViewModel>().StoredSnapshots, s => Assert.Equal(TestData.TenantB, _workspace.Evidence.LoadSnapshot(TestData.TenantB, s.Id)?.TenantId));
        Assert.Contains("No full capture open", _shell.EvidenceStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Historical_evidence_does_not_hide_an_active_token_with_write_scopes()
    {
        await SelectA(); OpenA(); Set(nameof(Workspace.Connection), new ConnectedTenant(TestData.Session(mode: SessionMode.Assessment), new FakeGraphClient(_workspace.RequireStandard()), null));
        Assert.True(_shell.IsDeploymentSession); Assert.Contains("broader permissions present", _shell.ModeBadge, StringComparison.Ordinal);
        Assert.Contains("Historical evidence", _shell.EvidenceStatus, StringComparison.Ordinal);
        var assessment = _shell.Page<AssessmentViewModel>(); assessment.Search = "synthetic unmatched search";
        Assert.Empty(assessment.Findings); Assert.Contains("No findings match", assessment.ViewStateText, StringComparison.Ordinal);
        Assert.Contains("Historical evidence", assessment.ViewStateText, StringComparison.Ordinal);
        Assert.Throws<ToolkitException>(() => _workspace.AcknowledgeSnapshot());
    }

    private void Set(string name, object? value) => typeof(Workspace).GetProperty(name)!.SetValue(_workspace, value);

    [Fact]
    public async Task Saving_a_new_tenant_ID_for_the_active_saved_client_clears_old_tenant_evidence()
    {
        await SelectA(); OpenA();
        Set(nameof(Workspace.ExchangeSnapshot), _capture); Set(nameof(Workspace.ExchangeCapturedByTool), true);
        Set(nameof(Workspace.LastRun), new DeploymentRun { Id = _a.Id, TenantId = TestData.TenantA });
        Set(nameof(Workspace.SnapshotIsLive), true); Set(nameof(Workspace.Plan), new DeploymentPlan());
        Set(nameof(Workspace.AcknowledgedSnapshotId), _capture.Id); Set(nameof(Workspace.InterruptedNotice), "Synthetic old tenant notice");
        var connect = _shell.Page<ConnectViewModel>();
        // B is already saved; use a new valid C so duplicate-profile refusal cannot conceal this transition defect.
        const string tenantC = "99999999-9999-4999-8999-999999999999";
        connect.EditTenantId = tenantC; connect.SaveProfileCommand.Execute(null);
        Assert.Empty(_shell.ErrorMessage); Assert.Equal(tenantC, _workspace.Profile?.TenantId);
        Assert.Equal(tenantC, _shell.HeaderTenantId); Assert.Contains("No full capture open", _shell.EvidenceStatus, StringComparison.Ordinal);
        Assert.Null(_workspace.Snapshot); Assert.Null(_workspace.ExchangeSnapshot); Assert.False(_workspace.ExchangeCapturedByTool);
        Assert.False(_workspace.SnapshotIsLive); Assert.Null(_workspace.Assessment); Assert.Null(_workspace.Plan); Assert.Null(_workspace.AcknowledgedSnapshotId);
        Assert.Null(_workspace.Access); Assert.Null(_workspace.Licences); Assert.Null(_workspace.LastRun); Assert.Null(_workspace.Control);
        Assert.Empty(_workspace.InterruptedNotice); Assert.False(_workspace.ExperimentalChangesEnabled);
        Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
        Assert.DoesNotContain(_capture.Id, _shell.DetailsText, StringComparison.Ordinal);
        Assert.Single(_workspace.Evidence.ListSnapshots(TestData.TenantA));
    }

    [Fact]
    public async Task Header_refuses_to_attribute_a_mismatched_capture_to_the_selected_client()
    {
        var connect = _shell.Page<ConnectViewModel>(); connect.Selected = connect.Profiles.Single(p => p.Id == _b.Id);
        await ExecuteAsync(connect.ReviewOfflineCommand); Set(nameof(Workspace.Snapshot), _capture);
        Assert.Equal(TestData.TenantB, _shell.HeaderTenantId);
        Assert.Contains("another tenant", _shell.EvidenceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(_capture.CapturedAt, _shell.EvidenceStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(_capture.Id, _shell.DetailsText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exchange_only_historical_assessment_keeps_the_original_capture_time()
    {
        await SelectA();
        var exchange = BuildStandard.Engine.Exchange.ExchangeEvidenceImporter.Snapshot(ExchangeTestData.Capture(), _workspace.Profile!, _workspace.RequireStandard());
        _workspace.Evidence.SaveSnapshot(exchange); _workspace.LoadStoredSnapshot(exchange.Id);
        Assert.Null(_workspace.Snapshot); Assert.NotNull(_workspace.ExchangeSnapshot); Assert.NotNull(_workspace.Assessment);
        Assert.Contains(exchange.CapturedAt, _shell.Page<AssessmentViewModel>().ViewStateText, StringComparison.Ordinal);
        Assert.Contains("Historical Exchange evidence", _shell.EvidenceStatus, StringComparison.Ordinal);
        Assert.False(_workspace.SnapshotIsLive); Assert.Null(_workspace.Plan);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Local_activation_waits_for_pending_discovery_release_and_respects_cancellation(bool cancel)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(Workspace).GetField("_pendingRelease", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(_workspace, release.Task);
        var activation = _workspace.SelectOfflineProfileAsync(_a.Id);
        Assert.True(_workspace.Busy); Assert.False(activation.IsCompleted); Assert.Null(_workspace.Profile);
        if (cancel) _workspace.CancelOperation();
        release.SetResult(); await activation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(_workspace.Busy); Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
        Assert.False(_workspace.ExperimentalChangesEnabled);
        if (cancel) Assert.Null(_workspace.Profile); else Assert.Equal(TestData.TenantA, _workspace.Profile?.TenantId);
    }
    private sealed class NoTokens : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) { Calls++; throw new InvalidOperationException("Offline action requested a token."); }
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class NoRequests : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Offline action sent an HTTP request."); }
    }
    public void Dispose() { _logger.Dispose(); _root.Dispose(); }
}
