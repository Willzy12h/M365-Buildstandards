using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BDIT.TenantToolkit.App.Services;

/// <summary>The Win32 standard handle slots, using their GetStdHandle identifiers.</summary>
public enum StandardHandle
{
    Input = -10,
    Output = -11,
    Error = -12,
}

/// <summary>Where the CLI's error text is written once the standard handles have been prepared.</summary>
public enum CliErrorRoute
{
    /// <summary>Standard error is usable and receives refusals and failures.</summary>
    StandardError,
    /// <summary>No standard error exists even after the console attempt; errors share standard output.</summary>
    StandardOutput,
}

/// <summary>
/// The standard-handle operations the console bridge needs. Kept behind this seam so the handle selection is
/// decided by plain logic that tests can drive without a Windows console.
/// </summary>
public interface IStandardHandles
{
    IntPtr Get(StandardHandle kind);
    /// <summary>Attaches to the parent process's console, if it has one. Never allocates a console.</summary>
    bool AttachParentConsole();
    /// <summary>Sets a standard handle; false when the operating system refuses.</summary>
    bool Set(StandardHandle kind, IntPtr handle);
}

/// <summary>Offline CLI dispatch through the existing executable. No desktop workspace or authentication.</summary>
public static class PortableCliHost
{
    public static bool IsRequested(string[] args) => args.Length > 0 && string.Equals(args[0], "--cli", StringComparison.OrdinalIgnoreCase);
    public static string[] Arguments(string[] args) => IsRequested(args) ? args.Skip(1).ToArray()
        : throw new ArgumentException("CLI dispatch requires --cli as the first argument.", nameof(args));

    public static int Run(string[] args)
    {
        try
        {
            var command = Arguments(args);
            var route = PrepareStandardHandles(NativeStandardHandles.Instance);
            var (output, error) = CreateWriters(route, Console.OpenStandardOutput, Console.OpenStandardError);
            Console.SetOut(output);
            Console.SetError(error);
            return BDIT.TenantToolkit.Cli.Program.Main(command);
        }
        catch (Exception ex)
        {
            // No message box, new console, alternate host or sign-in on a command-line failure.
            Console.Error.WriteLine("Failed: offline command host could not start: " + ex.GetType().Name);
            return 3;
        }
    }

    /// <summary>
    /// A WinExe has no console by default. Attach to the parent console only when an output handle is missing, and
    /// preserve inherited pipe/file handles across the attachment: AttachConsole can otherwise replace redirected
    /// output with the parent's screen handles. Only standard output is required; without it the command is refused
    /// rather than run invisibly. A missing standard error is routed to standard output so no refusal is lost.
    /// </summary>
    public static CliErrorRoute PrepareStandardHandles(IStandardHandles handles)
    {
        ArgumentNullException.ThrowIfNull(handles);
        var output = handles.Get(StandardHandle.Output);
        var error = handles.Get(StandardHandle.Error);
        var input = handles.Get(StandardHandle.Input);
        if (NeedsParentConsole(output, error))
        {
            // Failure to attach (no parent console) is not itself fatal; the checks below decide.
            _ = handles.AttachParentConsole();
            Restore(handles, StandardHandle.Output, output);
            Restore(handles, StandardHandle.Error, error);
            Restore(handles, StandardHandle.Input, input);
        }
        if (!IsValid(handles.Get(StandardHandle.Output)))
            throw new IOException("No command output handle is available.");
        return IsValid(handles.Get(StandardHandle.Error)) ? CliErrorRoute.StandardError : CliErrorRoute.StandardOutput;
    }

    public static bool NeedsParentConsole(IntPtr output, IntPtr error) => !IsValid(output) || !IsValid(error);

    public static bool IsValid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    /// <summary>UTF-8 writers for the prepared handles. Standard error is opened only when it is the chosen route.</summary>
    public static (TextWriter Output, TextWriter Error) CreateWriters(CliErrorRoute route, Func<Stream> openOutput, Func<Stream> openError)
    {
        ArgumentNullException.ThrowIfNull(openOutput);
        ArgumentNullException.ThrowIfNull(openError);
        var output = new StreamWriter(openOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var error = route == CliErrorRoute.StandardError
            ? new StreamWriter(openError(), new UTF8Encoding(false)) { AutoFlush = true }
            : output;
        return (output, error);
    }

    private static void Restore(IStandardHandles handles, StandardHandle kind, IntPtr inherited)
    {
        if (IsValid(inherited) && !handles.Set(kind, inherited))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    private sealed class NativeStandardHandles : IStandardHandles
    {
        public static readonly NativeStandardHandles Instance = new();
        public IntPtr Get(StandardHandle kind) => GetStdHandle((int)kind);
        public bool AttachParentConsole() => AttachConsole(uint.MaxValue);
        public bool Set(StandardHandle kind, IntPtr handle) => SetStdHandle((int)kind, handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int kind);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetStdHandle(int kind, IntPtr handle);
    }
}
