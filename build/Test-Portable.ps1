#Requires -Version 5.1
<#
.SYNOPSIS
  Verify the built ZIP, fresh extraction and actual offline first launch on Windows.
.DESCRIPTION
  Uses a new owned temporary directory with blank connection settings and no tenant evidence.
  Never authenticates or presses a tenant command. Closes only the process it starts.
#>
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ZipPath,
      [Parameter(Mandatory=$true)][string]$StagePath,
      [Parameter(Mandatory=$true)][string]$ResultPath)
$ErrorActionPreference = 'Stop'
$zip = (Resolve-Path -LiteralPath $ZipPath).Path
$stage = (Resolve-Path -LiteralPath $StagePath).Path.TrimEnd('\')
$expectedZip = (Get-Content -LiteralPath "$zip.sha256" -Raw).Split(' ')[0].Trim()
if ($expectedZip -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ine $expectedZip) { throw 'ZIP checksum mismatch.' }
$extract = Join-Path $env:TEMP ('M365-Portable-Check-' + [guid]::NewGuid().ToString('N'))
$process = $null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ($name -match '(^/|^[a-zA-Z]:|(^|/)\.\.(/|$))' -or -not $names.Add($name)) { throw "Unsafe or duplicate ZIP entry: $name" }
        }
    } finally { $archive.Dispose() }
    Expand-Archive -LiteralPath $zip -DestinationPath $extract
    # Windows PowerShell/.NET Framework may expand an existing TEMP 8.3 alias in GetFullPath.
    # Canonicalise the existing root in the same way as every child before testing containment.
    $extract = [IO.Path]::GetFullPath($extract)
    $listed = @{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $extract 'SHA256SUMS.txt')) {
        if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw 'Malformed package checksum line.' }
        $digest = $Matches[1]; $name = $Matches[2]
        $file = [IO.Path]::GetFullPath((Join-Path $extract $name))
        if (-not $file.StartsWith($extract + '\', [StringComparison]::OrdinalIgnoreCase) -or $listed.ContainsKey($name)) { throw "Unsafe checksum path: $name" }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $digest) { throw "Extracted checksum mismatch: $name" }
        if ((Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash -ine $digest) { throw "Stage/extraction mismatch: $name" }
        $listed[$name] = $digest
    }
    $files = @(Get-ChildItem -LiteralPath $extract -File -Recurse)
    if ($files.Count -ne $listed.Count + 1) { throw 'Unlisted files in fresh extraction.' }
    if (@(Get-ChildItem -LiteralPath $stage -File -Recurse).Count -ne $files.Count) { throw 'Stage file count differs from ZIP.' }
    foreach ($folder in 'data','logs','reports') {
        if (@(Get-ChildItem -LiteralPath (Join-Path $extract $folder) -Recurse -File).Count -ne 0) { throw "Private/generated content shipped in $folder." }
    }
    $settings = Get-Content -LiteralPath (Join-Path $extract 'config\toolkit.settings.json') -Raw | ConvertFrom-Json
    if ($settings.assessmentClientId -or $settings.deploymentClientId) { throw 'Connection identifiers are not blank.' }
    $metadata = Get-Content -LiteralPath (Join-Path $extract 'VERSION.json') -Raw | ConvertFrom-Json
    if (-not $metadata.selfContained -or $metadata.sourceCommit -notmatch '^[a-fA-F0-9]{40}$' -or $metadata.dotnetRuntime -notmatch '^10\.') { throw 'Release metadata does not identify a self-contained .NET 10 build.' }
    $exe = Join-Path $extract 'app\BDIT.TenantToolkit.App.exe'
    $process = Start-Process -FilePath $exe -WorkingDirectory $extract -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) { throw "Packaged application exited during startup: $($process.ExitCode)" }
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'Packaged first launch did not show its window.' }
    $startup = Get-Content -LiteralPath (Join-Path $extract 'logs\startup.log') -Raw
    if ($startup -notmatch ('Initialised\. Standard: ' + [regex]::Escape($settings.defaultStandardRelease))) { throw 'Packaged first launch did not load the default standard.' }
    if ($startup -match 'FATAL|CRASH') { throw 'Packaged startup recorded an error.' }
    if (-not $process.CloseMainWindow()) { throw 'Packaged application did not accept graceful close.' }
    if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'Packaged application did not shut down cleanly.' }
    $result = [ordered]@{ sourceCommit=$metadata.sourceCommit; version=$metadata.version; standard=$settings.defaultStandardRelease;
        zipSha256=$expectedZip; extractedFiles=$files.Count; stageBytesMatch=$true; checksumsVerified=$true;
        blankConnectionSettings=$true; emptyEvidenceFolders=$true; actualPackagedFirstLaunch=$true; gracefulShutdown=$true;
        tenantOperationsPerformed=$false; note='Offline Windows first launch only. WAM, physical accessibility and Microsoft service/device acceptance remain unperformed.' }
    $parent = Split-Path -Parent $ResultPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ResultPath), (($result | ConvertTo-Json -Depth 4) + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Host "Verified $($files.Count) extracted files, exact stage bytes and packaged offline startup/shutdown."
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
}
