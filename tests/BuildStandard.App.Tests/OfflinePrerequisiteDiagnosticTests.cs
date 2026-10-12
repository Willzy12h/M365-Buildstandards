using BuildStandard.App.Services;
using BuildStandard.Cli;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Json;
using BuildStandard.Engine.Diagnostics;
using Xunit;

namespace BuildStandard.Tests;

/// <summary>Also source-linked into the portable tests: coordinator/engine behaviour, not native WPF or clipboard proof.</summary>
public sealed class OfflinePrerequisiteDiagnosticTests
{
    private const string Private = "synthetic-private-user synthetic-private-machine profile@example.invalid token-private";
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(10);
    private static OfflinePrerequisiteResult Result(OfflinePrerequisiteStatus status) => new("synthetic-toolkit", "2026-10-10T00:00:00Z", "2026-10-10T00:00:01Z", status,
    [
        new("package.integrity", OfflineFindingApplicability.Required,
            status == OfflinePrerequisiteStatus.NeedsAttention ? OfflineFindingDisposition.Fail : OfflineFindingDisposition.Pass,
            "Synthetic package observation.", "Restore verified package files."),
        new("standard", OfflineFindingApplicability.Required,
            status == OfflinePrerequisiteStatus.Ready ? OfflineFindingDisposition.Pass : OfflineFindingDisposition.Unknown,
            "Synthetic catalogue observation.", "Review the selected catalogue."),
        new("powershell.7", OfflineFindingApplicability.Optional, OfflineFindingDisposition.Unknown,
            "Presence has not been established; execution was not tested.", "Ask the security owner to review optional host policy.")
    ]);

    [Fact]
    public void No_result_or_cancel_action_is_inert_until_a_check_is_started()
    {
        var calls = 0;
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, _) => { calls++; return Result(OfflinePrerequisiteStatus.Ready); });
        diagnostic.Cancel();
        Assert.False(diagnostic.IsRunning);
        Assert.False(diagnostic.CancellationRequested);
        Assert.False(diagnostic.HasResult);
        Assert.Empty(diagnostic.Output);
        Assert.Contains("No offline check has run", diagnostic.Message);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(OfflinePrerequisiteStatus.Ready)]
    [InlineData(OfflinePrerequisiteStatus.NeedsAttention)]
    [InlineData(OfflinePrerequisiteStatus.Cancelled)]
    public async Task Completed_status_findings_and_next_steps_match_the_actual_CLI_text(OfflinePrerequisiteStatus status)
    {
        var expected = Result(status);
        var diagnostic = new OfflinePrerequisiteDiagnostic((root, _) => { Assert.Equal("synthetic-root", root); return expected; });
        var changes = new List<string?>();
        diagnostic.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        await diagnostic.RunAsync("synthetic-root");
        using var text = new StringWriter();
        DoctorCommand.WriteText(expected, text);
        Assert.Same(expected, diagnostic.Result);
        Assert.Equal(text.ToString(), diagnostic.Output);
        Assert.Contains("[Unknown] powershell.7 (Optional)", diagnostic.Output);
        Assert.Contains("Next: Ask the security owner", diagnostic.Output);
        Assert.Contains("tenant readiness, write approval and script execution are not certified", diagnostic.Output);
        Assert.True(diagnostic.HasResult);
        Assert.False(diagnostic.IsRunning);
        Assert.Contains(nameof(OfflinePrerequisiteDiagnostic.IsRunning), changes);
        Assert.Contains(nameof(OfflinePrerequisiteDiagnostic.HasResult), changes);
        Assert.Contains(nameof(OfflinePrerequisiteDiagnostic.Message), changes);
    }

    [Fact]
    public async Task Actual_shared_engine_source_fixture_stays_Unknown_and_matches_the_desktop_result()
    {
        using var root = new TempRoot();
        root.WriteStandard("test.1.json", TestData.StandardJson); root.WriteManifest();
        File.WriteAllText(root.Paths.SettingsFile, ToolkitJson.Serialize(new ToolkitSettings { DefaultStandardRelease = "test.1" }));
        var engine = new OfflinePrerequisiteService(clock: new FixedClock());
        var expected = engine.Check(root.Root);
        var diagnostic = new OfflinePrerequisiteDiagnostic(engine.Check);
        await diagnostic.RunAsync(root.Root);
        Assert.Equal(ToolkitJson.Serialize(expected), ToolkitJson.Serialize(diagnostic.Result));
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, diagnostic.Result!.Status);
        Assert.Contains(diagnostic.Result.Findings, f => f.Id == "package.metadata" && f.Disposition == OfflineFindingDisposition.Unknown);
        Assert.Contains("[Unknown] package.metadata (Required)", diagnostic.Output);
        Assert.DoesNotContain(root.Root, diagnostic.Output);
        Assert.Empty(Directory.EnumerateFiles(root.Root, ".bdit-doctor-*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Rerun_clears_stale_Ready_output_before_work_and_refuses_reentry()
    {
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return Result(OfflinePrerequisiteStatus.Ready);
            started.TrySetResult(); release.Wait();
            return Result(OfflinePrerequisiteStatus.NeedsAttention);
        });
        await diagnostic.RunAsync("synthetic-root");
        Assert.Contains("Offline prerequisites: Ready", diagnostic.Output);
        var running = diagnostic.RunAsync("synthetic-root");
        try
        {
            await started.Task.WaitAsync(TestDeadline);
            Assert.True(diagnostic.IsRunning);
            Assert.Null(diagnostic.Result);
            Assert.Empty(diagnostic.Output);
            Assert.False(diagnostic.HasResult);
            await diagnostic.RunAsync("must-not-be-inspected");
            Assert.Equal(2, calls);
        }
        finally { release.Set(); await running.WaitAsync(TestDeadline); }
        Assert.Equal(OfflinePrerequisiteStatus.NeedsAttention, diagnostic.Result!.Status);
        Assert.DoesNotContain("Offline prerequisites: Ready", diagnostic.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Own_cancel_and_caller_shutdown_token_reach_the_check_and_reject_a_late_Ready_reply(bool caller)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        using var shutdown = new CancellationTokenSource();
        var observed = CancellationToken.None;
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, token) =>
        {
            observed = token; started.TrySetResult(); release.Wait();
            // A completion already prepared by a worker is still refused after cancellation, before publication.
            return Result(OfflinePrerequisiteStatus.Ready);
        });
        var running = diagnostic.RunAsync("synthetic-root", shutdown.Token);
        try
        {
            await started.Task.WaitAsync(TestDeadline);
            if (caller) shutdown.Cancel(); else diagnostic.Cancel();
            Assert.True(observed.IsCancellationRequested);
            Assert.True(diagnostic.CancellationRequested);
            Assert.Null(diagnostic.Result);
        }
        finally { release.Set(); await running.WaitAsync(TestDeadline); }
        Assert.Equal(OfflinePrerequisiteStatus.Cancelled, diagnostic.Result!.Status);
        Assert.Contains("Offline prerequisites: Cancelled", diagnostic.Output);
        Assert.DoesNotContain("Offline prerequisites: Ready", diagnostic.Output);
        Assert.False(diagnostic.IsRunning);
    }

    [Fact]
    public async Task A_completed_operations_caller_cannot_cancel_or_fail_the_next_run()
    {
        using var previousCaller = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var observed = CancellationToken.None;
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 2) { observed = token; started.TrySetResult(); release.Wait(); }
            return Result(OfflinePrerequisiteStatus.Ready);
        });
        await diagnostic.RunAsync("first", previousCaller.Token);
        diagnostic.Cancel(); // Completed operations have no owned cancellation scope.
        var running = diagnostic.RunAsync("second");
        try
        {
            await started.Task.WaitAsync(TestDeadline);
            previousCaller.Cancel();
            Assert.False(observed.IsCancellationRequested);
            Assert.False(diagnostic.CancellationRequested);
        }
        finally { release.Set(); await running.WaitAsync(TestDeadline); }
        Assert.Equal(OfflinePrerequisiteStatus.Ready, diagnostic.Result!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Input_and_private_unexpected_failures_replace_previous_Ready_without_raw_text(bool input)
    {
        var calls = 0;
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1) return Result(OfflinePrerequisiteStatus.Ready);
            if (input) throw new OfflineDiagnosticInputException();
            throw new InvalidOperationException(Private);
        });
        await diagnostic.RunAsync("first");
        await diagnostic.RunAsync(Private);
        Assert.Null(diagnostic.Result);
        Assert.Empty(diagnostic.Output);
        Assert.False(diagnostic.HasResult);
        Assert.False(diagnostic.IsRunning);
        Assert.DoesNotContain(Private, diagnostic.Message);
        Assert.Contains(input ? "refused" : "failed unexpectedly", diagnostic.Message);
        if (!input) Assert.Equal(OfflinePrerequisiteDiagnostic.UnexpectedFailure, diagnostic.Message);
    }

    [Fact]
    public async Task A_throwing_cancellation_callback_is_sanitised_and_cannot_publish_success()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, token) =>
        {
            using var callback = token.Register(() => throw new InvalidOperationException(Private));
            started.TrySetResult(); release.Wait();
            return Result(OfflinePrerequisiteStatus.Ready);
        });
        var running = diagnostic.RunAsync("synthetic-root");
        try
        {
            await started.Task.WaitAsync(TestDeadline);
            diagnostic.Cancel();
        }
        finally { release.Set(); await running.WaitAsync(TestDeadline); }
        Assert.Null(diagnostic.Result);
        Assert.Empty(diagnostic.Output);
        Assert.Equal(OfflinePrerequisiteDiagnostic.UnexpectedFailure, diagnostic.Message);
        Assert.DoesNotContain(Private, diagnostic.Message);
        Assert.False(diagnostic.IsRunning);
    }

    [Fact]
    public async Task A_cancelled_check_throwing_before_a_result_does_not_invent_completed_findings()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var diagnostic = new OfflinePrerequisiteDiagnostic((_, token) =>
        {
            started.TrySetResult(); release.Wait(); token.ThrowIfCancellationRequested();
            return Result(OfflinePrerequisiteStatus.Ready);
        });
        var running = diagnostic.RunAsync("synthetic-root");
        try { await started.Task.WaitAsync(TestDeadline); diagnostic.Cancel(); }
        finally { release.Set(); await running.WaitAsync(TestDeadline); }
        Assert.Null(diagnostic.Result);
        Assert.Empty(diagnostic.Output);
        Assert.Contains("Cancelled before a diagnostic result completed", diagnostic.Message);
    }
}
