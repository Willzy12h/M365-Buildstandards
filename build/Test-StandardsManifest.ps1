#Requires -Version 5.1
<#
.SYNOPSIS
  Verifies that standards/manifest.json lists exactly the release files present, with matching SHA-256 digests.
.DESCRIPTION
  A build verifies the committed manifest; it never regenerates it. Regenerating at build time re-blessed whatever
  catalogue bytes happened to be present and left the packaged manifest different from the committed one
  (CLA-20261006-03). An author who deliberately adds or edits a release runs Update-StandardsManifest.ps1, reviews
  the diff and commits it; published releases are additionally pinned by publication digest in the test suite.
  Read-only: writes nothing.
#>
[CmdletBinding()]
param([string]$StandardsDirectory)
$ErrorActionPreference = 'Stop'
if (-not $StandardsDirectory) { $StandardsDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'standards' }
$manifestPath = Join-Path $StandardsDirectory 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Standards manifest not found: $manifestPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.algorithm -ne 'SHA-256' -or $null -eq $manifest.files) { throw 'Standards manifest must declare SHA-256 file digests.' }

$listed = @{}
foreach ($property in $manifest.files.PSObject.Properties) { $listed[$property.Name] = ([string]$property.Value).ToLowerInvariant() }
$present = @(Get-ChildItem -LiteralPath $StandardsDirectory -Filter '*.json' -File | Where-Object { $_.Name -ne 'manifest.json' })

$problems = @()
foreach ($file in $present) {
    if (-not $listed.ContainsKey($file.Name)) { $problems += "$($file.Name) is not listed in the manifest"; continue }
    $actual = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $listed[$file.Name]) { $problems += "$($file.Name) does not match its manifest digest" }
}
foreach ($name in $listed.Keys) {
    if (-not ($present | Where-Object { $_.Name -eq $name })) { $problems += "$name is listed but missing" }
}
if ($problems.Count -gt 0) {
    throw ("Standards manifest verification failed. A published catalogue must not change; a new release needs a reviewed manifest commit.`n" + ($problems -join "`n"))
}
Write-Host ("Standards manifest verified: {0} release files match." -f $present.Count)
