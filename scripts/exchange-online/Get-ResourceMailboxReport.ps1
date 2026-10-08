<#
Room and equipment mailboxes with their booking settings: automatic processing, conflicts, recurring meetings, booking
window, maximum duration, who may book and the resource delegates.
Read only. Resource delegates are shown exactly as Exchange returns them and are not resolved to accounts. A true or
false value Exchange did not return is shown as Unknown, never as False.
#>
param(
    [string[]]$Mailbox,
    [string]$ResourceType = 'All',
    [int]$MaxRows = 1000
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

$types = @('RoomMailbox', 'EquipmentMailbox')
if ($ResourceType -ne 'All') { $types = @($ResourceType) }
$properties = @('ResourceCapacity')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity -Properties $properties })
    foreach ($target in $targets) {
        $kind = [string]$target.RecipientTypeDetails
        if (@('RoomMailbox', 'EquipmentMailbox') -notcontains $kind) { throw ([string]$target.PrimarySmtpAddress + ' is a ' + $kind + ', not a room or equipment mailbox.') }
    }
    $targets = @($targets | Where-Object { $types -contains [string]$_.RecipientTypeDetails })
} else {
    $targets = @(Get-EXOMailbox -RecipientTypeDetails $types -ResultSize ($MaxRows + 1) -Properties $properties)
    if ($targets.Count -gt $MaxRows) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxRows + ' room or equipment mailboxes exist; only the first ' + $MaxRows + ' are listed.')
        $targets = @($targets | Select-Object -First $MaxRows)
    }
}

foreach ($target in $targets) {
    $processing = Get-CalendarProcessing -Identity $target.ExchangeGuid.ToString()
    $automate = Get-Text (Get-Value $processing 'AutomateProcessing')
    $notes = @()
    if (-not $automate) { $automate = 'Unknown'; $notes += 'Exchange did not return how booking requests are processed.' }
    $conflicts = Get-Flag $processing 'AllowConflicts'
    $bookIn = Get-Flag $processing 'AllBookInPolicy'
    if ($conflicts -eq 'Unknown' -or $bookIn -eq 'Unknown') { $notes += 'Exchange did not return every booking setting.' }
    $row = [pscustomobject]@{
        DisplayName = Get-Text (Get-Value $target 'DisplayName')
        PrimarySmtpAddress = [string]$target.PrimarySmtpAddress
        RecipientTypeDetails = [string]$target.RecipientTypeDetails
        ResourceCapacity = Get-Text (Get-Value $target 'ResourceCapacity')
        AutomateProcessing = $automate
        AllowConflicts = $conflicts
        AllowRecurringMeetings = Get-Flag $processing 'AllowRecurringMeetings'
        BookingWindowInDays = Get-Text (Get-Value $processing 'BookingWindowInDays')
        MaximumDurationInMinutes = Get-Text (Get-Value $processing 'MaximumDurationInMinutes')
        AllBookInPolicy = $bookIn
        AllRequestInPolicy = Get-Flag $processing 'AllRequestInPolicy'
        ProcessExternalMeetingMessages = Get-Flag $processing 'ProcessExternalMeetingMessages'
        ResourceDelegates = Get-Text (Get-Value $processing 'ResourceDelegates')
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
