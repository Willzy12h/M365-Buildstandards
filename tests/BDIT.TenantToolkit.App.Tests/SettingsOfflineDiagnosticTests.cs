using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>Actual Settings commands/workspace lifecycle. Authored for Windows; portable coordinator tests are separate.</summary>
public sealed class SettingsOfflineDiagnosticTests : IDisposable
{
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;

    public SettingsOfflineDiagnosticTests()
    {
        _root.WriteStandard("test.1.json", TestData.StandardJson); _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: false);
        workspace.Initialise();
        _shell = new ShellViewModel(workspace);
    }

    private static OfflinePrerequisiteResult Ready() => new("synthetic-toolkit", "2026-10-10T00:00:00Z", "2026-10-10T00:00:01Z", OfflinePrerequisiteStatus.Ready,
        [new("package.integrity", OfflineFindingApplicability.Required, OfflineFindingDisposition.Pass, "Synthetic local observation.", "Use verified package files.")]);
    private static async Task Finish(AsyncCommand command)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (command.IsRunning) await Task.Delay(10, deadline.Token);
    }

    [Fact]
    public async Task Actual_Settings_command_runs_exclusively_without_a_profile_session_or_tenant_state_change()
    {
        var called = 0;
        var workspace = _shell.Workspace;
        var standard = workspace.Standard;
        var diagnostic = new OfflinePrerequisiteDiagnostic((root, token) =>
        {
            Assert.Equal(_root.Root, root);
            Assert.True(workspace.Busy);
            Assert.True(token.CanBeCanceled);
            Interlocked.Increment(ref called);
            return Ready();
        });
        var page = new SettingsViewModel(_shell, diagnostic);
        Assert.True(page.RunOfflineDiagnosticCommand.CanExecute(null));
        Assert.False(page.CancelOfflineDiagnosticCommand.CanExecute(null));
        Assert.False(page.CopyOfflineDiagnosticCommand.CanExecute(null));
        Assert.Contains("Run offline checks", page.OfflineDiagnosticAvailability);
        var command = Assert.IsType<AsyncCommand>(page.RunOfflineDiagnosticCommand);
        command.Execute(null); await Finish(command);
        Assert.Equal(1, called);
        Assert.True(workspace.Idle);
        Assert.True(page.CopyOfflineDiagnosticCommand.CanExecute(null));
        Assert.False(page.CancelOfflineDiagnosticCommand.CanExecute(null));
        Assert.Same(standard, workspace.Standard);
        Assert.Null(workspace.Profile); Assert.Null(workspace.Connection); Assert.Null(workspace.ApplicationSetup);
        Assert.Null(workspace.Snapshot); Assert.Null(workspace.Plan);
        Assert.Empty(_shell.ErrorMessage);
    }

    [Fact]
    public async Task Idle_cancel_and_disabled_run_cannot_affect_another_workspace_task()
    {
        var calls = 0;
        var page = new SettingsViewModel(_shell, new OfflinePrerequisiteDiagnostic((_, _) => { calls++; return Ready(); }));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherToken = CancellationToken.None;
        var changed = new List<string?>(); page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var other = _shell.Workspace.RunExclusiveAsync("Synthetic other task", async _ =>
        {
            otherToken = _shell.Workspace.OperationToken; started.TrySetResult(); await release.Task;
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(page.RunOfflineDiagnosticCommand.CanExecute(null));
            Assert.False(page.CancelOfflineDiagnosticCommand.CanExecute(null));
            Assert.Contains("Wait for the current workspace task", page.OfflineDiagnosticAvailability);
            page.CancelOfflineDiagnosticCommand.Execute(null);
            var command = Assert.IsType<AsyncCommand>(page.RunOfflineDiagnosticCommand);
            command.Execute(null); await Finish(command);
            Assert.False(otherToken.IsCancellationRequested);
            Assert.Equal(0, calls);
            Assert.Contains(nameof(SettingsViewModel.OfflineDiagnosticAvailability), changed);
        }
        finally { release.TrySetResult(); await other.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Own_cancel_or_shutdown_cancels_only_the_check_and_waits_for_its_completion(bool shutdown)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var observed = CancellationToken.None;
        var page = new SettingsViewModel(_shell, new OfflinePrerequisiteDiagnostic((_, token) =>
        {
            observed = token; started.TrySetResult(); release.Wait(); return Ready();
        }));
        var command = Assert.IsType<AsyncCommand>(page.RunOfflineDiagnosticCommand);
        command.Execute(null);
        Task? closing = null;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(_shell.Workspace.Busy);
            Assert.False(page.RunOfflineDiagnosticCommand.CanExecute(null));
            Assert.True(page.CancelOfflineDiagnosticCommand.CanExecute(null));
            Assert.False(page.CopyOfflineDiagnosticCommand.CanExecute(null));
            if (shutdown) closing = _shell.Workspace.ShutdownAsync(); else page.CancelOfflineDiagnosticCommand.Execute(null);
            Assert.True(observed.IsCancellationRequested);
            Assert.False(page.CancelOfflineDiagnosticCommand.CanExecute(null));
            Assert.Contains("Cancellation requested", page.OfflineDiagnostic.Message);
            if (closing is not null) Assert.False(closing.IsCompleted);
        }
        finally
        {
            release.Set(); await Finish(command);
            if (closing is not null) await closing.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(OfflinePrerequisiteStatus.Cancelled, page.OfflineDiagnostic.Result!.Status);
        Assert.True(_shell.Workspace.Idle);
        Assert.Empty(_shell.ErrorMessage);
        if (shutdown) Assert.True(_shell.Workspace.ShutdownComplete);
    }

    [Fact]
    public async Task A_private_engine_failure_is_not_sent_to_shell_error_or_workspace_log()
    {
        const string privateError = "synthetic-private-machine private-user profile@example.invalid token-private";
        const string publicControl = "Public diagnostic-test control.";
        _logger.Log(LogLevel.Information, "Tests", publicControl);
        var page = new SettingsViewModel(_shell, new OfflinePrerequisiteDiagnostic((_, _) => throw new InvalidOperationException(privateError)));
        var command = Assert.IsType<AsyncCommand>(page.RunOfflineDiagnosticCommand);
        command.Execute(null); await Finish(command);
        Assert.Equal(OfflinePrerequisiteDiagnostic.UnexpectedFailure, page.OfflineDiagnostic.Message);
        Assert.Null(page.OfflineDiagnostic.Result);
        Assert.Empty(page.OfflineDiagnostic.Output);
        Assert.Empty(_shell.ErrorMessage);
        _logger.Flush();
        var logFile = Assert.IsType<string>(_logger.FilePath);
        // The synchronous logger keeps its write handle open. A Windows reader must permit that access too.
        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var log = reader.ReadToEnd();
        Assert.Contains(publicControl, log);
        Assert.DoesNotContain(privateError, log);
        Assert.DoesNotContain("synthetic-private-machine", log);
        Assert.DoesNotContain("private-user", log);
        Assert.DoesNotContain("token-private", log);
    }

    public void Dispose() { _logger.Dispose(); _root.Dispose(); GC.SuppressFinalize(this); }
}
