using System.Diagnostics;
using BDIT.TenantToolkit.Engine.Scripts.Runner;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// AST-20261010-05: the owned process is ended before <see cref="OwnedProcess.RunAsync"/> returns or throws, whatever a
/// caller's callback does. Each case starts a real, harmless child that prints its own process id and then sleeps, so the
/// test can prove the child is gone rather than trusting the outcome. No PowerShell module, sign-in or tenant is used.
/// </summary>
public sealed class OwnedProcessTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(200);

    /// <summary>A child that prints its process id, then sleeps for 30 seconds.</summary>
    private static ProcessStartInfo Sleeper()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            : new ProcessStartInfo("/bin/sh");
        start.UseShellExecute = false; start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.CreateNoWindow = true;
        foreach (var argument in OperatingSystem.IsWindows()
            ? new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "$PID; Start-Sleep -Seconds 30" }
            : ["-c", "echo $$; exec sleep 30"])
            start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>Reads the child's process id from its first output line, then drains the rest.</summary>
    private sealed class Child
    {
        private readonly TaskCompletionSource<int> _pid = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<int> Pid => _pid.Task;

        public async Task ReadAsync(StreamReader reader)
        {
            var first = await reader.ReadLineAsync();
            if (int.TryParse(first?.Trim(), out var pid)) _pid.TrySetResult(pid); else _pid.TrySetException(new InvalidOperationException("No process id."));
            await reader.ReadToEndAsync();
        }

        public async Task<int> StartedAsync() => await Pid.WaitAsync(TimeSpan.FromSeconds(30));

        /// <summary>True when the child has ended. A process id the system has already reused is reported as ended only if it exited.</summary>
        public async Task<bool> GoneAsync()
        {
            var pid = await Pid;
            try { using var p = Process.GetProcessById(pid); return p.HasExited; }
            catch (ArgumentException) { return true; }
        }

        /// <summary>Ends a child a failed case left behind, so one failure never strands a process on the runner.</summary>
        public async Task CleanUpAsync()
        {
            if (!Pid.IsCompletedSuccessfully) return;
            try { using var p = Process.GetProcessById(await Pid); if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
    }

    private static Task Drain(StreamReader reader) => reader.ReadToEndAsync();

    [Fact]
    public async Task A_stop_request_that_throws_still_ends_the_child_and_reports_a_forced_stop()
    {
        var child = new Child();
        using var cts = new CancellationTokenSource();
        try
        {
            var run = OwnedProcess.RunAsync(Sleeper(), child.ReadAsync, Drain, Long, () => false,
                () => throw new UnauthorizedAccessException("The stop file could not be created."), Grace, cts.Token);
            await child.StartedAsync();
            cts.Cancel();
            var outcome = await run;
            Assert.Equal(OwnedProcessEnd.Cancelled, outcome.End);
            Assert.True(outcome.Killed);
            Assert.True(await child.GoneAsync(), "The child was still running after the run returned.");
        }
        finally { await child.CleanUpAsync(); }
    }

    [Fact]
    public async Task A_limit_check_that_throws_ends_the_child_before_the_error_is_returned()
    {
        var child = new Child();
        var started = false;
        try
        {
            var run = OwnedProcess.RunAsync(Sleeper(), child.ReadAsync, Drain, Long,
                () => Volatile.Read(ref started) ? throw new InvalidOperationException("Limit check failed.") : false,
                () => { }, Grace, CancellationToken.None);
            await child.StartedAsync();
            Volatile.Write(ref started, true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => run);
            Assert.True(await child.GoneAsync(), "The child was still running after the error was returned.");
        }
        finally { await child.CleanUpAsync(); }
    }

    [Fact]
    public async Task A_reader_that_throws_before_it_starts_ends_the_child_before_the_error_is_returned()
    {
        var child = new Child();
        try
        {
            // The output reader records the id; the error reader then fails synchronously, after the child started.
            var run = OwnedProcess.RunAsync(Sleeper(), child.ReadAsync,
                _ => { child.Pid.Wait(TimeSpan.FromSeconds(30)); throw new InvalidOperationException("Reader failed."); },
                Long, () => false, () => { }, Grace, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => run);
            Assert.True(await child.GoneAsync(), "The child was still running after the error was returned.");
        }
        finally { await child.CleanUpAsync(); }
    }

    [Fact]
    public async Task A_child_the_system_will_not_stop_is_reported_as_not_stopped_rather_than_ended()
    {
        var child = new Child();
        using var cts = new CancellationTokenSource();
        try
        {
            // The termination step is replaced by one that does nothing, as when the system refuses to end the process.
            var run = OwnedProcess.RunAsync(Sleeper(), child.ReadAsync, Drain, Long, () => false, () => { }, Grace, cts.Token,
                terminate: _ => { }, terminationWait: TimeSpan.FromMilliseconds(500));
            await child.StartedAsync();
            cts.Cancel();
            var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(OwnedProcessEnd.NotStopped, outcome.End);
            Assert.False(outcome.Killed);
            Assert.Null(outcome.ExitCode);
            Assert.False(await child.GoneAsync());
        }
        finally { await child.CleanUpAsync(); }
    }

    /// <summary>A termination step that fails the way a system refusal does.</summary>
    private static void Refuse(Process _) => throw new System.ComponentModel.Win32Exception(5, "Access is denied.");

    [Fact]
    public async Task A_limit_check_that_throws_when_the_system_will_not_stop_the_child_is_reported_as_not_stopped()
    {
        // AST-20261010-19: the callback's own error must not hide that the child may still be running.
        var child = new Child();
        var started = false;
        try
        {
            var run = OwnedProcess.RunAsync(Sleeper(), child.ReadAsync, Drain, Long,
                () => Volatile.Read(ref started) ? throw new InvalidOperationException("Limit check failed.") : false,
                () => { }, Grace, CancellationToken.None, terminate: Refuse, terminationWait: TimeSpan.FromMilliseconds(500));
            await child.StartedAsync();
            Volatile.Write(ref started, true);
            var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(new OwnedProcessOutcome(OwnedProcessEnd.NotStopped, null, false), outcome);
            Assert.False(await child.GoneAsync());
        }
        finally { await child.CleanUpAsync(); }
    }

    [Fact]
    public async Task A_reader_that_throws_when_the_system_will_not_stop_the_child_is_reported_as_not_stopped()
    {
        var child = new Child();
        try
        {
            var run = OwnedProcess.RunAsync(Sleeper(), child.ReadAsync,
                _ => { child.Pid.Wait(TimeSpan.FromSeconds(30)); throw new InvalidOperationException("Reader failed."); },
                Long, () => false, () => { }, Grace, CancellationToken.None, terminate: Refuse, terminationWait: TimeSpan.FromMilliseconds(500));
            var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(OwnedProcessEnd.NotStopped, outcome.End);
            Assert.False(await child.GoneAsync());
        }
        finally { await child.CleanUpAsync(); }
    }

    [Fact]
    public async Task A_cooperative_stop_that_works_ends_without_force()
    {
        // Positive control: a stop request the child honours ends the run as cancelled without a forced stop.
        var start = Sleeper();
        var child = new Child();
        using var cts = new CancellationTokenSource();
        try
        {
            var run = OwnedProcess.RunAsync(start, child.ReadAsync, Drain, Long, () => false,
                () => { using var p = Process.GetProcessById(child.Pid.Result); p.Kill(); }, TimeSpan.FromSeconds(10), cts.Token);
            await child.StartedAsync();
            cts.Cancel();
            var outcome = await run;
            Assert.Equal(OwnedProcessEnd.Cancelled, outcome.End);
            Assert.False(outcome.Killed);
            Assert.True(await child.GoneAsync());
        }
        finally { await child.CleanUpAsync(); }
    }

    [Fact]
    public async Task A_child_that_exits_by_itself_reports_its_exit_code()
    {
        // Positive control: nothing is forced when the child ends normally.
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            : new ProcessStartInfo("/bin/sh");
        start.UseShellExecute = false; start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.CreateNoWindow = true;
        foreach (var argument in OperatingSystem.IsWindows() ? new[] { "/d", "/c", "exit 7" } : ["-c", "exit 7"]) start.ArgumentList.Add(argument);
        var outcome = await OwnedProcess.RunAsync(start, Drain, Drain, Long, () => false, () => { }, Grace, CancellationToken.None);
        Assert.Equal(new OwnedProcessOutcome(OwnedProcessEnd.Exited, 7, false), outcome);
    }
}
