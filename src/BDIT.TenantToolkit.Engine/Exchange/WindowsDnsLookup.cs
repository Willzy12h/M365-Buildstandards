using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;
using BDIT.TenantToolkit.Core.Safety;

namespace BDIT.TenantToolkit.Engine.Exchange;

/// <summary>Fixed Windows DNS read adapter. No shell input, profile, policy bypass or tenant authentication.</summary>
public sealed class WindowsDnsLookup : IDnsLookup
{
    private readonly Func<string, CancellationToken, Task<string>> _run;
    public WindowsDnsLookup(Func<string, CancellationToken, Task<string>>? run = null) => _run = run ?? RunWindowsAsync;

    public async Task<DnsObservation> QueryAsync(string name, DnsRecordKind kind, CancellationToken cancellationToken)
    {
        var script = QueryScript(name, kind);
        try
        {
            var json = await _run(script, cancellationToken).ConfigureAwait(false);
            if (json.Length > 65536) throw new ConfigurationException("DNS response exceeded the supported limit.");
            var result = ToolkitJson.Deserialize<DnsObservation>(json);
            if (result.Name != name || result.Kind != kind) throw new ConfigurationException("DNS response does not match the question.");
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException or ToolkitException or OperationCanceledException)
        {
            return new DnsObservation { Name = name, Kind = kind, Status = DnsResultStatus.Error,
                QueriedAt = Timestamps.Format(DateTimeOffset.UtcNow), Error = "DNS lookup failed or timed out. No absence or signing readiness can be inferred." };
        }
    }

    public static string QueryScript(string name, DnsRecordKind kind)
    {
        MailDomain.ValidateQuery(name);
        if (!Enum.IsDefined(kind)) throw new ConfigurationException("Unsupported DNS record type.");
        var type = kind == DnsRecordKind.Cname ? "CNAME" : "TXT";
        return $$"""
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
            $result = [ordered]@{ name = '{{name}}'; kind = '{{kind}}'; status = 'Error'; records = @(); queriedAt = [DateTimeOffset]::UtcNow.ToString('O') }
            try {
                $answers = @(DnsClient\Resolve-DnsName -Name '{{name}}' -Type {{type}} -DnsOnly -NoHostsFile -QuickTimeout -ErrorAction Stop |
                    Where-Object { $_.Name.TrimEnd('.') -ieq '{{name}}' -and $_.Type.ToString() -eq '{{type}}' })
                if ($answers.Count -gt 100) { throw 'Too many DNS answers.' }
                $result.records = @($answers | ForEach-Object { if ('{{type}}' -eq 'CNAME') { $_.NameHost } else { $_.Strings -join '' } })
                if ($result.records.Count -gt 0) { $result.status = 'Present' } else { $result.status = 'NoRecords' }
            }
            catch {
                # Only documented native NXDOMAIN/no-record codes establish absence. Other failures stay unknown.
                $code = 0
                if ($_.Exception -is [ComponentModel.Win32Exception]) { $code = $_.Exception.NativeErrorCode }
                if ($code -in @(9003, 9501)) { $result.status = 'NoRecords' } else { $result.status = 'Error'; $result.error = 'DNS query failed; absence is unknown.' }
                $result.records = @()
            }
            $result | ConvertTo-Json -Depth 4 -Compress
            """;
    }

    private static async Task<string> RunWindowsAsync(string script, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new ConfigurationException("The DNS adapter requires Windows; no lookup was made.");
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            process.StartInfo.ArgumentList.Add(argument);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        process.Start();
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError, timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var text = await output.ConfigureAwait(false); _ = await error.ConfigureAwait(false);
            if (process.ExitCode != 0) throw new ConfigurationException("The DNS helper failed.");
            return text;
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder(); var buffer = new char[2048];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (count == 0) return text.ToString();
            if (text.Length + count > 65536) throw new ConfigurationException("DNS helper output exceeded its limit.");
            text.Append(buffer, 0, count);
        }
    }
}
