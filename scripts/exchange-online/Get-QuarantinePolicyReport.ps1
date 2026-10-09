<#
Quarantine policies: what end users may do with messages a policy quarantines, whether quarantine notifications are
sent, and the organisation's global quarantine notification settings.
Read only. EndUserPermissionsValue is shown as Exchange returned it and decoded into permission names using the bit
order Microsoft documents (view header 128, download 64, allow sender 32, block sender 16, request release 8, release 4,
preview 2, delete 1). AccessPreset is NoAccess, LimitedAccess or FullAccess only when the value equals that preset, and
Custom otherwise. A value Exchange did not return, or returned null, is Unknown, never False or NoAccess. Which
anti-spam, anti-phishing or anti-malware policy uses each quarantine policy is not read here.
#>
param(
    [switch]$IncludeGlobalSettings
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# True when any column holds Unknown, alone or as a name=Unknown setting, so the run can say what was not measured.
function Test-UnknownValue([object]$Row) {
    foreach ($property in @($Row.PSObject.Properties)) {
        if ([string]$property.Value -cmatch '(^|=)Unknown(;|$)') { return $true }
    }
    return $false
}
$unknownRows = 0

function Get-Value([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
    return $null
}

function Get-Text([object]$Value) {
    if ($null -eq $Value) { return '' }
    return ((@($Value) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ }) -join ', ') -replace '\s+', ' '
}

function Get-Flag([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $value = $Item.$Name
    if ($value -is [bool]) { if ($value) { return 'True' } else { return 'False' } }
    $text = [string]$value
    if ($text -eq 'True') { return 'True' }
    if ($text -eq 'False') { return 'False' }
    return 'Unknown'
}

# Name=value pairs; a setting that was not returned, or was returned null, is Unknown. One returned as an empty value is
# NotSet. Lists are counted, and only a list returned empty counts as 0.
function Get-Settings([object]$Item, [string[]]$Names, [string[]]$Counted) {
    $pairs = @()
    foreach ($name in $Names) {
        $value = 'Unknown'
        if ($null -ne $Item -and $Item.PSObject.Properties[$name] -and $null -ne $Item.$name) {
            $raw = $Item.$name
            if ($Counted -contains $name) { $value = [string]@($raw | Where-Object { $null -ne $_ -and [string]$_ -ne '' }).Count }
            elseif ($raw -is [bool]) { if ($raw) { $value = 'True' } else { $value = 'False' } }
            else { $value = Get-Text $raw; if ($value -eq '') { $value = 'NotSet' } }
        }
        $pairs += ($name + '=' + $value)
    }
    return $pairs -join '; '
}

$bits = @(
    @(128, 'PermissionToViewHeader'), @(64, 'PermissionToDownload'), @(32, 'PermissionToAllowSender'), @(16, 'PermissionToBlockSender'),
    @(8, 'PermissionToRequestRelease'), @(4, 'PermissionToRelease'), @(2, 'PermissionToPreview'), @(1, 'PermissionToDelete')
)

foreach ($policy in @(Get-QuarantinePolicy)) {
    $notes = @()
    $name = Get-Text (Get-Value $policy 'Name')
    if (-not $name) { $name = 'Unknown'; $notes += 'Exchange did not return the policy name.' }
    $value = 'Unknown'; $preset = 'Unknown'; $permissions = 'Unknown'
    $raw = Get-Value $policy 'EndUserQuarantinePermissionsValue'
    $number = 0
    if ($null -ne $raw -and [int]::TryParse([string]$raw, [ref]$number) -and $number -ge 0 -and $number -le 255) {
        $value = [string]$number
        $granted = @($bits | Where-Object { ($number -band $_[0]) -ne 0 } | ForEach-Object { $_[1] })
        $permissions = 'None'
        if ($granted.Count -gt 0) { $permissions = $granted -join ', ' }
        $preset = 'Custom'
        if ($number -eq 0) { $preset = 'NoAccess' }
        if ($number -eq 27) { $preset = 'LimitedAccess' }
        if ($number -eq 23) { $preset = 'FullAccess' }
    } elseif ($null -ne $raw) {
        $value = [string]$raw
        $notes += 'Exchange returned an end-user permissions value that is not a number from 0 to 255, so it was not decoded.'
    } else {
        $notes += 'Exchange did not return the end-user permissions value.'
    }
    $notifications = Get-Flag $policy 'ESNEnabled'
    if ($notifications -eq 'Unknown') { $notes += 'Exchange did not return whether quarantine notifications are on (ESNEnabled).' }
    $row = [pscustomobject]@{
        PolicyKind = 'QuarantinePolicy'
        Name = $name
        EndUserPermissionsValue = $value
        AccessPreset = $preset
        EndUserPermissions = $permissions
        NotificationsEnabled = $notifications
        GlobalSettings = 'NotApplicable'
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($IncludeGlobalSettings) {
    $globals = @(Get-QuarantinePolicy -QuarantinePolicyType GlobalQuarantinePolicy)
    if ($globals.Count -eq 0) {
        Write-Warning 'BDIT:UNKNOWN Exchange returned no global quarantine settings, so notification frequency and branding could not be read.'
    }
    foreach ($global in $globals) {
        $settings = Get-Settings $global @('EndUserSpamNotificationFrequency', 'OrganizationBrandingEnabled', 'EndUserSpamNotificationCustomFromAddress', 'MultiLanguageSetting') @('MultiLanguageSetting')
        $notes = @()
        if ($settings -cmatch '=Unknown(;|$)') { $notes += 'One or more global settings were not returned and are shown as Unknown.' }
        $name = Get-Text (Get-Value $global 'Name')
        if (-not $name) { $name = 'Unknown'; $notes += 'Exchange did not return the name of the global settings object.' }
        $row = [pscustomobject]@{
            PolicyKind = 'GlobalQuarantinePolicy'
            Name = $name
            EndUserPermissionsValue = 'NotApplicable'
            AccessPreset = 'NotApplicable'
            EndUserPermissions = 'NotApplicable'
            NotificationsEnabled = 'NotApplicable'
            GlobalSettings = $settings
            Notes = $notes -join ' '
        }
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
