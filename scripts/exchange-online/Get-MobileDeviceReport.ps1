<#
Mobile devices that have synchronised with each mailbox, with access state and, if ticked, the last successful sync.
Read only. Devices are read per mailbox, so each row names the mailbox by its primary SMTP address. A last sync that
was not returned is Unknown; with a stale filter, a device whose last sync is Unknown is always kept, never treated as
recent. A true or false value Exchange did not return is shown as Unknown, never as False.
#>
param(
    [string[]]$Mailbox,
    [switch]$IncludeLastSync,
    [int]$StaleDays,
    [int]$MaxMailboxes = 500,
    [int]$MaxRows = 5000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($StaleDays -gt 0 -and -not $IncludeLastSync) { throw 'Tick Include last sync to filter devices by when they last synchronised.' }

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
    return ((@($Value) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ }) -join '; ') -replace '\s+', ' '
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

if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity })
} else {
    $targets = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1))
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
}

$now = [DateTime]::UtcNow
$rows = 0
$stopped = $false
foreach ($target in $targets) {
    if ($stopped) { break }
    $address = [string]$target.PrimarySmtpAddress
    # Each mailbox's devices are read up to the row limit, plus one to show whether more exist.
    $devices = @(Get-MobileDevice -Mailbox $target.ExchangeGuid.ToString() -ResultSize ($MaxRows + 1))
    if ($devices.Count -gt $MaxRows) {
        $stopped = $true
        $devices = @($devices | Select-Object -First $MaxRows)
    }
    foreach ($device in $devices) {
        if ($rows -ge $MaxRows) { $stopped = $true; break }
        $notes = @()
        $lastSync = ''
        $age = ''
        $status = 'NotChecked'
        if ($IncludeLastSync) {
            $key = Get-Text (Get-Value $device 'Guid')
            if (-not $key) { $key = Get-Text (Get-Value $device 'Identity') }
            $statistics = $null
            $failure = ''
            if ($key) {
                try { $statistics = Get-MobileDeviceStatistics -Identity $key }
                catch { $failure = $_.Exception.Message }
            } else { $failure = 'Exchange returned no identity for this device.' }
            $value = Get-Value $statistics 'LastSuccessSync'
            if ($null -ne $value -and $value -isnot [datetime]) {
                $parsed = [datetime]::MinValue
                $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
                if ([datetime]::TryParse([string]$value, [Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) { $value = $parsed }
            }
            $status = 'Unknown'
            if ($value -is [datetime]) {
                $lastSync = $value.ToUniversalTime().ToString('yyyy-MM-dd HH:mm') + ' UTC'
                $days = [math]::Floor(($now - $value.ToUniversalTime()).TotalDays)
                $age = $days
                $status = 'Recent'
                if ($StaleDays -gt 0 -and $days -ge $StaleDays) { $status = 'Stale' }
                if ($StaleDays -le 0) { $status = 'Dated' }
            } else {
                $notes += 'Exchange did not return a last successful sync for this device.'
                if ($failure) { $notes += ('The statistics read failed: ' + $failure) }
            }
            # A device that cannot be dated is kept: leaving it out would read as "synchronised recently".
            if ($StaleDays -gt 0 -and $status -eq 'Recent') { continue }
        }
        $rows++
        $row = [pscustomobject]@{
            Mailbox = $address
            FriendlyName = Get-Text (Get-Value $device 'FriendlyName')
            DeviceModel = Get-Text (Get-Value $device 'DeviceModel')
            DeviceOS = Get-Text (Get-Value $device 'DeviceOS')
            DeviceType = Get-Text (Get-Value $device 'DeviceType')
            ClientType = Get-Text (Get-Value $device 'ClientType')
            DeviceId = Get-Text (Get-Value $device 'DeviceId')
            DeviceAccessState = Get-Text (Get-Value $device 'DeviceAccessState')
            IsManaged = Get-Flag $device 'IsManaged'
            IsCompliant = Get-Flag $device 'IsCompliant'
            FirstSyncTime = Get-Text (Get-Value $device 'FirstSyncTime')
            LastSuccessSync = $lastSync
            DaysSinceLastSync = $age
            SyncStatus = $status
            Notes = $notes -join ' '
        }
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($stopped) { Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' devices.') }
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
