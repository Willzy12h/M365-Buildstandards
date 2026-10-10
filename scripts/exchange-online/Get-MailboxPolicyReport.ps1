<#
Outlook on the web (OWA) mailbox policies and mobile device mailbox policies, each with whether it is the default and
its key settings.
Read only. A setting Exchange did not return is shown as Unknown, and so is one returned null; one returned as an empty
value is NotSet. Lists are counted, and only a list returned empty counts as 0. Known False and zero are shown as they
are. Which mailboxes use each policy is not read here: that needs one read per mailbox. Conditional Access and app
protection policies in Entra ID and Intune, which can also govern these clients, are not read.
#>
param(
    [string[]]$PolicyType = @('OwaMailbox', 'MobileDeviceMailbox')
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
# NotSet. Lists are counted, and only a list returned empty counts as 0; one holding a null, empty or whitespace-only
# entry is Unknown.
function Get-Settings([object]$Item, [string[]]$Names, [string[]]$Counted) {
    $pairs = @()
    foreach ($name in $Names) {
        $value = 'Unknown'
        if ($null -ne $Item -and $Item.PSObject.Properties[$name] -and $null -ne $Item.$name) {
            $raw = $Item.$name
            # A list holding a null, empty or whitespace-only entry could not be read in full: it is Unknown, not a
            # shorter count or a shorter list. Only a list returned empty counts as 0.
            $unreadable = $raw -is [System.Collections.IEnumerable] -and $raw -isnot [string] -and
                @(@($raw) | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0
            if ($unreadable) { $value = 'Unknown' }
            elseif ($Counted -contains $name) { $value = [string]@($raw | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count }
            elseif ($raw -is [bool]) { if ($raw) { $value = 'True' } else { $value = 'False' } }
            else { $value = Get-Text $raw; if ($value -eq '') { $value = 'NotSet' } }
        }
        $pairs += ($name + '=' + $value)
    }
    return $pairs -join '; '
}

$types = @(
    @{ Type = 'OwaMailbox'; Default = 'IsDefault'
       Settings = @('ConditionalAccessPolicy', 'DirectFileAccessOnPublicComputersEnabled', 'DirectFileAccessOnPrivateComputersEnabled',
           'WacViewingOnPublicComputersEnabled', 'AdditionalStorageProvidersAvailable', 'ActiveSyncIntegrationEnabled', 'AllowOfflineOn',
           'PersonalAccountCalendarsEnabled', 'BlockedFileTypes', 'AllowedFileTypes')
       Counted = @('BlockedFileTypes', 'AllowedFileTypes') },
    @{ Type = 'MobileDeviceMailbox'; Default = 'IsDefault'
       Settings = @('AllowNonProvisionableDevices', 'PasswordEnabled', 'AlphanumericPasswordRequired', 'AllowSimplePassword', 'MinPasswordLength',
           'MaxPasswordFailedAttempts', 'MaxInactivityTimeLock', 'PasswordExpiration', 'RequireDeviceEncryption', 'DeviceEncryptionEnabled')
       Counted = @() }
)

foreach ($type in $types) {
    if ($PolicyType -notcontains $type.Type) { continue }
    switch ($type.Type) {
        'OwaMailbox' { $policies = @(Get-OwaMailboxPolicy) }
        'MobileDeviceMailbox' { $policies = @(Get-MobileDeviceMailboxPolicy) }
    }
    foreach ($policy in $policies) {
        $notes = @()
        $name = Get-Text (Get-Value $policy 'Name')
        if (-not $name) { $name = 'Unknown'; $notes += 'Exchange did not return the policy name.' }
        $isDefault = Get-Flag $policy $type.Default
        if ($isDefault -eq 'Unknown') { $notes += 'Exchange did not return whether this is the default policy.' }
        $settings = Get-Settings $policy $type.Settings $type.Counted
        if ($settings -cmatch '=Unknown(;|$)') { $notes += 'One or more settings were not returned and are shown as Unknown.' }
        $row = [pscustomobject]@{
            PolicyType = $type.Type
            Name = $name
            IsDefault = $isDefault
            KeySettings = $settings
            WhenChanged = Get-Text (Get-Value $policy 'WhenChanged')
            Notes = $notes -join ' '
        }
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
