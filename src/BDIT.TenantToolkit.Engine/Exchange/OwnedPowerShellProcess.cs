using System.Diagnostics;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Exchange;

/// <summary>How an owned PowerShell process ended.</summary>
public enum OwnedProcessEnd { Exited, TimedOut, Cancelled, LimitExceeded }

public sealed record OwnedProcessOutcome(OwnedProcessEnd End, int? ExitCode);

/// <summary>
/// The one bounded child-process runner for PowerShell, shared by the integrated Exchange/Purview capture and the
/// Scripts &amp; Reports Run action. It starts exactly the <see cref="ProcessStartInfo"/> it is given, drains both pipes
/// through the caller's readers, and on timeout, cancellation or a caller-detected limit kills the whole process tree
/// and waits for it to end. It never chooses another host, changes execution policy or installs anything.
/// </summary>
public static class OwnedPowerShellProcess
{
    /// <summary>Windows PowerShell 5.1 at its fixed system location. No PATH lookup is made.</summary>
    public static string WindowsPowerShellPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>PowerShell 7 at its standard machine-wide install location. No PATH lookup is made.</summary>
    public static string PowerShell7Path() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");

    /// <summary>
    /// Runs the process to completion or until it is stopped. <paramref name="limitExceeded"/>, when given, is checked
    /// about four times a second; returning true stops the process as <see cref="OwnedProcessEnd.LimitExceeded"/>.
    /// </summary>
    public static async Task<OwnedProcessOutcome> RunAsync(ProcessStartInfo start, Func<StreamReader, Task> readOutput,
        Func<StreamReader, Task> readErrors, TimeSpan timeout, Func<bool>? limitExceeded, CancellationToken ct)
    {
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError)
            throw new ConfigurationException("An owned PowerShell process must redirect its output and never use the shell.");
        using var process = new Process { StartInfo = start };
        ct.ThrowIfCancellationRequested();
        if (!process.Start()) throw new ConfigurationException("The PowerShell process could not start.");
        var output = readOutput(process.StandardOutput);
        var errors = readErrors(process.StandardError);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(timeout);
        var end = OwnedProcessEnd.Exited;
        try
        {
            var exit = process.WaitForExitAsync(stop.Token);
            while (true)
            {
                if (limitExceeded is null) { await exit; break; }
                var first = await Task.WhenAny(exit, Task.Delay(250, stop.Token));
                if (first == exit) { await exit; break; }
                stop.Token.ThrowIfCancellationRequested();
                if (limitExceeded()) { end = OwnedProcessEnd.LimitExceeded; break; }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            end = ct.IsCancellationRequested ? OwnedProcessEnd.Cancelled : OwnedProcessEnd.TimedOut;
        }

        if (end != OwnedProcessEnd.Exited)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // It ended between the check and the kill.
            await process.WaitForExitAsync(CancellationToken.None);
        }
        await Task.WhenAll(output, errors);
        // A limit can also be reached by a reader just as the process ends; that is still a stopped, bounded run.
        if (end == OwnedProcessEnd.Exited && limitExceeded?.Invoke() == true) end = OwnedProcessEnd.LimitExceeded;
        return new OwnedProcessOutcome(end, end == OwnedProcessEnd.Exited ? process.ExitCode : null);
    }

    /// <summary>Reads a pipe to its end and keeps nothing, so a chatty process can never block on a full pipe.</summary>
    public static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) != 0) { }
    }
}
