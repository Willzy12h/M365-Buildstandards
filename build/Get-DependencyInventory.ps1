# Resolved publish dependencies, not a separately maintained version list. Writes local licence references only.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DepsFile,
    [Parameter(Mandatory)][string]$NoticesDirectory,
    [string]$NuGetRoot = $env:NUGET_PACKAGES
)
$ErrorActionPreference = 'Stop'
if (-not $NuGetRoot) { $NuGetRoot = Join-Path $env:USERPROFILE '.nuget/packages' }
New-Item -ItemType Directory -Force -Path $NoticesDirectory | Out-Null
$deps = Get-Content -LiteralPath $DepsFile -Raw | ConvertFrom-Json
$inventory = @($deps.libraries.PSObject.Properties | Where-Object { $_.Value.type -in @('package', 'runtimepack') } | Sort-Object Name | ForEach-Object {
    $kind = $_.Value.type
    $resolvedIdentity = $_.Name
    $parts = $_.Name -split '/', 2
    if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[A-Za-z0-9_.-]+$' -or $parts[1] -notmatch '^[A-Za-z0-9_.+-]+$') { throw 'Unexpected resolved package identity.' }
    $name = $parts[0] -replace '^runtimepack\.', ''; $version = $parts[1]
    $package = Join-Path $NuGetRoot ($name.ToLowerInvariant() + '/' + $version.ToLowerInvariant())
    $nuspec = Join-Path $package ($name.ToLowerInvariant() + '.nuspec')
    if (-not (Test-Path -LiteralPath $nuspec)) { throw "Resolved package metadata is missing: $name/$version" }
    [xml]$spec = Get-Content -LiteralPath $nuspec -Raw
    $metadata = $spec.SelectSingleNode('/*[local-name()="package"]/*[local-name()="metadata"]')
    $license = $metadata.SelectSingleNode('*[local-name()="license"]')
    $licenseReference = if ($license) { $license.InnerText } else { 'Not declared; maintainer review required' }
    $noticeFiles = @()
    $folder = Join-Path $NoticesDirectory ($name + '-' + $version)
    $sources = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|THIRD.PARTY.NOTICES)(\..*)?$' })
    if ($license -and $license.GetAttribute('type') -eq 'file') {
        $relative = $license.InnerText.Replace('\', '/')
        if ($relative -match '(^/|(^|/)\.\.(/|$)|:)') { throw "Unsafe package licence reference: $name" }
        $declared = Join-Path $package $relative
        if (-not (Test-Path -LiteralPath $declared -PathType Leaf)) { throw "Declared package licence file is missing: $name" }
        $sources += Get-Item -LiteralPath $declared
    }
    foreach ($source in @($sources | Sort-Object FullName -Unique)) {
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $folder -Force
        $noticeFiles += 'licenses/' + $name + '-' + $version + '/' + $source.Name
    }
    [ordered]@{ name = $name; version = $version; kind = $kind; resolvedIdentity = $resolvedIdentity; license = $licenseReference; notices = $noticeFiles }
})
if ($inventory.Count -eq 0) { throw 'Published dependency inventory is empty.' }
[IO.File]::WriteAllText((Join-Path $NoticesDirectory 'README.txt'), "Resolved NuGet and self-contained runtime-pack licence metadata and supplied notice files. This inventory does not constitute a legal approval. The self-contained .NET/Windows Desktop runtime is also identified in VERSION.json. Release owner reviews redistribution obligations before publication.`n", [Text.UTF8Encoding]::new($false))
$inventory
