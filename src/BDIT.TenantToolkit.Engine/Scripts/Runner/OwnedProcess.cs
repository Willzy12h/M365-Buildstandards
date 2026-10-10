using System.ComponentModel;
using System.Diagnostics;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Scripts.Runner;

/// <summary>How an owned process ended.</summary>
public enum OwnedProcessEnd { Exited, TimedOut, Cancelled, LimitExceeded, NotStarted, OutputHeldOpen }

/// <summary>The end state, the exit code when the process ended by itself, and whether it was stopped by force.</summary>
public sealed record OwnedProcessOutcome(OwnedProcessEnd End, int? ExitCode, bool Killed);

/// <summary>
/// Runs exactly the <see cref="ProcessStartInfo"/> it is given and owns that process tree until it ends. Both pipes are
/// drained by the caller's readers. A timeout or a reader-detected limit ends the tree at once. Cancellation first asks the
/// child to stop cooperatively, then ends the tree after the grace period. It never chooses another host, changes policy
/// or installs anything.
/// </summary>
public static class OwnedProcess
{
    public static async Task<OwnedProcessOutcome> RunAsync(ProcessStartInfo start, Func<StreamReader, Task> readOutput,
        Func<StreamReader, Task> readErrors, TimeSpan timeout, Func<bool> limitExceeded, Action requestStop, TimeSpan stopGrace,
        CancellationToken ct)
    {
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError || !string.IsNullOrEmpty(start.Arguments))
            throw new ConfigurationException("An owned process must redirect its output, never use the shell and take only an argument list.");
        ct.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) return new(OwnedProcessEnd.NotStarted, null, false); }
        catch (Win32Exception) { return new(OwnedProcessEnd.NotStarted, null, false); }
        var output = readOutput(process.StandardOutput);
        var errors = readErrors(process.StandardError);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var deadline = Task.Delay(timeout, CancellationToken.None);
        var cancelled = Task.Delay(Timeout.Infinite, ct);
        var end = OwnedProcessEnd.Exited;
        var kill = false;
        while (!exit.IsCompleted)
        {
            var first = await Task.WhenAny(exit, deadline, cancelled, Task.Delay(250, CancellationToken.None));
            if (first == exit) break;
            if (first == deadline) { end = OwnedProcessEnd.TimedOut; kill = true; break; }
            if (limitExceeded()) { end = OwnedProcessEnd.LimitExceeded; kill = true; break; }
            if (first == cancelled)
            {
                end = OwnedProcessEnd.Cancelled;
                requestStop();
                // A module call in flight may not poll the stop file, so the grace period is a hard limit.
                if (await Task.WhenAny(exit, Task.Delay(stopGrace, CancellationToken.None)) != exit) kill = true;
                break;
            }
        }
        if (kill)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // It ended between the check and the kill.
            catch (Win32Exception) { }
        }
        await exit;
        // A descendant that inherited the pipes can keep them open after the process ends; never wait on it indefinitely.
        var readers = Task.WhenAll(output, errors);
        if (await Task.WhenAny(readers, Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None)) != readers)
            return new(OwnedProcessEnd.OutputHeldOpen, null, kill);
        await readers;
        // A reader can reach its limit just as the process ends; that is still a stopped, bounded run.
        if (end == OwnedProcessEnd.Exited && limitExceeded()) end = OwnedProcessEnd.LimitExceeded;
        return new(end, kill ? null : process.ExitCode, kill);
    }
}
