#Requires -Version 5.1
<#
.SYNOPSIS
  Restores, builds, tests, publishes and packages the M365 BuildStandard Tool as a portable ZIP.
.DESCRIPTION
  Steps: dotnet restore -> dotnet build -c Release -> dotnet test -> verify committed standards manifest ->
  dotnet publish (self-contained win-x64, framework-dependent runtime NOT required on the engineer's PC) ->
  stage standards, config, the operator documents and launchers -> write VERSION.json and SHA256SUMS.txt -> zip.
  The package carries one application executable on purpose: a second would widen the application-control exception
  a client has to allow. The offline CLI (bdit) ships as the App's managed dependency and runs through that same
  executable via bdit.cmd (INT-074/INT-079); the runtime's createdump.exe diagnostic is the only other .exe.
  Requires the .NET 10 SDK on the build machine only. Engineers never need the SDK.
.PARAMETER SkipTests
  Skip the test step (not recommended for a release).
.PARAMETER Runtime
  Runtime identifier for the publish step. Default win-x64.
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE" }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'The .NET SDK (dotnet) is not on PATH. Install the .NET 10 SDK on the build machine: https://dotnet.microsoft.com/download/dotnet/10.0' }
$sdkVersion = & dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^10\.0\.\d+$') { throw 'The SDK selected by global.json must be a stable .NET 10 SDK. Run BUILD-ME-FIRST.cmd or install the pinned SDK.' }

[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw 'Version not found in Directory.Build.props' }
$sourceTreeDirty = $null
if ((Test-Path -LiteralPath (Join-Path $root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
    $sourceTreeDirty = @(& git status --porcelain).Count -gt 0
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source-tree cleanliness.' }
}

$dist = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $root 'dist' }
$stageName = "M365-BuildStandard-Tool-$version-$Runtime"
$stage = Join-Path $dist $stageName
$zip = Join-Path $dist "$stageName.zip"
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Invoke-Step 'Restore' { & dotnet restore BDIT.TenantToolkit.sln }
Invoke-Step 'Build' { & dotnet build BDIT.TenantToolkit.sln -c $Configuration --no-restore -warnaserror }
if (-not $SkipTests) {
    Invoke-Step 'Test' { & dotnet test tests\BDIT.TenantToolkit.Tests\BDIT.TenantToolkit.Tests.csproj -c $Configuration --no-build --nologo --logger 'trx;LogFileName=test-results.trx' --results-directory (Join-Path $dist 'test-results') }
    Invoke-Step 'Test the application' { & dotnet test tests\BDIT.TenantToolkit.App.Tests\BDIT.TenantToolkit.App.Tests.csproj -c $Configuration --no-build --nologo --logger 'trx;LogFileName=app-test-results.trx' --results-directory (Join-Path $dist 'test-results') }
}
# Verify, never regenerate: the package ships the committed manifest bytes, and a changed catalogue fails the build.
Invoke-Step 'Standards manifest' { & (Join-Path $PSScriptRoot 'Test-StandardsManifest.ps1') -StandardsDirectory (Join-Path $root 'standards') }
Invoke-Step 'Publish application (self-contained)' {
    & dotnet publish src\BDIT.TenantToolkit.App\BDIT.TenantToolkit.App.csproj -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none -p:DebugSymbols=false -o (Join-Path $stage 'app')
}

Invoke-Step 'Stage package contents' {
    foreach ($dir in 'standards', 'config') {
        Copy-Item -LiteralPath (Join-Path $root $dir) -Destination (Join-Path $stage $dir) -Recurse -Force
    }

    # The documents an engineer needs to run the tool, named one by one. The rest of docs/ is internal: preserved
    # source history, agent coordination, review briefs and decision logs. Copying the folder shipped all of it to
    # clients, and would ship every internal document written afterwards too, so this is an allow-list.
    $documents = @(
        'APPLICATION-SETUP.md', 'AUTOMATION-COVERAGE.md', 'BUILD-STANDARD-SUMMARY.md', 'DEVICE-AUTOMATION.md',
        'EQUIVALENCE-SIGNALS.md', 'LICENSING.md', 'LIVE-VALIDATION.md', 'POLICY-AUTOMATION-CODE.md',
        'RECOVERY.md', 'TESTING-THIS-BUILD.md', 'UNRESOLVED-WRITES.md', 'ENGINEER-DOCUMENTS.md', 'EXCHANGE-PURVIEW.md', 'NATIVE-SETTINGS-AND-MOBILE.md', 'CONTROLLED-ACCEPTANCE.md',
        'LIVE-ACCEPTANCE-RUNSHEET.md', 'OPERATOR-START.md', 'WORKSPACE-CONTINUITY.md', 'INTERNAL-OPERATING-MODEL.md', 'RELEASE-AND-SERVICING.md'
    )
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'docs') | Out-Null
    foreach ($document in $documents) {
        $source = Join-Path $root (Join-Path 'docs' $document)
        # A renamed or deleted document fails the build rather than disappearing from the package unnoticed.
        if (-not (Test-Path -LiteralPath $source)) { throw "Packaged document not found: docs\$document. Update the list in build\Build-Portable.ps1." }
        Copy-Item -LiteralPath $source -Destination (Join-Path $stage 'docs') -Force
    }

    foreach ($dir in 'data', 'logs', 'reports') { New-Item -ItemType Directory -Force -Path (Join-Path $stage $dir) | Out-Null }
    Get-ChildItem -LiteralPath (Join-Path $root 'packaging') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Force }
    # packaging/README.md is the engineer's README and is copied by the loop above. The repository README is for
    # contributors — it describes branches, agents and source repositories — so it is deliberately not shipped.
    Copy-Item -LiteralPath (Join-Path $root 'CHANGELOG.md') -Destination $stage -Force

    $runtimeVersion = (Get-ChildItem -LiteralPath (Join-Path $stage 'app') -Filter 'System.Private.CoreLib.dll' -Recurse | Select-Object -First 1).VersionInfo.ProductVersion
    $dependencies = @(& (Join-Path $PSScriptRoot 'Get-DependencyInventory.ps1') -DepsFile (Join-Path $stage 'app/BDIT.TenantToolkit.App.deps.json') -NoticesDirectory (Join-Path $stage 'licenses'))
    [IO.File]::WriteAllText((Join-Path $stage 'DEPENDENCIES.json'), (($dependencies | ConvertTo-Json -Depth 5) + "`n"), [Text.UTF8Encoding]::new($false))
    $sourceCommit = 'unavailable (source archive)'
    if ((Test-Path -LiteralPath (Join-Path $root '.git')) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        $sourceCommit = & git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the Git source commit.' }
    }
    $manifest = [ordered]@{
        product        = 'M365 BuildStandard Tool'
        version        = $version
        runtime        = $Runtime
        selfContained  = $true
        dotnetRuntime  = $runtimeVersion
        dotnetSdk      = (& dotnet --version)
        sourceCommit   = $sourceCommit
        sourceTreeDirty = $sourceTreeDirty
        defaultStandard = (Get-Content -LiteralPath (Join-Path $stage 'config\toolkit.settings.json') -Raw | ConvertFrom-Json).defaultStandardRelease
        builtAt        = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        builtOn        = $env:COMPUTERNAME
        standards      = (Get-ChildItem -LiteralPath (Join-Path $stage 'standards') -Filter '*.json' | Where-Object { $_.Name -ne 'manifest.json' } | ForEach-Object { $_.Name })
        documents      = $documents
        nugetPackages  = @($dependencies | Where-Object { $_.kind -eq 'package' })
        runtimePacks   = @($dependencies | Where-Object { $_.kind -eq 'runtimepack' })
        acceptance     = 'Preview; synthetic validation only. No live service/device capability accepted. See docs/CONTROLLED-ACCEPTANCE.md and docs/RELEASE-AND-SERVICING.md.'
        note           = 'Preview tool, unsigned. Checksums detect modification only against a trusted reference; they do not establish publisher identity. Any application-control exception and internal distribution need organisational approval.'
    }
    [IO.File]::WriteAllText((Join-Path $stage 'VERSION.json'), (($manifest | ConvertTo-Json -Depth 5) + "`n"), [Text.UTF8Encoding]::new($false))
}

Invoke-Step 'Documentation links resolve inside the package' {
    # The package ships a subset of docs/, so a link that resolves in the repository can still be dead in the
    # engineer's copy. That is how RECOVERY.md came to point at an unresolved-write procedure the package did not
    # contain. Check every local Markdown link against the staged tree and fail the build rather than ship it.
    $broken = @()
    Get-ChildItem -LiteralPath $stage -Recurse -File -Filter '*.md' | ForEach-Object {
        $document = $_
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $document.FullName -Raw), '\[[^\]]*\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value.Split('#')[0].Trim()
            if (-not $target -or $target -match '^(https?:|mailto:)') { continue }
            $resolved = Join-Path $document.DirectoryName $target
            if (-not (Test-Path -LiteralPath $resolved)) {
                $broken += '{0} -> {1}' -f $document.FullName.Substring($stage.Length + 1).Replace('\', '/'), $target
            }
        }
    }
    if ($broken) {
        $broken | ForEach-Object { Write-Host "  broken: $_" -ForegroundColor Red }
        throw "$($broken.Count) documentation link(s) do not resolve inside the package. Ship the target, make the guidance self-contained, or use an absolute repository URL."
    }
    Write-Host 'All packaged documentation links resolve.'
}

Invoke-Step 'Checksums' {
    $lines = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object FullName | ForEach-Object {
        $relative = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
    [IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'), (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Host ("{0} files listed" -f $lines.Count)
}

Invoke-Step 'Zip' {
    # ZIP entry names use '/' as the ZIP specification requires (APPNOTE 4.4.17). Windows PowerShell's Compress-Archive
    # wrote '\' separators, which non-Windows verification tools misread (CLA-20261006-14). Entries are added in ordinal
    # path order; empty folders are omitted as before, and the application creates them at first launch.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $files = @{}
        foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) { $files[$file.FullName.Substring($stage.Length + 1).Replace('\', '/')] = $file.FullName }
        $names = [string[]]@($files.Keys)
        [Array]::Sort($names, [StringComparer]::Ordinal)
        foreach ($name in $names) {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $files[$name], $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$zipHash  $stageName.zip`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Package: $zip"
    Write-Host "SHA-256: $zipHash"
}

Write-Host ""
Write-Host "Portable release ready: $stage" -ForegroundColor Green
