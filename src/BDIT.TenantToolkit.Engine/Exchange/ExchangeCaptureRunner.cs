using System.Diagnostics;
using System.Text;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Exchange;

public interface IExchangeCaptureRunner
{
    Task<string> CaptureAsync(string tenantId, string referenceDomain, string account, bool includePurview,
        IProgress<string>? progress, CancellationToken ct);
}

/// <summary>Owns one fresh PowerShell process. Runs only the embedded read template, never caller-supplied code.</summary>
public sealed class ExchangeCaptureRunner : IExchangeCaptureRunner
{
    public const string DependencyGuidance = "Install a supported ExchangeOnlineManagement module (3.7.0 or later) through your organisation's approved PowerShell process. The toolkit does not install modules or bypass script policy. WAM is the module's default; Microsoft can require separate Exchange/Purview authentication and RBAC.";

    public async Task<string> CaptureAsync(string tenantId, string referenceDomain, string account, bool includePurview,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Integrated Exchange capture requires Windows.");
        // Generate and validate before creating files or starting a process. User values never become shell code.
        var script = ExchangeCaptureScripts.ReadOnlyCapture(tenantId, referenceDomain);
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(executable)) throw new ConfigurationException("Windows PowerShell 5.1 is unavailable. " + DependencyGuidance);
        var directory = Path.Combine(Path.GetTempPath(), "BDIT-Exchange-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var scriptFile = Path.Combine(directory, "Read-Capture.ps1");
            var resultFile = Path.Combine(directory, "capture.json");
            await File.WriteAllTextAsync(scriptFile, script, new UTF8Encoding(false), ct);
            var start = CreateStartInfo(executable, scriptFile, resultFile, account, includePurview);
            progress?.Report("Checking the installed Exchange module, then opening Microsoft sign-in in a separate PowerShell process. No tenant writes.");
            using var process = new Process { StartInfo = start };
            ct.ThrowIfCancellationRequested();
            if (!process.Start()) throw new ConfigurationException("The Exchange read process could not start.");
            // Drain both pipes without retaining arbitrary authentication/module output or writing it to logs.
            var output = ReadProgressAsync(process.StandardOutput, progress);
            var errors = DrainAsync(process.StandardError);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(output, errors);
                ct.ThrowIfCancellationRequested();
                throw new AuthenticationRequiredException("Exchange/Purview capture exceeded fifteen minutes. Nothing was imported; retry deliberately after checking Microsoft sign-in.");
            }
            await Task.WhenAll(output, errors);
            if (process.ExitCode != 0 || !File.Exists(resultFile))
                throw new ConfigurationException("Exchange capture did not produce verified evidence. Check module availability, script policy, Microsoft sign-in and read-only RBAC. " + DependencyGuidance);
            return await ReadBoundedResultAsync(resultFile, ct);
        }
        finally
        {
            // Only the GUID directory created by this invocation is removed.
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static ProcessStartInfo CreateStartInfo(string executable, string scriptFile, string resultFile,
        string account, bool includePurview)
    {
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = false };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", scriptFile, "-OutputFile", resultFile, "-Integrated" }) start.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(account)) { start.ArgumentList.Add("-UserPrincipalName"); start.ArgumentList.Add(account); }
        if (includePurview) start.ArgumentList.Add("-IncludePurview");
        return start;
    }

    public static async Task<string> ReadBoundedResultAsync(string file, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ExchangeCaptureSchema.MaximumBytes) throw new ConfigurationException("Exchange capture exceeds the 2 MiB limit.");
        var bytes = new byte[ExchangeCaptureSchema.MaximumBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(length), ct);
            if (count == 0) break;
            length += count;
        }
        if (length > ExchangeCaptureSchema.MaximumBytes) throw new ConfigurationException("Exchange capture exceeds the 2 MiB limit.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, length);
    }

    private static async Task DrainAsync(StreamReader reader)
    { var buffer = new char[4096]; while (await reader.ReadAsync(buffer) != 0) { } }

    private static async Task ReadProgressAsync(StreamReader reader, IProgress<string>? progress)
    {
        var buffer = new char[4096]; var line = new StringBuilder(); bool oversized = false; int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
            for (var i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c == '\n')
                {
                    if (!oversized) ReportKnownProgress(line.ToString().TrimEnd('\r'), progress);
                    line.Clear(); oversized = false;
                }
                else if (!oversized && line.Length < 256) line.Append(c);
                else oversized = true;
            }
    }
    private static void ReportKnownProgress(string line, IProgress<string>? progress)
    {
        if (line == "BDIT:EXCHANGE") progress?.Report("Microsoft Exchange sign-in; tenant identity will be verified before reading.");
        else if (line == "BDIT:PURVIEW") progress?.Report("Microsoft Purview sign-in; separate tenant identity and read RBAC are required.");
        else if (line == "BDIT:MODULE_MISSING") progress?.Report(DependencyGuidance);
        else if (line.StartsWith("BDIT:READ:", StringComparison.Ordinal) && ExchangeCaptureSchema.Definitions.TryGetValue(line[10..], out var definition))
            progress?.Report("Reading " + definition.Command + " (read-only).");
    }
}
