using System.Globalization;

namespace BDIT.TenantToolkit.Cli;

/// <summary>Owns the doctor's console handler and buffers output until cancellation cleanup is complete.</summary>
internal sealed class DoctorConsoleCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly object _gate = new();
    private readonly ConsoleCancelEventHandler _handler;
    private readonly Action<ConsoleCancelEventHandler> _unsubscribe;
    private bool _disposed;
    private bool _failed;
    internal CancellationToken Token { get; }
    internal bool Failed { get { lock (_gate) return _failed; } }

    private DoctorConsoleCancellation() : this(handler => Console.CancelKeyPress += handler,
        handler => Console.CancelKeyPress -= handler) { }

    // Tests substitute event registration only. Production always uses the actual Console event.
    internal DoctorConsoleCancellation(Action<ConsoleCancelEventHandler> subscribe, Action<ConsoleCancelEventHandler> unsubscribe)
    {
        Token = _source.Token;
        _unsubscribe = unsubscribe;
        _handler = OnCancel;
        try { subscribe(_handler); }
        catch
        {
            lock (_gate) _disposed = true;
            try { unsubscribe(_handler); } catch (Exception) { }
            _source.Dispose();
            throw;
        }
    }

    private void OnCancel(object? sender, ConsoleCancelEventArgs args)
    {
        lock (_gate)
        {
            // A queued event after detach belongs to the later command; do not suppress its normal behaviour.
            if (_disposed) return;
            args.Cancel = true;
            try { _source.Cancel(); }
            catch (Exception) { _failed = true; } // Callback failures must never escape on the console event thread.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; // Waits for an active cancellation before allowing source disposal.
        }
        try { _unsubscribe(_handler); }
        catch (Exception) { lock (_gate) _failed = true; }
        finally { _source.Dispose(); }
    }

    internal static int RunCommand(string[] commandArgs, TextWriter output, TextWriter error) =>
        Run(output, error, (token, stagedOutput, stagedError) =>
            DoctorCommand.Run(commandArgs.Skip(1).ToArray(), null, stagedOutput, stagedError, token), () => new());

    internal static int Run(TextWriter output, TextWriter error,
        Func<CancellationToken, TextWriter, TextWriter, int> command, Func<DoctorConsoleCancellation> createScope)
    {
        try
        {
            using var stagedOutput = new StringWriter(CultureInfo.InvariantCulture);
            using var stagedError = new StringWriter(CultureInfo.InvariantCulture);
            var scope = createScope();
            int exit;
            try { exit = command(scope.Token, stagedOutput, stagedError); }
            finally { scope.Dispose(); }
            if (scope.Failed) return Failure(error);
            output.Write(stagedOutput.ToString());
            error.Write(stagedError.ToString());
            return exit;
        }
        catch (Exception) { return Failure(error); }
    }

    private static int Failure(TextWriter error)
    {
        try { error.WriteLine("Doctor failed unexpectedly. No readiness was certified. Review the offline failure with the toolkit maintainer; if a probe started, review temporary .bdit-doctor- files in the portable folder."); }
        catch (Exception) { /* An unusable stderr must not expose its exception through the general CLI catch. */ }
        return 3;
    }
}
