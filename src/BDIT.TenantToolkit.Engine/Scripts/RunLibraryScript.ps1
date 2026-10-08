<#
Fixed launcher for the Scripts & Reports Run action. Embedded in the engine, plain ASCII, never edited at run time.

It checks the required module is installed (it never installs one), then runs the Copy wrapper the desktop generated for
this item, tenant, account and form - the same verified text Copy produces - and reports only short marker lines on
standard output:

  BDIT:STARTED:          the launcher itself was allowed to run
  BDIT:MODULE_MISSING:   a required module is not installed at the minimum version; nothing was signed in or read
  BDIT:WARNING:<text>    a warning from the wrapper or library body (BDIT:PARTIAL and BDIT:UNKNOWN among them)
  BDIT:INFO:<text>       a short single-line host message (connected tenant and account, rows saved)
  BDIT:FAILED:<text>     the wrapper stopped with an error (another tenant or account, a read that failed)
  BDIT:DONE:             the wrapper finished and disconnected

Rows go only to the CSV file named by -OutputCsv. Long host output (the wrapper's table preview) is dropped, so the
desktop never parses or stores free-form console text. No credentials, policy change, installation or upload.
#>
param(
    [Parameter(Mandatory = $true)][string]$Wrapper,
    [Parameter(Mandatory = $true)][string]$OutputCsv,
    [Parameter(Mandatory = $true)][string]$Modules
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Marker([string]$Kind, [string]$Text) {
    $line = ([string]$Text -replace '[\x00-\x1f\x7f]', ' ').Trim()
    if ($line.Length -gt 400) { $line = $line.Substring(0, 400) }
    [Console]::Out.WriteLine('BDIT:' + $Kind + ':' + $line)
    [Console]::Out.Flush()
}

Write-Marker 'STARTED' ''

foreach ($requirement in @($Modules -split ';' | Where-Object { $_ })) {
    $parts = $requirement -split ':'
    $name = $parts[0]
    $minimum = [version]$parts[1]
    $found = @(Get-Module -ListAvailable -Name $name | Where-Object { $_.Version -ge $minimum })
    if ($found.Count -eq 0) {
        Write-Marker 'MODULE_MISSING' ($name + ' ' + $parts[1] + ' or later')
        exit 3
    }
}

try {
    & $Wrapper -OutputCsv $OutputCsv 3>&1 6>&1 | ForEach-Object {
        if ($_ -is [System.Management.Automation.WarningRecord]) { Write-Marker 'WARNING' $_.Message }
        elseif ($_ -is [System.Management.Automation.InformationRecord]) {
            $message = [string]$_.MessageData
            if ($message.Length -le 400 -and -not $message.Contains("`n")) { Write-Marker 'INFO' $message }
        }
    }
}
catch {
    Write-Marker 'FAILED' $_.Exception.Message
    exit 2
}
Write-Marker 'DONE' ''
exit 0
