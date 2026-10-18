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
        $directory = Join-Path $extract $folder
        # Compress-Archive omits empty directories. Absence is empty, and the application must create them at launch.
        if (Test-Path -LiteralPath $directory) {
            if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Expected an evidence directory: $folder" }
            if (@(Get-ChildItem -LiteralPath $directory -Recurse -File -Force).Count -ne 0) { throw "Private/generated content shipped in $folder." }
        }
    }
    $settings = Get-Content -LiteralPath (Join-Path $extract 'config\toolkit.settings.json') -Raw | ConvertFrom-Json
    if ($settings.assessmentClientId -or $settings.deploymentClientId) { throw 'Connection identifiers are not blank.' }
    $metadata = Get-Content -LiteralPath (Join-Path $extract 'VERSION.json') -Raw | ConvertFrom-Json
    & (Join-Path $PSScriptRoot 'Test-DependencyInventory.ps1') -PackageRoot $extract -TestRejections
    if (-not $metadata.selfContained -or $metadata.sourceCommit -notmatch '^[a-fA-F0-9]{40}$' -or $metadata.dotnetRuntime -notmatch '^10\.') { throw 'Release metadata does not identify a self-contained .NET 10 build.' }
    $accessibility = Join-Path $extract 'app\Accessibility.dll'
    if (-not (Test-Path -LiteralPath $accessibility -PathType Leaf)) { throw 'Portable package is missing Accessibility.dll, required by text-box context menus.' }
    $accessibilityIdentity = [Reflection.AssemblyName]::GetAssemblyName($accessibility)
    if ($accessibilityIdentity.Name -ne 'Accessibility' -or $accessibilityIdentity.Version.ToString() -ne '4.0.0.0') { throw 'Unexpected Accessibility assembly identity.' }
    $deps = Get-Content -LiteralPath (Join-Path $extract 'app\BuildStandard.App.deps.json') -Raw
    if (-not $deps.Contains('"Accessibility.dll"')) { throw 'Accessibility.dll is missing from the runtime dependency manifest.' }
    $exe = Join-Path $extract 'app\BuildStandard.App.exe'
    # Exercise the same self-contained executable in offline CLI mode before desktop startup.
    $executables = @(Get-ChildItem -LiteralPath $extract -Filter '*.exe' -File -Recurse | ForEach-Object { [IO.Path]::GetFullPath($_.FullName) })
    # Preserve the SDK's existing runtime crash diagnostic beside the runtime; it is not a second toolkit application
    # host. Only these exact package paths are allowed: the same file name anywhere else is refused.
    $allowedExecutables = @([IO.Path]::GetFullPath($exe), [IO.Path]::GetFullPath((Join-Path $extract 'app\createdump.exe')))
    if (@($executables | Where-Object { $_ -ieq $allowedExecutables[0] }).Count -ne 1 -or
        @($executables | Where-Object { $allowedExecutables -notcontains $_ }).Count -gt 0) { throw 'Unexpected second application executable in the portable toolkit.' }
    if (-not (Test-Path -LiteralPath (Join-Path $extract 'app\BuildStandard.Cli.dll')) -or -not $deps.Contains('"BuildStandard.Cli/')) { throw 'Portable CLI managed dependency is missing.' }
    $launcher = Join-Path $extract 'buildstandard.cmd'
    $legacyLauncher = Join-Path $extract 'bdit.cmd'
    if (-not (Test-Path -LiteralPath $legacyLauncher)) { throw 'Legacy CLI compatibility launcher is missing.' }
    if (-not (Test-Path -LiteralPath $launcher)) { throw 'Portable CLI launcher is missing.' }
    function Invoke-OwnedCli([string]$FileName, [string]$Arguments, [int]$ExpectedExit) {
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $FileName; $info.Arguments = $Arguments; $info.WorkingDirectory = $extract
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        # The owned child must diagnose this fresh extraction, even if the CI caller set a toolkit root.
        $info.EnvironmentVariables.Remove('BDIT_TOOLKIT_ROOT')
        $info.EnvironmentVariables.Remove('BUILDSTANDARD_ROOT')
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        $owned = [Diagnostics.Process]::new(); $owned.StartInfo = $info
        try {
            if (-not $owned.Start()) { throw 'Packaged CLI did not start.' }
            $outTask = $owned.StandardOutput.ReadToEndAsync(); $errTask = $owned.StandardError.ReadToEndAsync()
            if (-not $owned.WaitForExit(30000)) { $owned.Kill(); $owned.WaitForExit(5000) | Out-Null; throw 'Packaged CLI exceeded its offline test budget.' }
            $text = $outTask.GetAwaiter().GetResult(); $errorText = $errTask.GetAwaiter().GetResult()
            if ($owned.ExitCode -ne $ExpectedExit) { throw "Packaged CLI exit $($owned.ExitCode), expected $ExpectedExit. $errorText" }
            return [pscustomobject]@{ Text=$text; Error=$errorText }
        } finally { if (-not $owned.HasExited) { $owned.Kill() }; $owned.Dispose() }
    }
    $help = Invoke-OwnedCli $exe '--cli --help' 0
    if ($help.Text -notmatch 'buildstandard inventory' -or $help.Error) { throw 'CLI help did not preserve redirected stdout/stderr.' }
    $failure = Invoke-OwnedCli $exe '--cli inventory --snapshot "synthetic missing file.json"' 2
    if ($failure.Error -notmatch 'Refused:' -or $failure.Text) { throw 'CLI refusal did not preserve stderr or exit code.' }
    $unknown = Invoke-OwnedCli $exe '--cli synthetic-unknown-command' 64
    if ($unknown.Error -notmatch 'Unknown command') { throw 'CLI unknown-command status was lost.' }
    $viaLauncher = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" --help"') 0
    if ($viaLauncher.Text -notmatch 'buildstandard inventory' -or $viaLauncher.Error) { throw 'buildstandard.cmd did not forward help and stdout.' }
    $viaLegacy = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $legacyLauncher + '" --help"') 0
    if ($viaLegacy.Text -ne $viaLauncher.Text -or $viaLegacy.Error) { throw 'The legacy CLI alias did not reach the same dispatcher/output.' }
    $legacyFailure = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $legacyLauncher + '" inventory --snapshot "synthetic missing file.json""') 2
    if ($legacyFailure.Error -notmatch 'Refused:' -or $legacyFailure.Text) { throw 'The legacy CLI alias lost the refusal/exit code.' }
    # Real diagnostics from the fresh extracted host, before any synthetic evidence or native startup.
    $doctor = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" doctor --format json"') 0
    if ($doctor.Error) { throw 'Packaged doctor wrote unexpected standard error.' }
    & (Join-Path $PSScriptRoot 'Test-DoctorResult.ps1') -Json $doctor.Text -ExpectedToolkitVersion $metadata.version -TestRejections
    $launcherFailure = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" inventory --snapshot "synthetic missing file.json""') 2
    if ($launcherFailure.Error -notmatch 'Refused:') { throw 'buildstandard.cmd lost the refusal or real exit code.' }
    # Mixed handles: stdout is a pipe owned by this test, stderr is NOT redirected, so the child inherits this
    # script's own standard error. This is the "| findstr" shape, where only stdout is redirected. It proves the
    # host keeps a piped stdout and the exit code when stderr is whatever the caller provides. CI hosts usually give
    # this script a pipe; the observed handle type is recorded. It does not prove interactive-console display: an
    # engineer's console run stays a manual Windows gate.
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PortableCliStandardHandles {
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern uint GetFileType(IntPtr handle);
}
'@
    $callerError = [PortableCliStandardHandles]::GetStdHandle(-12)
    $callerErrorValid = $callerError -ne [IntPtr]::Zero -and $callerError -ne [IntPtr]::new(-1)
    # FILE_TYPE_DISK 1, FILE_TYPE_CHAR 2 (console), FILE_TYPE_PIPE 3.
    $inheritedErrorType = if (-not $callerErrorValid) { 'unavailable' } else {
        switch ([PortableCliStandardHandles]::GetFileType($callerError)) { 1 { 'file' } 2 { 'console' } 3 { 'pipe' } default { 'unknown' } } }
    function Invoke-InheritedErrorCli([string]$Arguments, [int]$ExpectedExit) {
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $exe; $info.Arguments = $Arguments; $info.WorkingDirectory = $extract
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $false
        $owned = [Diagnostics.Process]::new(); $owned.StartInfo = $info
        try {
            if (-not $owned.Start()) { throw 'Packaged CLI did not start.' }
            $outTask = $owned.StandardOutput.ReadToEndAsync()
            if (-not $owned.WaitForExit(30000)) { $owned.Kill(); $owned.WaitForExit(5000) | Out-Null; throw 'Packaged CLI exceeded its offline test budget.' }
            $text = $outTask.GetAwaiter().GetResult()
            if ($owned.ExitCode -ne $ExpectedExit) { throw "Packaged CLI with inherited stderr ($inheritedErrorType) exit $($owned.ExitCode), expected $ExpectedExit." }
            return $text
        } finally { if (-not $owned.HasExited) { $owned.Kill() }; $owned.Dispose() }
    }
    $mixedHelp = Invoke-InheritedErrorCli '--cli --help' 0
    if ($mixedHelp -notmatch 'buildstandard inventory') { throw "CLI help was not captured on piped stdout with inherited stderr ($inheritedErrorType)." }
    Write-Host "Expected synthetic refusal follows on the inherited standard error ($inheritedErrorType):"
    $mixedFailure = Invoke-InheritedErrorCli '--cli inventory --snapshot "synthetic missing file.json"' 2
    # A usable inherited stderr receives the refusal, so stdout stays clean. With no stderr at all, the host routes
    # errors to stdout rather than losing them (CLA-20261008-15).
    if ($callerErrorValid) {
        if ($mixedFailure) { throw "CLI refusal leaked onto piped stdout while stderr ($inheritedErrorType) was usable." }
    } elseif ($mixedFailure -notmatch 'Refused:') { throw 'CLI refusal was lost when no standard error was available.' }
    $synthetic = Join-Path $extract 'synthetic CLI evidence'
    New-Item -ItemType Directory -Path $synthetic | Out-Null
    # Exercise the CSV parser from the extracted self-contained host, before any profile is created.
    $customerFile = Join-Path $synthetic 'synthetic customers.csv'
    [IO.File]::WriteAllText($customerFile, "Microsoft ID,Company name`n11111111-1111-4111-8111-111111111111,`"Synthetic, client`"`n", [Text.UTF8Encoding]::new($false))
    $customers = Invoke-OwnedCli $exe ('--cli customers --csv "' + $customerFile + '"') 0
    $customerPreview = $customers.Text | ConvertFrom-Json
    if ($customers.Error -or $customerPreview.kind -ne 'customerImportPreview' -or $customerPreview.saved -ne $false -or
        @($customerPreview.rows).Count -ne 1 -or $customerPreview.rows[0].state -ne 'Ready' -or
        $customerPreview.rows[0].company -ne 'Synthetic, client' -or (Test-Path -LiteralPath (Join-Path $extract 'data\profiles.json'))) {
        throw 'Packaged customer preview did not preserve quoted input and remain unsaved.'
    }
    [IO.File]::WriteAllText($customerFile, "Microsoft ID,Company name`n11111111-1111-4111-8111-111111111111,broken`"quote`n", [Text.UTF8Encoding]::new($false))
    $invalidCustomers = Invoke-OwnedCli $exe ('--cli customers --csv "' + $customerFile + '"') 2
    if ($invalidCustomers.Text -or $invalidCustomers.Error -notmatch 'Refused:') { throw 'Packaged customer preview accepted malformed quoting or lost its refusal.' }
    # Separate domain envelope: original JSON, historical provenance, HTML/CSV; no DNS command exists in CLI.
    $domainFile = Join-Path $synthetic 'synthetic domain report.json'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-fixtures\domain-readiness.json') -Destination $domainFile
    $domainOutput = Join-Path $synthetic 'domain exports'
    $domainRead = Invoke-OwnedCli $exe ('--cli domain-report --input "' + $domainFile + '" --output "' + $domainOutput + '"') 0
    if ($domainRead.Error -or $domainRead.Text -notmatch 'Historical' -or $domainRead.Text -notmatch 'Partial' -or $domainRead.Text -notmatch 'no DNS queries') {
        throw 'Packaged domain report lost historical/partial/read-only meaning.'
    }
    $domainPacks = @(Get-ChildItem -LiteralPath $domainOutput -Directory)
    if ($domainPacks.Count -ne 1) { throw 'Packaged domain report did not publish exactly one new pack.' }
    $domainPack = $domainPacks[0].FullName
    if ((Get-FileHash -LiteralPath $domainFile -Algorithm SHA256).Hash -cne (Get-FileHash -LiteralPath (Join-Path $domainPack 'report.json') -Algorithm SHA256).Hash -or
        -not (Test-Path -LiteralPath (Join-Path $domainPack 'observations.csv.zip')) -or
        (Get-Content -LiteralPath (Join-Path $domainPack 'readiness.html') -Raw) -notmatch 'DNS-owner worksheet') {
        throw 'Packaged domain report did not preserve original JSON or generate the shared worksheet.'
    }
    $domainRefusal = Invoke-OwnedCli $exe ('--cli domain-report --input "' + $domainFile + '" --output "' + $domainOutput + '" --run') 2
    if ($domainRefusal.Text -or $domainRefusal.Error -notmatch '--run' -or @(Get-ChildItem -LiteralPath $domainOutput -Directory).Count -ne 1) {
        throw 'Packaged domain CLI did not refuse a collection option before exporting.'
    }
    # Remove the exact test-owned pack files, then their now-empty folders. Never recurse over unknown evidence.
    $domainManifest = Get-Content -LiteralPath (Join-Path $domainPack 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($name in 'report.json','readiness.html','observations.csv.zip') {
        $actual = (Get-FileHash -LiteralPath (Join-Path $domainPack $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $domainManifest.files.$name) { throw "Domain pack fingerprint mismatch: $name" }
        Remove-Item -LiteralPath (Join-Path $domainPack $name) -Force
    }
    Remove-Item -LiteralPath (Join-Path $domainPack 'manifest.json') -Force
    Remove-Item -LiteralPath $domainPack -Force
    Remove-Item -LiteralPath $domainOutput -Force
    Remove-Item -LiteralPath $domainFile -Force
    $outFile = Join-Path $synthetic 'redirected help.txt'; $errFile = Join-Path $synthetic 'redirected errors.txt'
    $fileHelp = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" --help > "' + $outFile + '" 2> "' + $errFile + '""') 0
    if ($fileHelp.Text -or $fileHelp.Error -or (Get-Content -LiteralPath $outFile -Raw) -notmatch 'buildstandard inventory' -or (Get-Item -LiteralPath $errFile).Length -ne 0) { throw 'CLI file redirection did not preserve output handles.' }
    Remove-Item -LiteralPath $outFile, $errFile -Force
    $snapshotFile = Join-Path $synthetic 'synthetic configuration.json'
    $snapshot = [ordered]@{ id='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; tenantId='11111111-1111-4111-8111-111111111111';
        tenantName='Synthetic portable tenant'; primaryDomain='synthetic.example.invalid'; clientLabel='Synthetic CLI';
        capturedAt='2026-10-08T00:00:00Z'; capturedBy='engineer@example.invalid'; sessionMode='Assessment'; standardRelease=$settings.defaultStandardRelease;
        toolkitVersion=$metadata.version; identitySource='Synthetic offline test'; collections=@{}; complete=$false; integrityDigest='' }
    [IO.File]::WriteAllText($snapshotFile, ($snapshot | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $inventory = Invoke-OwnedCli $exe ('--cli inventory --snapshot "' + $snapshotFile + '" --format html') 0
    # The printed path must be the one generated file, and that file must carry the synthetic tenant's identity.
    function Assert-SyntheticReport([string]$Printed, [string]$Filter, [string]$Label) {
        $generated = @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -Filter $Filter -File)
        if ($generated.Count -ne 1) { throw "Packaged CLI generated $($generated.Count) $Label file(s), expected 1." }
        $expectedPath = [IO.Path]::GetFullPath($generated[0].FullName)
        if (-not $Printed -or [IO.Path]::GetFullPath($Printed.Trim()) -ine $expectedPath) { throw "Packaged CLI printed '$Printed' for the $Label, but generated '$expectedPath'." }
        $content = Get-Content -LiteralPath $expectedPath -Raw
        if (-not $content.Contains('Synthetic portable tenant') -or -not $content.Contains('11111111-1111-4111-8111-111111111111')) { throw "Packaged CLI $Label does not identify the synthetic tenant by name and ID." }
    }
    if ($inventory.Error) { throw "Packaged CLI inventory wrote to stderr: $($inventory.Error)" }
    Assert-SyntheticReport (@($inventory.Text -split "`r?`n")[0]) 'configuration-*.html' 'configuration inventory'
    $profileFile = Join-Path $extract 'data\profiles.json'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $profileFile) | Out-Null
    [IO.File]::WriteAllText($profileFile, '[{"id":"88888888-8888-4888-8888-888888888888","tenantId":"11111111-1111-4111-8111-111111111111","company":"Synthetic CLI client","domain":"synthetic.example.invalid","parameters":{}}]', [Text.UTF8Encoding]::new($false))
    $jobs = Invoke-OwnedCli $exe '--cli jobs --tenant 11111111-1111-4111-8111-111111111111' 0
    if ($jobs.Text -notmatch 'Synthetic CLI client.*0 job\(s\)' -or $jobs.Error) { throw "Packaged CLI did not project stored synthetic client jobs cleanly. $($jobs.Error)" }
    $clients = Invoke-OwnedCli $exe '--cli clients' 0
    $clientRows = $clients.Text | ConvertFrom-Json
    if ($clients.Error -or $clientRows.kind -ne 'clientReviews' -or @($clientRows.clients).Count -ne 1 -or $clientRows.clients[0].company -ne 'Synthetic CLI client') { throw 'Packaged client history command did not return the saved synthetic client.' }
    $review = Invoke-OwnedCli $exe ('--cli review --tenant 11111111-1111-4111-8111-111111111111 --output "' + $synthetic + '"') 0
    $reviewPack = $review.Text | ConvertFrom-Json
    if ($review.Error -or $reviewPack.kind -ne 'clientReviewPack') { throw 'Packaged review command failed.' }
    $reviewDirectory = [IO.Path]::GetFullPath($reviewPack.directory)
    if ([IO.Path]::GetDirectoryName($reviewDirectory) -ine [IO.Path]::GetFullPath($synthetic)) { throw 'Packaged review output escaped its requested synthetic folder.' }
    $reviewManifest = Get-Content -LiteralPath (Join-Path $reviewDirectory 'manifest.json') -Raw | ConvertFrom-Json
    if ($reviewManifest.tenantId -ne '11111111-1111-4111-8111-111111111111' -or $reviewManifest.notice -notmatch 'not a live assessment') { throw 'Review manifest lost client identity or limitations.' }
    foreach ($name in 'engineer.html','client.html','review.csv.zip') {
        $actual = (Get-FileHash -LiteralPath (Join-Path $reviewDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $reviewManifest.files.$name) { throw "Review output fingerprint mismatch: $name" }
        Remove-Item -LiteralPath (Join-Path $reviewDirectory $name) -Force
    }
    Remove-Item -LiteralPath (Join-Path $reviewDirectory 'manifest.json') -Force
    Remove-Item -LiteralPath $reviewDirectory -Force
    $report = Invoke-OwnedCli $exe ('--cli report --snapshot "' + $snapshotFile + '" --format html') 0
    if ($report.Error) { throw "Packaged CLI report wrote to stderr: $($report.Error)" }
    $reportLine = @($report.Text -split "`r?`n" | Where-Object { $_ -match '^Report: ' })
    if ($reportLine.Count -ne 1) { throw 'Packaged CLI did not print exactly one assessment report path.' }
    Assert-SyntheticReport $reportLine[0].Substring('Report: '.Length) 'assessment-*.html' 'assessment report'
    # The registered report path uses a strict separate fixture, never configuration assessment or live collection.
    $registeredFile = Join-Path $synthetic 'synthetic registered report.json'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-fixtures\registered-report.json') -Destination $registeredFile
    $registered = Invoke-OwnedCli $exe ('--cli report-evidence --input "' + $registeredFile + '" --tenant 11111111-1111-4111-8111-111111111111') 0
    if ($registered.Error -or $registered.Text -notmatch 'read status Partial') { throw 'Packaged registered report lost its partial read state.' }
    $registeredExports = @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -Filter 'tenant-report-intune-devices-*.html' -File)
    if ($registeredExports.Count -ne 1 -or [IO.Path]::GetFullPath((@($registered.Text -split "`r?`n")[0]).Trim()) -ine $registeredExports[0].FullName) { throw 'Packaged registered report did not return its exact export path.' }
    $registeredHtml = Get-Content -LiteralPath $registeredExports[0].FullName -Raw
    if ($registeredHtml -notmatch '11111111-1111-4111-8111-111111111111' -or $registeredHtml -notmatch 'not an empty successful check' -or $registeredHtml -notmatch 'Read status: Partial') { throw 'Packaged registered report invented a complete empty read.' }
    $wrongRegisteredTenant = Invoke-OwnedCli $exe ('--cli report-evidence --input "' + $registeredFile + '" --tenant 22222222-2222-4222-8222-222222222222') 2
    if ($wrongRegisteredTenant.Text -or $wrongRegisteredTenant.Error -notmatch 'Refused:') { throw 'Packaged registered report did not refuse another tenant.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -Filter 'tenant-report-intune-devices-*.html' -File).Count -ne 1) { throw 'Refused registered report wrote another export.' }
    # Strictly sealed synthetic schema 2 fixtures exercise both application families in the fresh extraction.
    # HTML uses the packaged executable; JSON uses the shipped launcher, preserving the same offline path.
    foreach ($family in 'expiry','consent') {
        $applicationFile = Join-Path $synthetic ('synthetic application ' + $family + '.json')
        $fixtureName = if ($family -eq 'expiry') { 'application-expiry-current.json' } else { 'application-consent.json' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('test-fixtures\' + $fixtureName)) -Destination $applicationFile
        $originalApplication = Get-Content -LiteralPath $applicationFile -Raw
        $applicationDigest = (Get-FileHash -LiteralPath $applicationFile -Algorithm SHA256).Hash
        $applicationFixture = $originalApplication | ConvertFrom-Json
        # PS7 converts JSON timestamps to DateTime; preserve the exact recorded ISO text on PS5.1 and PS7.
        $applicationStart = [regex]::Match($originalApplication, '"startedAt"\s*:\s*"([^"]+)"').Groups[1].Value
        $applicationEnd = [regex]::Match($originalApplication, '"endedAt"\s*:\s*"([^"]+)"').Groups[1].Value
        $applicationId = 'application-' + $family
        if ($applicationFixture.schemaVersion -ne 2 -or $applicationFixture.reportId -cne $applicationId -or $applicationFixture.status -cne 'Partial' -or -not $applicationStart -or -not $applicationEnd) { throw 'Unexpected synthetic application fixture contract.' }
        foreach ($format in 'html','json') {
            $arguments = 'report-evidence --input "' + $applicationFile + '" --tenant 11111111-1111-4111-8111-111111111111 --format ' + $format
            $application = if ($format -eq 'html') { Invoke-OwnedCli $exe ('--cli ' + $arguments) 0 }
                else { Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" ' + $arguments + '"') 0 }
            if ($application.Error -or -not $application.Text.Contains('Offline export ' + [char]0x00B7 + ' read status ' + $applicationFixture.status) -or
                -not $application.Text.Contains($applicationStart + ' to ' + $applicationEnd) -or
                -not $application.Text.Contains('no live reads or writes. Export success does not prove collection success.')) { throw "Packaged $applicationId lost its original read status or timestamps." }
            $applicationExports = @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -Filter ('tenant-report-' + $applicationId + '-*.' + $format) -File)
            if ($applicationExports.Count -ne 1 -or [IO.Path]::GetFullPath((@($application.Text -split "`r?`n")[0]).Trim()) -ine $applicationExports[0].FullName) { throw "Packaged $applicationId did not return its exact $format export path." }
            $applicationContent = Get-Content -LiteralPath $applicationExports[0].FullName -Raw
            if ($format -eq 'json') {
                # Full original nested evidence, digest, source claims and fixed horizon are preserved, not a display projection.
                # Current JSON is reserialised with the platform newline; only CRLF versus LF may differ.
                # Escaped line breaks inside JSON strings are untouched, as are every field, value and digest.
                if ($applicationContent.Replace("`r`n", "`n") -cne $originalApplication.Replace("`r`n", "`n")) {
                    throw "Packaged $applicationId JSON changed the original evidence beyond platform newline formatting."
                }
            } else {
                if (-not $applicationContent.Contains('Read status: Partial') -or
                    -not $applicationContent.Contains('Offline supplied file: source, account and live-mode claims have not been independently verified.') -or
                    -not $applicationContent.Contains('No live reads were performed for this export.') -or
                    -not $applicationContent.Contains($applicationStart) -or -not $applicationContent.Contains($applicationEnd)) { throw "Packaged $applicationId HTML lost supplied-file provenance or original collection times." }
                $nestedValues = if ($family -eq 'expiry') {
                    @('55555555-5555-4555-8555-555555555555','66666666-6666-4666-8666-666666666666',
                      '44444444-4444-4444-8444-444444444444','33333333-3333-4333-8333-333333333333',
                      'Synthetic metadata','ExpiringWithinHorizon','2026-09-20T00:00:00Z','Credentials','Owners')
                } else {
                    @('55555555-5555-4555-8555-555555555555','66666666-6666-4666-8666-666666666666',
                      '44444444-4444-4444-8444-444444444444','nDJ_opaque-Base64url_Assignment-1',
                      'Read synthetic records','Synthetic.Read','NotAttempted','Role definitions')
                }
                foreach ($value in $nestedValues) { if (-not $applicationContent.Contains($value)) { throw "Packaged $applicationId HTML omitted nested metadata: $value" } }
            }
        }
        $beforeRefusal = @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count
        $wrongApplicationTenant = Invoke-OwnedCli $exe ('--cli report-evidence --input "' + $applicationFile + '" --tenant 22222222-2222-4222-8222-222222222222') 2
        if ($wrongApplicationTenant.Text -or $wrongApplicationTenant.Error -notmatch 'Refused:.*Application report evidence belongs to another tenant' -or
            @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count -ne $beforeRefusal) { throw "Packaged $applicationId wrong-tenant refusal wrote output." }
        $tamperedApplicationFile = Join-Path $synthetic ('tampered application ' + $family + '.json')
        $tamperedApplication = $originalApplication.Replace('Synthetic portable fixture only; no tenant queried.', 'Changed synthetic fixture without resealing.')
        if ($tamperedApplication -ceq $originalApplication) { throw 'Synthetic tamper did not change the application fixture.' }
        [IO.File]::WriteAllText($tamperedApplicationFile, $tamperedApplication, [Text.UTF8Encoding]::new($false))
        $tamperedApplicationResult = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" report-evidence --input "' + $tamperedApplicationFile + '" --tenant 11111111-1111-4111-8111-111111111111"') 2
        if ($tamperedApplicationResult.Text -or $tamperedApplicationResult.Error -notmatch 'Refused:.*Application report integrity check failed' -or
            @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count -ne $beforeRefusal) { throw "Packaged $applicationId tamper refusal wrote output." }
        if ((Get-FileHash -LiteralPath $applicationFile -Algorithm SHA256).Hash -cne $applicationDigest -or
            (Get-Content -LiteralPath $tamperedApplicationFile -Raw) -cne $tamperedApplication) { throw 'Offline application export/refusal changed supplied evidence.' }
        Remove-Item -LiteralPath $applicationFile, $tamperedApplicationFile -Force
    }
    # Keep the original revision-1 fixture immutable and prove the explicit historical boundary in the actual ZIP.
    $historicalFile = Join-Path $synthetic 'synthetic original expiry.json'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'test-fixtures\application-expiry.json') -Destination $historicalFile
    $historicalBytes = [IO.File]::ReadAllBytes($historicalFile)
    $historicalHash = (Get-FileHash -LiteralPath $historicalFile -Algorithm SHA256).Hash
    $historicalNotice = 'Historical owner outcome consistency unverified'
    $beforeHistorical = @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count
    $defaultHistorical = Invoke-OwnedCli $exe ('--cli report-evidence --input "' + $historicalFile + '" --tenant 11111111-1111-4111-8111-111111111111 --format json') 2
    if ($defaultHistorical.Text -or -not $defaultHistorical.Error.Contains($historicalNotice) -or
        @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count -ne $beforeHistorical) { throw 'Historical expiry entered current validation or wrote output without explicit historical selection.' }
    $historical = Invoke-OwnedCli $exe ('--cli report-evidence --input "' + $historicalFile + '" --tenant 11111111-1111-4111-8111-111111111111 --format json --historical') 0
    if ($historical.Error -or -not $historical.Text.Contains($historicalNotice)) { throw 'Explicit historical expiry export lost its limitation.' }
    $historicalLines = @($historical.Text -split "`r?`n" | Where-Object { $_.Trim().EndsWith('.json.zip') })
    if ($historicalLines.Count -ne 1) { throw 'Historical export did not print exactly one bundle path.' }
    $historicalPath = [IO.Path]::GetFullPath($historicalLines[0].Trim())
    if ([IO.Path]::GetDirectoryName($historicalPath) -ine [IO.Path]::GetFullPath((Join-Path $extract 'reports'))) { throw 'Historical bundle path is outside the owned reports folder.' }
    if (-not $historicalPath.EndsWith('.json.zip') -or -not (Test-Path -LiteralPath $historicalPath) -or
        @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count -ne ($beforeHistorical + 1)) { throw 'Historical JSON did not produce exactly one labelled bundle.' }
    $historicalArchive = [IO.Compression.ZipFile]::OpenRead($historicalPath)
    try {
        if ($historicalArchive.Entries.Count -ne 2) { throw 'Historical bundle must contain the original record and its limitation.' }
        $originalEntry = $historicalArchive.GetEntry('report.json')
        $noticeEntry = $historicalArchive.GetEntry('READ-ME.txt')
        if ($null -eq $originalEntry -or $null -eq $noticeEntry) { throw 'Historical bundle is missing the original record or notice.' }
        $originalStream = $originalEntry.Open(); $originalCopy = [IO.MemoryStream]::new()
        try {
            $originalStream.CopyTo($originalCopy)
            if ([Convert]::ToBase64String($originalCopy.ToArray()) -cne [Convert]::ToBase64String($historicalBytes)) { throw 'Historical JSON changed the original bytes.' }
        } finally { $originalStream.Dispose(); $originalCopy.Dispose() }
        $noticeReader = [IO.StreamReader]::new($noticeEntry.Open())
        try { if (-not $noticeReader.ReadToEnd().Contains($historicalNotice)) { throw 'Historical bundle notice is missing.' } }
        finally { $noticeReader.Dispose() }
    } finally { $historicalArchive.Dispose() }
    $wrongHistoricalTenant = Invoke-OwnedCli $exe ('--cli report-evidence --input "' + $historicalFile + '" --tenant 22222222-2222-4222-8222-222222222222 --historical') 2
    if ($wrongHistoricalTenant.Text -or $wrongHistoricalTenant.Error -notmatch 'Refused:.*Application report evidence belongs to another tenant' -or
        @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File).Count -ne ($beforeHistorical + 1) -or
        (Get-FileHash -LiteralPath $historicalFile -Algorithm SHA256).Hash -cne $historicalHash) { throw 'Historical wrong-tenant refusal wrote output or changed the original record.' }
    Remove-Item -LiteralPath $historicalFile -Force
    if (Test-Path -LiteralPath (Join-Path $extract 'logs\startup.log')) { throw 'Offline CLI initialised the desktop workspace.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $extract 'data') -File -Recurse | Where-Object { $_.Name -match 'cache|token|session' }).Count -gt 0) { throw 'Offline CLI created an authentication cache.' }
    # Remove only test-owned outputs, leaving blank evidence for the existing desktop first-launch checks.
    Remove-Item -LiteralPath $snapshotFile, $profileFile, $registeredFile, $customerFile -Force
    Remove-Item -LiteralPath $synthetic -Force
    Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -File | Remove-Item -Force

    $process = Start-Process -FilePath $exe -WorkingDirectory $extract -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) { throw "Packaged application exited during startup: $($process.ExitCode)" }
    } while ($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq 0) { throw 'Packaged first launch did not show its window.' }
    foreach ($folder in 'data','logs','reports') {
        if (-not (Test-Path -LiteralPath (Join-Path $extract $folder) -PathType Container)) { throw "First launch did not create $folder." }
    }
    $startup = Get-Content -LiteralPath (Join-Path $extract 'logs\startup.log') -Raw
    if ($startup -notmatch ('Initialised\. Standard: ' + [regex]::Escape($settings.defaultStandardRelease))) { throw 'Packaged first launch did not load the default standard.' }
    if ($startup -match 'FATAL|CRASH') { throw 'Packaged startup recorded an error.' }

    # Exercise the actual extracted application, which cannot borrow its DLLs from the review harness.
    # UI Automation is restricted to our own process; the only input is synthetic catalogue search text. No sign-in is pressed.
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -AssemblyName WindowsBase
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PortableTextMenu {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@
    $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $descendants = [Windows.Automation.TreeScope]::Descendants
    function Find-Named([string]$Name, [object]$ControlType = $null) {
        $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
        if ($ControlType) {
            $condition = [Windows.Automation.AndCondition]::new($condition, [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType))
        }
        $element = $window.FindFirst($descendants, $condition)
        if (-not $element) { throw "Packaged UI did not expose: $Name" }
        return $element
    }
    # Use the visible catalogue search editor. Client details are below the connection cards and require page
    # scrolling; TextPattern.ScrollIntoView scrolls the text editor itself, not that outer page.
    $buildGroup = Find-Named 'Build standard' ([Windows.Automation.ControlType]::Button)
    $buildGroup.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
    $pages = Find-Named 'Page in the selected navigation group' ([Windows.Automation.ControlType]::ComboBox)
    $pageExpansion = $pages.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $pageExpansion.Expand()
    $catalogueCondition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Catalogue reference'),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$process.Id))
    $catalogueDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $catalogue = $pages.FindFirst($descendants, $catalogueCondition)
        if (-not $catalogue) { $catalogue = [Windows.Automation.AutomationElement]::RootElement.FindFirst($descendants, $catalogueCondition) }
        if (-not $catalogue) { Start-Sleep -Milliseconds 100 }
    } while (-not $catalogue -and [DateTime]::UtcNow -lt $catalogueDeadline)
    if (-not $catalogue) { throw 'Packaged page selector did not expose its catalogue route.' }
    $catalogue.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $pageExpansion.Collapse()
    Start-Sleep -Milliseconds 500
    $field = Find-Named 'Search standard controls'
    $testText = 'Synthetic text check'
    $field.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($testText)
    if (-not [PortableTextMenu]::SetForegroundWindow($process.MainWindowHandle)) { throw 'Could not foreground the owned packaged window for its text-menu check.' }
    $field.SetFocus()
    $range = $field.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).DocumentRange
    $range.ScrollIntoView($false)
    $range.Select()
    Start-Sleep -Milliseconds 200
    # Use the editor's visible bounds. WPF's TextPattern can expose no character rectangles even when its
    # single-line editor is visible; verify the actual selection separately instead of relying on that geometry.
    $selection = $field.GetCurrentPattern([Windows.Automation.TextPattern]::Pattern).GetSelection()
    $selectedText = (@($selection | ForEach-Object { $_.GetText(-1) }) -join '')
    if ($selectedText -ne $testText) { throw "Packaged editor did not select the synthetic text (selected length=$($selectedText.Length))." }
    $bounds = $field.Current.BoundingRectangle
    if ($field.Current.IsOffscreen -or $bounds.Width -lt 32 -or $bounds.Height -lt 10) { throw "Packaged editor is not visible for a right-click (offscreen=$($field.Current.IsOffscreen); bounds=$bounds)." }
    # The short text fits in this editor. The point just after its left padding is inside the selection.
    $clickX = [int]($bounds.Left + 16)
    $clickY = [int]($bounds.Top + $bounds.Height / 2)
    if (-not $bounds.Contains($clickX, $clickY) -or -not $window.Current.BoundingRectangle.Contains($clickX, $clickY)) { throw 'Refused to right-click outside the owned packaged text box.' }
    if (-not [PortableTextMenu]::SetCursorPos($clickX, $clickY)) { throw 'Could not position the pointer over the packaged text box.' }
    [PortableTextMenu]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
    [PortableTextMenu]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
    $copyCondition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$process.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, 'Copy'),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem))
    $copy = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $copy = [Windows.Automation.AutomationElement]::RootElement.FindFirst($descendants, $copyCondition)
    } while (-not $copy -and [DateTime]::UtcNow -lt $deadline)
    if (-not $copy -or -not $copy.Current.IsEnabled) {
        $menuCondition = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$process.Id),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::MenuItem))
        $menus = @([Windows.Automation.AutomationElement]::RootElement.FindAll($descendants, $menuCondition) | ForEach-Object { $_.Current.Name + ' (enabled=' + $_.Current.IsEnabled + ')' })
        throw ('Packaged text-box context menu did not expose an enabled Copy command. Owned menu items: ' + ($menus -join ', '))
    }
    $copy.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 100
    if ((Get-Clipboard -Raw).TrimEnd("`r", "`n") -ne $testText) { throw 'Packaged text-box context-menu Copy did not copy the selected synthetic text.' }
    foreach ($log in Get-ChildItem -LiteralPath (Join-Path $extract 'logs') -File) {
        if ((Get-Content -LiteralPath $log.FullName -Raw) -match 'Unhandled UI exception|Could not load file or assembly|FATAL|CRASH') { throw 'Packaged text editing recorded a runtime error.' }
    }
    if (-not $process.CloseMainWindow()) { throw 'Packaged application did not accept graceful close.' }
    if (-not $process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw 'Packaged application did not shut down cleanly.' }
    $result = [ordered]@{ sourceCommit=$metadata.sourceCommit; version=$metadata.version; standard=$settings.defaultStandardRelease;
        zipSha256=$expectedZip; extractedFiles=$files.Count; stageBytesMatch=$true; checksumsVerified=$true;
        blankConnectionSettings=$true; emptyEvidenceFolders=$true; actualPackagedFirstLaunch=$true; gracefulShutdown=$true;
        accessibilityAssemblyVerified=$true; textBoxContextMenuOpened=$true; contextMenuCopyVerified=$true;
        portableCliHelp=$true; portableCliLauncher=$true; portableCliLegacyAlias=$true; portableCliDoctorReady=$true; portableCliDoctorRequiredChecks=$true; portableCliDoctorRejections=$true; portableCliPipedStdoutInheritedStderr=$inheritedErrorType; portableCliExitCodes=$true; portableCliInventory=$true; portableCliJobs=$true; portableCliReport=$true; portableCliRegisteredReport=$true; portableCliApplicationExpiry=$true; portableCliApplicationConsent=$true; portableCliApplicationHistorical=$true; noCliDesktopOrAuthInitialisation=$true;
        portableCliCustomerPreview=$true; portableCliCustomerMalformedRefused=$true; portableCliClients=$true; portableCliClientReviewPack=$true; portableCliDomainReport=$true; portableCliDomainCollectionRefused=$true;
        tenantOperationsPerformed=$false; note='Offline Windows first launch only. An interactive console run of buildstandard.cmd (and legacy bdit.cmd), WAM, physical accessibility and Microsoft service/device acceptance remain unperformed.' }
    $parent = Split-Path -Parent $ResultPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($ResultPath), (($result | ConvertTo-Json -Depth 4) + "`n"), [Text.UTF8Encoding]::new($false))
    Write-Host "Verified $($files.Count) extracted files, exact stage bytes and packaged offline startup/shutdown."
} catch {
    $failure = $_
    # Only the owned fresh-extraction logs are inspected; this process has blank settings and never signs in.
    # Preserve any application error behind a failed automation step instead of reporting only "menu not found".
    $runtimeErrors = @()
    if (Test-Path -LiteralPath (Join-Path $extract 'logs')) {
        try {
            $runtimeErrors = @(Get-ChildItem -LiteralPath (Join-Path $extract 'logs') -File | ForEach-Object {
                Get-Content -LiteralPath $_.FullName | Where-Object { $_ -match 'Unhandled UI exception|Could not load file or assembly|FATAL|CRASH' }
            } | Select-Object -First 10)
        } catch { } # A diagnostic read must not replace the original failure.
    }
    if ($runtimeErrors.Count -gt 0) {
        throw ($failure.Exception.Message + "`nPackaged runtime errors:`n" + ($runtimeErrors -join "`n") + "`n" + $failure.ScriptStackTrace)
    }
    throw
} finally {
    if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
    if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
}
