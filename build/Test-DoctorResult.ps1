#Requires -Version 5.1
<#
.SYNOPSIS
  Assert actual packaged doctor readiness, then optionally exercise synthetic refusal controls.
.DESCRIPTION
  Receives stdout from the owned packaged CLI. Never replaces or fabricates its diagnostics.
  This certifies local package checks only; tenant readiness and write authority remain external.
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Json,
      [Parameter(Mandatory=$true)][string]$ExpectedToolkitVersion,
      [switch]$TestRejections)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$requiredIds = @('root.access','platform','package.layout','package.metadata','package.integrity','settings','standard',
    'writable.config','cleanup.config','writable.data','cleanup.data','writable.logs','cleanup.logs','writable.reports','cleanup.reports')
function Assert-DoctorResult($diagnostic) {
    if ($null -eq $diagnostic -or $diagnostic.schemaVersion -notin @(1) -or
        ($diagnostic.schemaVersion -isnot [int] -and $diagnostic.schemaVersion -isnot [long]) -or
        $diagnostic.kind -cne 'offlinePrerequisites' -or $diagnostic.status -cne 'Ready' -or
        $diagnostic.toolkitVersion -cne $ExpectedToolkitVersion) { throw 'Packaged doctor schema, kind, status or toolkit version differs from the ready contract.' }
    $findings = @($diagnostic.findings)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($finding in $findings) {
        if ([string]::IsNullOrWhiteSpace($finding.id) -or -not $seen.Add($finding.id) -or
            $finding.applicability -cnotin @('Required','Optional') -or
            $finding.disposition -cnotin @('Pass','Fail','Unknown','NotApplicable')) { throw 'Packaged doctor findings are invalid or duplicated.' }
        if ($finding.applicability -ceq 'Required' -and $finding.disposition -cne 'Pass') { throw 'Packaged doctor did not pass every required check.' }
    }
    foreach ($id in $requiredIds) {
        $matchingFindings = @($findings | Where-Object { $_.id -ceq $id })
        if ($matchingFindings.Count -ne 1 -or $matchingFindings[0].applicability -cne 'Required' -or $matchingFindings[0].disposition -cne 'Pass') {
            throw 'Packaged doctor omitted or weakened a mandatory required check.'
        }
    }
}
$actual = $Json | ConvertFrom-Json
Assert-DoctorResult $actual
if ($TestRejections) {
    # Mutations of already-admitted real output test the assertion only. None replaces the actual smoke result.
    foreach ($scenario in @('schema','kind','status','version','empty','missing','optional','fail','unknown','not-applicable','duplicate','new-required')) {
        $modified = $Json | ConvertFrom-Json
        switch ($scenario) {
            'schema' { $modified.schemaVersion = 2 }
            'kind' { $modified.kind = 'tenantSnapshot' }
            'status' { $modified.status = 'NeedsAttention' }
            'version' { $modified.toolkitVersion = 'synthetic-other-version' }
            'empty' { $modified.findings = @() }
            'missing' { $modified.findings = @($modified.findings | Where-Object { $_.id -cne 'package.integrity' }) }
            'optional' { @($modified.findings | Where-Object { $_.id -ceq 'package.integrity' })[0].applicability = 'Optional' }
            'fail' { @($modified.findings | Where-Object { $_.id -ceq 'package.integrity' })[0].disposition = 'Fail' }
            'unknown' { @($modified.findings | Where-Object { $_.id -ceq 'package.integrity' })[0].disposition = 'Unknown' }
            'not-applicable' { @($modified.findings | Where-Object { $_.id -ceq 'package.integrity' })[0].disposition = 'NotApplicable' }
            'duplicate' { $modified.findings = @($modified.findings) + @($modified.findings)[0] }
            'new-required' { $modified.findings = @($modified.findings) + [pscustomobject]@{ id='synthetic.new-required'; applicability='Required'; disposition='Unknown' } }
        }
        $refused = $false
        try { Assert-DoctorResult $modified } catch { $refused = $true }
        if (-not $refused) { throw "Doctor result verification did not refuse synthetic $scenario." }
    }
}
