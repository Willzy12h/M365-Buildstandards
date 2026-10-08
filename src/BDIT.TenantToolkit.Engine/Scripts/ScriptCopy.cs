using System.Globalization;
using System.Text;
using BDIT.TenantToolkit.Core;

namespace BDIT.TenantToolkit.Engine.Scripts;

/// <summary>Who the copied script is for. All optional: without a tenant the script still refuses an ambiguous connection.</summary>
public sealed record ScriptCopyTarget(string? TenantId = null, string? TenantName = null, string? Account = null);

/// <summary>
/// Produces the standalone Copy script (INT-072): the reviewed body unchanged, the engineer's values as quoted literals,
/// module requirements, an Exchange Online sign-in with a tenant check, and a CSV of the declared columns. It contains no
/// credentials, installation, policy change or upload, and nothing the engineer typed can become code.
/// </summary>
public static class ScriptCopy
{
    // PowerShell treats the typographic single quotes as quote characters too, so each is doubled like the ASCII one.
    private static readonly char[] SingleQuotes = { '\'', '‘', '’', '‚', '‛' };

    public static string Generate(ScriptEntry entry, ScriptBinding binding, ScriptCopyTarget target, DateTimeOffset now)
    {
        if (!binding.IsValid) throw new ConfigurationException("The form is not complete: " + string.Join(" ", binding.Problems));
        var m = entry.Manifest;
        var tenant = "";
        if (!string.IsNullOrWhiteSpace(target.TenantId))
            tenant = Guid.TryParse(target.TenantId, out var parsed) ? parsed.ToString("D") : throw new ConfigurationException("The tenant ID is not a GUID.");
        var account = "";
        if (!string.IsNullOrWhiteSpace(target.Account))
        {
            var problems = new List<string>();
            var upn = new ScriptParameter("Account", "Sign-in account", ScriptParameterType.String, "Account", Format: ScriptValueFormat.Upn);
            if (ScriptInputs.Convert(upn, target.Account, problems) is not ScriptText valid) throw new ConfigurationException(string.Join(" ", problems));
            account = valid.Value;
        }

        var b = new StringBuilder();
        void Line(string text = "") => b.Append(text).Append('\n');

        Line("# M365 BuildStandard Tool - copied library script");
        Line("# Item:        " + Comment(m.Name) + " (" + m.Id + ")");
        Line("# Type:        " + (m.Mode == ScriptMode.ReadOnly ? "Read only. Makes no changes." : "CHANGE. Review every line before running."));
        Line("# Purpose:     " + Comment(m.Description));
        if (tenant.Length > 0) Line("# Tenant:      " + Comment(target.TenantName ?? "") + " (" + tenant + ")");
        Line("# Needs:       " + string.Join("; ", m.Modules.Select(x => x.Name + " " + x.MinimumVersion + " or later")) + (m.Roles.Count > 0 ? "; role " + string.Join(" or ", m.Roles) : ""));
        Line("# PowerShell:  " + string.Join(" or ", m.SupportedRuntimes));
        Line("# Script hash: " + m.ScriptSha256 + " (SHA-256 of the reviewed body below)");
        Line("# Generated:   " + now.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        Line("# Live status: not yet tested in a tenant.");
        foreach (var limitation in m.Limitations) Line("# Limitation:  " + Comment(limitation));
        Line("#requires -Version " + (m.SupportedRuntimes.Contains("5.1") ? "5.1" : "7.0"));
        foreach (var module in m.Modules) Line("#requires -Modules @{ ModuleName = " + Quote(module.Name) + "; ModuleVersion = " + Quote(module.MinimumVersion) + " }");
        Line("[CmdletBinding()]");
        Line("param([string]$OutputCsv = '')");
        Line("Set-StrictMode -Version Latest");
        Line("$ErrorActionPreference = 'Stop'");
        Line();
        Line("$expectedTenantId = " + Quote(tenant));
        Line("$signInAs = " + Quote(account));
        Line();
        Line("# Exchange Online sign-in opens a Microsoft sign-in window. This script saves no credentials or tokens.");
        Line("$connect = @{ ShowBanner = $false }");
        Line("if ($signInAs) { $connect.UserPrincipalName = $signInAs }");
        Line("Connect-ExchangeOnline @connect");
        Line("try {");
        Line("    $connections = @(Get-ConnectionInformation | Where-Object { $_.State -eq 'Connected' -and -not $_.IsEopSession })");
        Line("    if ($connections.Count -ne 1) { throw 'Expected exactly one Exchange Online connection. Close other sessions and run again. Nothing was read.' }");
        Line("    if ($expectedTenantId -and ([guid]$connections[0].TenantID -ne [guid]$expectedTenantId)) {");
        Line("        throw ('Signed in to tenant ' + $connections[0].TenantID + ', not ' + $expectedTenantId + '. Nothing was read.')");
        Line("    }");
        Line("    Write-Host ('Connected to tenant ' + $connections[0].TenantID + ' as ' + $connections[0].UserPrincipalName + '.')");
        Line();
        Line("    $arguments = @{");
        foreach (var argument in binding.Arguments) Line("        " + argument.Name + " = " + Literal(argument.Value));
        Line("    }");
        Line();
        Line("    # Reviewed library body, unchanged.");
        Line("    $library = {");
        // Not re-indented: a here-string terminator in the body must stay at the start of its line.
        b.Append(entry.Script.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n')).Append('\n');
        Line("    }");
        Line();
        Line("    $rows = @(& $library @arguments)");
        Line("    $rows | Format-Table -AutoSize | Out-String -Width 4096 | Write-Host");
        Line("    if (-not $OutputCsv) {");
        Line("        $OutputCsv = Join-Path ([Environment]::GetFolderPath('MyDocuments')) (" + Quote("BDIT-" + m.Id + "-") + " + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')");
        Line("    }");
        Line("    $rows | Select-Object " + string.Join(", ", m.OutputSchema.Columns.Select(Quote)) + " | Export-Csv -LiteralPath $OutputCsv -NoTypeInformation -NoClobber -Encoding UTF8");
        Line("    Write-Host ('Saved ' + $rows.Count + ' row(s) to ' + $OutputCsv)");
        Line("}");
        Line("finally {");
        Line("    Disconnect-ExchangeOnline -Confirm:$false");
        Line("}");
        return b.ToString();
    }

    /// <summary>A PowerShell single-quoted literal. Inside one, only a quote character is special, and doubling it escapes it.</summary>
    public static string Quote(string value)
    {
        var b = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            b.Append(c);
            if (Array.IndexOf(SingleQuotes, c) >= 0) b.Append(c);
        }
        return b.Append('\'').ToString();
    }

    private static string Literal(ScriptValue value) => value switch
    {
        ScriptText t => Quote(t.Value),
        ScriptTextList l => "@(" + string.Join(", ", l.Values.Select(Quote)) + ")",
        ScriptFlag f => f.Value ? "$true" : "$false",
        ScriptNumber n => n.Value.ToString(CultureInfo.InvariantCulture),
        _ => throw new ConfigurationException("Unsupported script value.")
    };

    // Comment text is already single-line plain text; this keeps it that way for display values that are not.
    private static string Comment(string text) =>
        new(text.Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator ? ' ' : c).ToArray());
}
