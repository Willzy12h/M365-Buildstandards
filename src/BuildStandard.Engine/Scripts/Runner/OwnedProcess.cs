using System.ComponentModel;
using System.Diagnostics;
using BuildStandard.Core;

namespace BuildStandard.Engine.Scripts.Runner;

/// <summary>
/// How an owned process ended. <see cref="OwnedProcessEnd.NotStopped"/> means the system did not end the process when asked:
/// it may still be running, and nothing it wrote is trusted.
/// </summary>
public enum OwnedProcessEnd { Exited, TimedOut, Cancelled, LimitExceeded, NotStarted, OutputHeldOpen, NotStopped }

/// <summary>The end state, the exit code when the process ended by itself, and whether it was stopped by force.</summary>
public sealed record OwnedProcessOutcome(OwnedProcessEnd End, int? ExitCode, bool Killed);

/// <summary>
/// Runs exactly the <see cref="ProcessStartInfo"/> it is given and owns that process tree until it ends. Both pipes are
/// drained by the caller's readers. A timeout or a reader-detected limit ends the tree at once. Cancellation first asks the
/// child to stop cooperatively, then ends the tree after the grace period; a stop request that fails ends it at once. Once
/// the process has started, it is ended before this method returns or throws, whatever a caller's callback does, and a
/// process the system will not end is reported as not stopped rather than as ended. It never chooses another host, changes
/// policy or installs anything.
/// </summary>
public static class OwnedProcess
{
    /// <summary>How long to wait for the system to end the tree after asking it to.</summary>
    internal static readonly TimeSpan TerminationWait = TimeSpan.FromSeconds(10);

    public static Task<OwnedProcessOutcome> RunAsync(ProcessStartInfo start, Func<StreamReader, Task> readOutput,
        Func<StreamReader, Task> readErrors, TimeSpan timeout, Func<bool> limitExceeded, Action requestStop, TimeSpan stopGrace,
        CancellationToken ct) =>
        RunAsync(start, readOutput, readErrors, timeout, limitExceeded, requestStop, stopGrace, ct, terminate: null, terminationWait: null);

    /// <param name="terminate">Replaces the tree termination step; tests use it to stand in for a system that refuses.</param>
    /// <param name="ownedLimitExceeded">Optional sampled resource limit for this exact owned process. It is not an OS allocation cap.</param>
    internal static async Task<OwnedProcessOutcome> RunAsync(ProcessStartInfo start, Func<StreamReader, Task> readOutput,
        Func<StreamReader, Task> readErrors, TimeSpan timeout, Func<bool> limitExceeded, Action requestStop, TimeSpan stopGrace,
        CancellationToken ct, Action<Process>? terminate, TimeSpan? terminationWait, Func<Process, bool>? ownedLimitExceeded = null)
    {
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError || !string.IsNullOrEmpty(start.Arguments))
            throw new ConfigurationException("An owned process must redirect its output, never use the shell and take only an argument list.");
        ct.ThrowIfCancellationRequested();
        terminate ??= p => p.Kill(entireProcessTree: true);
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) return new(OwnedProcessEnd.NotStarted, null, false); }
        catch (Win32Exception) { return new(OwnedProcessEnd.NotStarted, null, false); }

        var exit = process.WaitForExitAsync(CancellationToken.None);
        var stopped = false;
        try
        {
            var output = readOutput(process.StandardOutput);
            var errors = readErrors(process.StandardError);
            var deadline = Task.Delay(timeout, CancellationToken.None);
            var cancelled = Task.Delay(Timeout.Infinite, ct);
            var end = OwnedProcessEnd.Exited;
            var kill = false;
            while (!exit.IsCompleted)
            {
                var first = await Task.WhenAny(exit, deadline, cancelled, Task.Delay(250, CancellationToken.None));
                if (first == exit) break;
                if (first == deadline) { end = OwnedProcessEnd.TimedOut; kill = true; break; }
                if (limitExceeded() || (ownedLimitExceeded?.Invoke(process) ?? false)) { end = OwnedProcessEnd.LimitExceeded; kill = true; break; }
                if (first == cancelled)
                {
                    end = OwnedProcessEnd.Cancelled;
                    // A stop request that cannot be made (the stop file cannot be created, say) ends the tree at once.
                    var asked = true;
                    try { requestStop(); }
                    catch (Exception) { asked = false; }
                    // A module call in flight may not poll the stop file, so the grace period is a hard limit.
                    if (!asked || await Task.WhenAny(exit, Task.Delay(stopGrace, CancellationToken.None)) != exit) kill = true;
                    break;
                }
            }
            if (kill && !await TerminateAsync(process, exit, terminate, terminationWait ?? TerminationWait))
            {
                stopped = true; // Asked once already; a second attempt would only repeat the refusal.
                return new(OwnedProcessEnd.NotStopped, null, false);
            }
            await exit;
            stopped = true;
            // A descendant that inherited the pipes can keep them open after the process ends; never wait on it indefinitely.
            var readers = Task.WhenAll(output, errors);
            if (await Task.WhenAny(readers, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None)) != readers)
                return new(OwnedProcessEnd.OutputHeldOpen, null, kill);
            await readers;
            // A reader can reach its limit just as the process ends; that is still a stopped, bounded run.
            if (end == OwnedProcessEnd.Exited && limitExceeded()) end = OwnedProcessEnd.LimitExceeded;
            return new(end, kill ? null : process.ExitCode, kill);
        }
        catch (Exception) when (!stopped && !exit.IsCompleted)
        {
            // Whatever failed above, a started process is never left running behind a thrown result. When it is ended, the
            // original error stands; when the system will not end it, that is what the caller must be told (AST-20261010-19).
            if (!await TerminateAsync(process, exit, terminate, terminationWait ?? TerminationWait))
                return new(OwnedProcessEnd.NotStopped, null, false);
            throw;
        }
    }

    /// <summary>Ends the tree and waits a bounded time for it to go. False when the process is still running afterwards.</summary>
    private static async Task<bool> TerminateAsync(Process process, Task exit, Action<Process> terminate, TimeSpan wait)
    {
        try { if (!process.HasExited) terminate(process); }
        catch (InvalidOperationException) { } // It ended between the check and the kill.
        catch (Win32Exception) { }            // The system refused; the wait below decides what is reported.
        return await Task.WhenAny(exit, Task.Delay(wait, CancellationToken.None)) == exit;
    }
}
