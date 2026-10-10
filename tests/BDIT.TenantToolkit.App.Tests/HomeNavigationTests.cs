using System.Net.Http;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class HomeNavigationTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;

    public HomeNavigationTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, true); _workspace.Initialise();
        _shell = new ShellViewModel(_workspace);
    }

    // Private state seeding represents existing in-memory work, not an authenticated connection or authorised plan.
    private void Set(string name, object? value) => typeof(Workspace).GetProperty(name)!.SetValue(_workspace, value);

    [Theory]
    [InlineData("cold")] [InlineData("historical")] [InlineData("connected")]
    public void Every_home_route_only_navigates_and_retains_identity_evidence_and_plan(string state)
    {
        var tokens = new NoTokens(); using var handler = new NoRequests(); using var http = new HttpClient(handler);
        if (state != "cold")
        {
            _workspace.ApplyProfileToSession(TestData.Profile(), save: false);
            var capture = TestData.Snapshot(_workspace.RequireStandard()); _workspace.Evidence.SaveSnapshot(capture);
            _workspace.LoadStoredSnapshot(capture.Id);
            Set(nameof(Workspace.Plan), new DeploymentPlan { TenantId = TestData.TenantA, PlanDigest = new string('a', 64) });
            Set(nameof(Workspace.AcknowledgedSnapshotId), capture.Id);
        }
        if (state == "connected")
        {
            var session = TestData.Session(mode: SessionMode.Assessment); session.Scopes = new() { "Policy.Read.All" };
            session.TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
            var graph = new GraphClient(http, tokens, session.TenantId, session.Mode,
                GraphRouteAllowList.FromStandard(_workspace.RequireStandard()), new GraphClientOptions { Sleep = false }, NullLog.Instance);
            Set(nameof(Workspace.Connection), new ConnectedTenant(session, graph, null));
            Set(nameof(Workspace.SnapshotIsLive), true);
        }
        var home = new HomeViewModel(_shell);
        var profile = _workspace.Profile; var connection = _workspace.Connection; var captureBefore = _workspace.Snapshot;
        var assessment = _workspace.Assessment; var plan = _workspace.Plan; var ack = _workspace.AcknowledgedSnapshotId;
        var profileJson = ToolkitJson.Serialize(profile); var evidenceJson = ToolkitJson.Serialize(captureBefore);
        var planJson = ToolkitJson.Serialize(plan);
        var guard = typeof(ExperimentalOperationGuard).GetField("_binding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        guard.SetValue(_workspace.ExperimentalGuard, "synthetic closed authority marker");
        foreach (var task in home.Tasks.Concat(home.Shortcuts))
        {
            home.Refresh(); Assert.True(home.OpenTaskCommand.CanExecute(task)); home.OpenTaskCommand.Execute(task);
            Assert.Equal(task.PageKey, Assert.Single(_shell.NavItems, n => n.IsActive).Key);
            Assert.Same(profile, _workspace.Profile); Assert.Same(connection, _workspace.Connection);
            Assert.Same(captureBefore, _workspace.Snapshot); Assert.Same(assessment, _workspace.Assessment); Assert.Same(plan, _workspace.Plan);
            Assert.Equal(ack, _workspace.AcknowledgedSnapshotId); Assert.Null(_workspace.ApplicationSetup);
            Assert.Equal(profileJson, ToolkitJson.Serialize(_workspace.Profile)); Assert.Equal(evidenceJson, ToolkitJson.Serialize(_workspace.Snapshot));
            Assert.Equal(planJson, ToolkitJson.Serialize(_workspace.Plan)); Assert.False(_workspace.Busy);
        }
        home.Refresh(); home.NextStepCommand.Execute(null);
        Assert.Equal(home.Projection.NextPageKey, Assert.Single(_shell.NavItems, n => n.IsActive).Key);
        Assert.Same(profile, _workspace.Profile); Assert.Same(captureBefore, _workspace.Snapshot); Assert.Same(plan, _workspace.Plan);
        Assert.Equal(ack, _workspace.AcknowledgedSnapshotId); Assert.Equal(0, tokens.Calls); Assert.Equal(0, handler.Calls);
        Assert.False(_shell.Page<DeployViewModel>().DeployCommand.CanExecute(null));
    }

    [Fact]
    public void Home_refresh_does_not_evaluate_or_invalidate_experimental_authority()
    {
        var guard = typeof(ExperimentalOperationGuard).GetField("_binding", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        guard.SetValue(_workspace.ExperimentalGuard, "synthetic expired authority marker");
        var home = new HomeViewModel(_shell); home.Refresh(); home.Refresh();
        Assert.Equal("synthetic expired authority marker", guard.GetValue(_workspace.ExperimentalGuard));
    }

    [Fact]
    public void Unknown_task_parameters_cannot_open_a_different_workflow()
    {
        var home = new HomeViewModel(_shell); var page = _shell.CurrentPage;
        var foreign = new HomeTaskCard("Synthetic action", "", "", "deploy", "Synthetic action");
        Assert.False(home.OpenTaskCommand.CanExecute(foreign)); home.OpenTaskCommand.Execute(foreign);
        Assert.False(home.OpenTaskCommand.CanExecute("deploy")); home.OpenTaskCommand.Execute("deploy");
        Assert.Same(page, _shell.CurrentPage); Assert.Null(_workspace.Profile); Assert.Null(_workspace.Connection);
    }

    [Fact]
    public async Task Busy_status_tracks_actual_operation_and_cancellation_without_starting_a_home_action()
    {
        var home = new HomeViewModel(_shell);
        await _workspace.RunExclusiveAsync("Synthetic read awaiting cancellation", token =>
        {
            Assert.Contains("operation is in progress", home.Projection.Guidance);
            Assert.Contains("Synthetic read awaiting cancellation", home.Projection.Guidance);
            home.NextStepCommand.Execute(null); Assert.IsType<OverviewViewModel>(_shell.CurrentPage);
            Assert.True(_workspace.Busy); _workspace.CancelOperation();
            Assert.True(_workspace.OperationToken.IsCancellationRequested);
            return Task.CompletedTask;
        });
        Assert.False(_workspace.Busy); Assert.DoesNotContain("operation is in progress", home.Projection.Guidance);
        Assert.Null(_workspace.Connection); Assert.Null(_workspace.Snapshot);
    }

    [Fact]
    public void Selected_client_notification_reprojects_home_without_leaving_old_identity()
    {
        var home = new HomeViewModel(_shell); _workspace.ApplyProfileToSession(TestData.Profile(), save: false);
        Assert.Contains("Test client", home.Projection.Headline);
        var next = TestData.Profile(TestData.TenantB); next.Company = "Synthetic next engineer client";
        _workspace.ApplyProfileToSession(next, save: false);
        Assert.Contains(next.Company, home.Projection.Headline); Assert.DoesNotContain("Test client", home.Projection.Headline);
        Assert.Contains("No full capture", home.Projection.EvidenceText);
    }

    public void Dispose() { _logger.Dispose(); _root.Dispose(); }

    private sealed class NoTokens : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct) { Calls++; throw new InvalidOperationException("Home navigation requested a token."); }
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }
    private sealed class NoRequests : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Home navigation sent an HTTP request."); }
    }
}
