#Requires -Version 5.1
<#
.SYNOPSIS
  One-click first build for a machine with no .NET SDK. Needs no administrator rights.
.DESCRIPTION
  1. Uses a compatible .NET 10 SDK if one is on PATH; otherwise downloads the official SDK archive, verifies
     Microsoft's SHA-512 and installs into <repo>\.dotnet (user-local, no machine-wide PATH change).
  2. Runs build\Build-Portable.ps1 (restore, build, test, manifest, publish, package).
  3. Reports where the portable folder and Start.cmd are.
  Internet access to builds.dotnet.microsoft.com and nuget.org is required for the first run only.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
# Everything shown on screen is also written to build\last-build.log so the output can be read back without copying it.
$logFile = Join-Path $PSScriptRoot 'last-build.log'
try { Start-Transcript -Path $logFile -Force | Out-Null } catch { }
Write-Host "M365 BuildStandard Tool - first build in $root" -ForegroundColor Cyan
Write-Host "Full output is being written to $logFile"

$sdkPin = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).sdk.version
function Test-Sdk10([string]$exe) {
    if (-not (Test-Path -LiteralPath $exe)) { return $false }
    try {
        $sdks = & $exe --list-sdks 2>$null
        if ($LASTEXITCODE -ne 0) { return $false }
        foreach ($line in $sdks) {
            if ($line -match '^(10\.0\.\d+)\s' -and [version]$Matches[1] -ge [version]$sdkPin) { return $true }
        }
        return $false
    } catch { return $false }
}

$localSdk = Join-Path $root '.dotnet'
$localExe = Join-Path $localSdk 'dotnet.exe'
$useLocal = $false

$onPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($onPath -and (Test-Sdk10 $onPath.Source)) {
    Write-Host "Using .NET 10 SDK already installed at $($onPath.Source)"
} elseif (Test-Sdk10 $localExe) {
    Write-Host "Using user-local .NET 10 SDK at $localExe"
    $useLocal = $true
} else {
    Write-Host "No compatible .NET 10 SDK found. Installing a verified user-local copy into $localSdk..." -ForegroundColor Yellow
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $sdk = @($metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $sdkPin }) | Select-Object -First 1
    if (-not $sdk) { throw "The official release metadata does not contain pinned SDK $sdkPin." }
    $download = @($sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' }) | Select-Object -First 1
    if (-not $download -or $download.url -notmatch '^https://builds\.dotnet\.microsoft\.com/' -or $download.hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'The official Windows SDK archive or SHA-512 is unavailable.' }
    $archivePath = Join-Path $env:TEMP ('M365-BuildStandard-SDK-' + [guid]::NewGuid().ToString('N') + '.zip')
    try {
        Invoke-WebRequest -Uri $download.url -OutFile $archivePath -UseBasicParsing
        if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ine $download.hash) { throw 'The SDK archive failed Microsoft SHA-512 verification; installation refused.' }
        New-Item -ItemType Directory -Path $localSdk -Force | Out-Null
        Expand-Archive -LiteralPath $archivePath -DestinationPath $localSdk -Force
    } finally {
        if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
    }
    if (-not (Test-Sdk10 $localExe)) { throw "The .NET 10 SDK install did not complete. Check internet access and re-run, or install the SDK from https://dotnet.microsoft.com/download/dotnet/10.0" }
    $useLocal = $true
}

if ($useLocal) {
    $env:PATH = "$localSdk;$env:PATH"
    $env:DOTNET_ROOT = $localSdk
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$buildArgs = @()
if ($SkipTests) { $buildArgs += '-SkipTests' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-Portable.ps1') @buildArgs 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) {
    try { Stop-Transcript | Out-Null } catch { }
    throw "Build-Portable.ps1 failed with exit code $LASTEXITCODE. Scroll up for the first error; compile errors are listed as 'error CS....'. The full log is in $logFile."
}

[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
# Must match $stageName in Build-Portable.ps1, or the paths printed below point at a folder that does not exist.
$stage = Join-Path $root "dist\M365-BuildStandard-Tool-$version-win-x64"
Write-Host ""
Write-Host "Ready. Portable folder: $stage" -ForegroundColor Green
Write-Host "Run:   $stage\Start.cmd"
Write-Host "ZIP:   $stage.zip  (copy this to other engineers)"
try { Stop-Transcript | Out-Null } catch { }
