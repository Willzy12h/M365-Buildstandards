using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BDIT.TenantToolkit.App.Services;

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
            // A WinExe has no console by default. Preserve inherited pipe/file handles across attachment;
            // AttachConsole can otherwise replace redirected output with the parent's screen handles.
            var output = GetStdHandle(-11); var error = GetStdHandle(-12); var input = GetStdHandle(-10);
            if (!Valid(output) || !Valid(error)) _ = AttachConsole(uint.MaxValue);
            Restore(-11, output); Restore(-12, error); Restore(-10, input);
            if (!Valid(GetStdHandle(-11)) || !Valid(GetStdHandle(-12)))
                throw new IOException("No command output handles are available.");
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            return BDIT.TenantToolkit.Cli.Program.Main(command);
        }
        catch (Exception ex)
        {
            // No message box, new console, alternate host or sign-in on a command-line failure.
            Console.Error.WriteLine("Failed: offline command host could not start: " + ex.GetType().Name);
            return 3;
        }
    }

    private static bool Valid(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);
    private static void Restore(int kind, IntPtr handle)
    {
        if (Valid(handle) && !SetStdHandle(kind, handle))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int kind, IntPtr handle);
}
