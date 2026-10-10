using System.Reflection;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Models;
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
}
