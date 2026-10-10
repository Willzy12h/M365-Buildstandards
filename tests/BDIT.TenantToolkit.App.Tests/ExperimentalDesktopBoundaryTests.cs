using System.Reflection;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.App.Views;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Graph;
using BDIT.TenantToolkit.Graph.Auth;
using BDIT.TenantToolkit.Graph.Setup;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ExperimentalDesktopBoundaryTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _log;
    private readonly Workspace _workspace;
    private readonly ShellViewModel _shell;
    public ExperimentalDesktopBoundaryTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson); _root.WriteManifest();
        _log = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        _workspace = new Workspace(_root.Paths, new ToolkitSettings(), _log, true);
        _workspace.Initialise(); _shell = new ShellViewModel(_workspace);
    }
    public void Dispose() { _log.Dispose(); _root.Dispose(); }

    [Fact]
    public async Task Direct_workspace_deploy_and_recovery_refuse_before_engine_or_connection_use()
    {
        var deploy = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.DeployAsync(TestData.TenantA));
        Assert.Equal(ExperimentalOperationGuard.ClosedReason, deploy.Message);
        var recovery = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.ExecuteRecoveryAsync(new RecoveryPlan(), TestData.TenantA, true, true));
        Assert.Equal(ExperimentalOperationGuard.ClosedReason, recovery.Message);
        Assert.Null(_workspace.LastRun); Assert.False(_workspace.Busy);
    }

    [Theory]
    [InlineData("Execute")] [InlineData("ExecuteLaps")] [InlineData("PublishPackage")]
    public async Task Direct_policy_routes_cannot_bypass_the_disabled_commands(string method)
    {
        var vm = _shell.Page<AutomationViewModel>();
        var entry = typeof(AutomationViewModel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = await Assert.ThrowsAsync<SafetyViolationException>(() => (Task)entry.Invoke(vm, null)!);
        Assert.Equal(ExperimentalOperationGuard.ClosedReason, error.Message);
        Assert.False(vm.ExecuteCommand.CanExecute(null)); Assert.False(vm.ExecuteLapsCommand.CanExecute(null)); Assert.False(vm.PublishPackageCommand.CanExecute(null));
        Assert.Null(_workspace.LastRun);
    }

    [Fact]
    public async Task An_access_request_without_visible_desktop_approval_refuses_locally()
    {
        var error = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.ReviewPermissionRequestAsync(TestData.TenantA,
            "cccccccc-cccc-cccc-cccc-cccccccccccc", new[] { "User.Read" }, "Synthetic request", CancellationToken.None));
        Assert.Contains("No request was sent", error.Message);
        Assert.Null(_workspace.Connection); Assert.Null(_workspace.ApplicationSetup);
    }

    private FakeGraphClient SetVerifiedDeployment(bool optIn)
    {
        _workspace.Settings.DeploymentClientId = TestData.ClientId;
        var profile = TestData.Profile(); var session = TestData.Session();
        session.TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
        var graph = new FakeGraphClient(_workspace.RequireStandard());
        typeof(Workspace).GetProperty(nameof(Workspace.Profile))!.SetValue(_workspace, profile);
        typeof(Workspace).GetProperty(nameof(Workspace.Connection))!.SetValue(_workspace, new ConnectedTenant(session, graph, null));
        var snapshot = TestData.Snapshot(_workspace.RequireStandard(), capturedAt: DateTimeOffset.UtcNow);
        _workspace.Evidence.SaveSnapshot(snapshot);
        typeof(Workspace).GetProperty(nameof(Workspace.Snapshot))!.SetValue(_workspace, snapshot);
        typeof(Workspace).GetProperty(nameof(Workspace.SnapshotIsLive))!.SetValue(_workspace, true);
        var plan = _workspace.BuildPlan(new[] { "CA-001" });
        Assert.True(Assert.Single(plan.Rows).IsWrite);
        _workspace.AcknowledgeSnapshot(); _workspace.ValidatePlanForExecution();
        Assert.True(ConfirmTenantDialog.CanApprove(profile, plan, session, true));
        if (optIn) { _workspace.SetExperimentalChanges(true); Assert.True(_workspace.ExperimentalChangesEnabled); }
        return graph;
    }

    [Fact]
    public async Task Write_capable_connection_and_reviewable_plan_do_not_bypass_the_off_gate()
    {
        var graph = SetVerifiedDeployment(optIn: false); var plan = _workspace.Plan;
        var error = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.DeployAsync(TestData.TenantA));
        Assert.Equal(ExperimentalOperationGuard.ClosedReason, error.Message);
        var recovery = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.ExecuteRecoveryAsync(new RecoveryPlan(), TestData.TenantA, true, true));
        Assert.Equal(ExperimentalOperationGuard.ClosedReason, recovery.Message);
        Assert.Same(plan, _workspace.Plan); Assert.Null(_workspace.LastRun);
        Assert.Empty(graph.Reads); Assert.Empty(graph.Writes); Assert.False(_workspace.Busy);
    }

    [Fact]
    public async Task Setup_request_invalidates_deployment_opt_in_and_refuses_before_acquisition_without_a_visible_preview()
    {
        var graph = SetVerifiedDeployment(optIn: true);
        var error = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.ConnectApplicationSetupAsync(TestData.TenantA));
        Assert.Contains("No request was sent", error.Message);
        Assert.False(_workspace.ExperimentalChangesEnabled); Assert.Null(_workspace.ApplicationSetup);
        Assert.Empty(graph.Reads); Assert.Empty(graph.Writes); Assert.False(_workspace.Busy);
    }

    [Fact]
    public async Task Deliberate_deployment_connect_refuses_before_acquisition_without_a_visible_preview()
    {
        var graph = SetVerifiedDeployment(optIn: true); var connection = _workspace.Connection;
        var error = await Assert.ThrowsAsync<SafetyViolationException>(() => _workspace.ConnectAsync(_workspace.Profile!, SessionMode.Deployment,
            loginHint: _workspace.Session!.Account));
        Assert.Contains("No request was sent", error.Message);
        Assert.False(_workspace.ExperimentalChangesEnabled); Assert.Same(connection, _workspace.Connection);
        Assert.Empty(graph.Reads); Assert.Empty(graph.Writes); Assert.False(_workspace.Busy);
    }

    private sealed class NoTokens : IAccessTokenProvider
    {
        public int Calls { get; private set; }
        public Task<string> GetAccessTokenAsync(CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Synthetic refusal must precede authentication or reads."); }
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct) => GetAccessTokenAsync(ct);
    }

    [Theory]
    [InlineData(SessionMode.Assessment)] [InlineData(SessionMode.Deployment)]
    public async Task Actual_consent_route_invalidates_opt_in_and_refuses_before_redirect_or_grant_reads_without_preview(SessionMode mode)
    {
        var graph = SetVerifiedDeployment(optIn: true); var tokens = new NoTokens();
        using var http = new System.Net.Http.HttpClient();
        var setup = new ApplicationSetupService(http, tokens, new SignInOutcome
        { TenantId = TestData.TenantA, AccountObjectId = TestData.Operator, Account = "engineer@test.example" }, NullLog.Instance);
        typeof(Workspace).GetProperty(nameof(Workspace.ApplicationSetup))!.SetValue(_workspace, setup);
        var vm = _shell.Page<ApplicationSetupViewModel>();
        vm.TenantId = TestData.TenantA; vm.AssessmentClientId = TestData.ClientId; vm.DeploymentClientId = TestData.ClientId;
        var entry = typeof(ApplicationSetupViewModel).GetMethod("OpenConsentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var error = await Assert.ThrowsAsync<SafetyViolationException>(() => (Task)entry.Invoke(vm, new object[] { mode })!);
        Assert.Contains("No request was sent", error.Message); Assert.False(_workspace.ExperimentalChangesEnabled);
        Assert.Equal(0, tokens.Calls); Assert.Empty(graph.Reads); Assert.Empty(graph.Writes); Assert.False(_workspace.Busy);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("engineer@test.example")]
    public void Permission_preview_uses_only_the_account_hint_of_the_actual_request(string? hint)
    {
        SetVerifiedDeployment(optIn: false);
        var entry = typeof(Workspace).GetMethod("PermissionRequestDetails", BindingFlags.Static | BindingFlags.NonPublic)!;
        var text = (string)entry.Invoke(null, new object?[] { TestData.TenantA, TestData.ClientId,
            new[] { "User.Read" }, "Synthetic account chooser", hint })!;
        Assert.Contains(TestData.ClientId, text); Assert.Contains("User.Read", text);
        if (string.IsNullOrWhiteSpace(hint))
        { Assert.Contains("Not known; Microsoft will ask you to choose", text); Assert.DoesNotContain(_workspace.Session!.Account, text); }
        else Assert.Contains("Account context: " + hint, text);
    }
}
