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
    $deps = Get-Content -LiteralPath (Join-Path $extract 'app\BDIT.TenantToolkit.App.deps.json') -Raw
    if (-not $deps.Contains('"Accessibility.dll"')) { throw 'Accessibility.dll is missing from the runtime dependency manifest.' }
    $exe = Join-Path $extract 'app\BDIT.TenantToolkit.App.exe'
    # Exercise the same self-contained executable in offline CLI mode before desktop startup.
    $executables = @(Get-ChildItem -LiteralPath $extract -Filter '*.exe' -File -Recurse)
    # Preserve the SDK's existing runtime crash diagnostic; it is not a second toolkit application host.
    if (@($executables | Where-Object { $_.Name -eq 'BDIT.TenantToolkit.App.exe' }).Count -ne 1 -or
        @($executables | Where-Object { $_.Name -notin @('BDIT.TenantToolkit.App.exe', 'createdump.exe') }).Count -gt 0) { throw 'Unexpected second application executable in the portable toolkit.' }
    if (-not (Test-Path -LiteralPath (Join-Path $extract 'app\bdit.dll')) -or -not $deps.Contains('"bdit/')) { throw 'Portable CLI managed dependency is missing.' }
    $launcher = Join-Path $extract 'bdit.cmd'
    if (-not (Test-Path -LiteralPath $launcher)) { throw 'Portable CLI launcher is missing.' }
    function Invoke-OwnedCli([string]$FileName, [string]$Arguments, [int]$ExpectedExit) {
        $info = [Diagnostics.ProcessStartInfo]::new()
        $info.FileName = $FileName; $info.Arguments = $Arguments; $info.WorkingDirectory = $extract
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
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
    if ($help.Text -notmatch 'bdit inventory' -or $help.Error) { throw 'CLI help did not preserve redirected stdout/stderr.' }
    $failure = Invoke-OwnedCli $exe '--cli inventory --snapshot "synthetic missing file.json"' 2
    if ($failure.Error -notmatch 'Refused:' -or $failure.Text) { throw 'CLI refusal did not preserve stderr or exit code.' }
    $unknown = Invoke-OwnedCli $exe '--cli synthetic-unknown-command' 64
    if ($unknown.Error -notmatch 'Unknown command') { throw 'CLI unknown-command status was lost.' }
    $viaLauncher = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" --help"') 0
    if ($viaLauncher.Text -notmatch 'bdit inventory' -or $viaLauncher.Error) { throw 'bdit.cmd did not forward help and stdout.' }
    $launcherFailure = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" inventory --snapshot "synthetic missing file.json""') 2
    if ($launcherFailure.Error -notmatch 'Refused:') { throw 'bdit.cmd lost the refusal or real exit code.' }
    $synthetic = Join-Path $extract 'synthetic CLI evidence'
    New-Item -ItemType Directory -Path $synthetic | Out-Null
    $outFile = Join-Path $synthetic 'redirected help.txt'; $errFile = Join-Path $synthetic 'redirected errors.txt'
    $fileHelp = Invoke-OwnedCli $env:ComSpec ('/d /s /c ""' + $launcher + '" --help > "' + $outFile + '" 2> "' + $errFile + '""') 0
    if ($fileHelp.Text -or $fileHelp.Error -or (Get-Content -LiteralPath $outFile -Raw) -notmatch 'bdit inventory' -or (Get-Item -LiteralPath $errFile).Length -ne 0) { throw 'CLI file redirection did not preserve output handles.' }
    Remove-Item -LiteralPath $outFile, $errFile -Force
    $snapshotFile = Join-Path $synthetic 'synthetic configuration.json'
    $snapshot = [ordered]@{ id='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; tenantId='11111111-1111-4111-8111-111111111111';
        tenantName='Synthetic portable tenant'; primaryDomain='synthetic.example.invalid'; clientLabel='Synthetic CLI';
        capturedAt='2026-10-08T00:00:00Z'; capturedBy='engineer@example.invalid'; sessionMode='Assessment'; standardRelease=$settings.defaultStandardRelease;
        toolkitVersion=$metadata.version; identitySource='Synthetic offline test'; collections=@{}; complete=$false; integrityDigest='' }
    [IO.File]::WriteAllText($snapshotFile, ($snapshot | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $inventory = Invoke-OwnedCli $exe ('--cli inventory --snapshot "' + $snapshotFile + '" --format html') 0
    $inventoryFiles = @(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -Filter 'configuration-*.html' -File)
    if ($inventoryFiles.Count -ne 1 -or (Get-Content -LiteralPath $inventoryFiles[0].FullName -Raw) -notmatch 'Synthetic portable tenant|INCOMPLETE|Incomplete') { throw 'Packaged CLI did not render actual synthetic inventory.' }
    $profileFile = Join-Path $extract 'data\profiles.json'
    [IO.File]::WriteAllText($profileFile, '[{"tenantId":"11111111-1111-4111-8111-111111111111","company":"Synthetic CLI client","domain":"synthetic.example.invalid","parameters":{}}]', [Text.UTF8Encoding]::new($false))
    $jobs = Invoke-OwnedCli $exe '--cli jobs --tenant 11111111-1111-4111-8111-111111111111' 0
    if ($jobs.Text -notmatch 'Synthetic CLI client.*0 job\(s\)') { throw 'Packaged CLI did not project stored synthetic client jobs.' }
    $report = Invoke-OwnedCli $exe ('--cli report --snapshot "' + $snapshotFile + '" --format html') 0
    if (@(Get-ChildItem -LiteralPath (Join-Path $extract 'reports') -Filter 'assessment-*.html' -File).Count -ne 1) { throw 'Packaged CLI did not generate the synthetic assessment report.' }
    if (Test-Path -LiteralPath (Join-Path $extract 'logs\startup.log')) { throw 'Offline CLI initialised the desktop workspace.' }
    if (@(Get-ChildItem -LiteralPath (Join-Path $extract 'data') -File -Recurse | Where-Object { $_.Name -match 'cache|token|session' }).Count -gt 0) { throw 'Offline CLI created an authentication cache.' }
    # Remove only test-owned outputs, leaving blank evidence for the existing desktop first-launch checks.
    Remove-Item -LiteralPath $snapshotFile, $profileFile -Force
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
    function Find-Named([string]$Name) {
        $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $Name)
        $element = $window.FindFirst($descendants, $condition)
        if (-not $element) { throw "Packaged UI did not expose: $Name" }
        return $element
    }
    # Use the visible catalogue search editor. Client details are below the connection cards and require page
    # scrolling; TextPattern.ScrollIntoView scrolls the text editor itself, not that outer page.
    $standard = Find-Named 'Build Standard'
    $standard.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
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
        portableCliHelp=$true; portableCliLauncher=$true; portableCliExitCodes=$true; portableCliInventory=$true; portableCliJobs=$true; portableCliReport=$true; noCliDesktopOrAuthInitialisation=$true;
        tenantOperationsPerformed=$false; note='Offline Windows first launch only. WAM, physical accessibility and Microsoft service/device acceptance remain unperformed.' }
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
