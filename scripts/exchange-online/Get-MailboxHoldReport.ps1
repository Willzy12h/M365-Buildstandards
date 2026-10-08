<#
Holds and retention on each mailbox: litigation hold, in-place and retention policy holds, delay holds, retention hold
and the retention policy assigned, with the organisation-wide holds Exchange reports.
Read only. HoldStatus is MailboxHold when a hold is set on the mailbox itself, NoMailboxHold when every hold value was
returned and none is set, and Unknown otherwise. Organisation-wide holds are listed for reference; whether they cover a
given mailbox is not worked out here. Hold IDs are not resolved to policy names. A true or false value Exchange did not
return is shown as Unknown, never as False; with only holds ticked, a mailbox with an Unknown hold value is kept.
#>
param(
    [string[]]$Mailbox,
    [switch]$OnlyOnHold,
    [int]$MaxMailboxes = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

# A hold list is Unknown when it was not returned or came back as null; an empty list means none.
function Get-HoldList([object]$Item) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties['InPlaceHolds']) { return 'Unknown' }
    $value = $Item.InPlaceHolds
    if ($null -eq $value) { return 'Unknown' }
    return Get-Text $value
}

$organisation = Get-OrganizationConfig
$orgHolds = Get-HoldList $organisation

$properties = @('LitigationHoldEnabled', 'LitigationHoldDate', 'LitigationHoldOwner', 'LitigationHoldDuration', 'InPlaceHolds',
    'RetentionHoldEnabled', 'RetentionPolicy', 'ComplianceTagHoldApplied', 'DelayHoldApplied', 'DelayReleaseHoldApplied')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity -Properties $properties })
} else {
    $targets = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties $properties)
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
}

$unknown = 0
foreach ($target in $targets) {
    $notes = @()
    $litigation = Get-Flag $target 'LitigationHoldEnabled'
    $tagHold = Get-Flag $target 'ComplianceTagHoldApplied'
    $delay = Get-Flag $target 'DelayHoldApplied'
    $delayRelease = Get-Flag $target 'DelayReleaseHoldApplied'
    $inPlace = Get-HoldList $target
    $values = @($litigation, $tagHold, $delay, $delayRelease)
    $status = 'NoMailboxHold'
    if ($values -contains 'Unknown' -or $inPlace -eq 'Unknown') { $status = 'Unknown' }
    if ($values -contains 'True' -or ($inPlace -ne 'Unknown' -and $inPlace -ne '')) { $status = 'MailboxHold' }
    if ($litigation -eq 'Unknown') { $notes += 'Exchange did not return whether litigation hold is on.' }
    if ($inPlace -eq 'Unknown') { $notes += 'Exchange did not return the in-place and retention policy holds.' }
    if ($tagHold -eq 'Unknown' -or $delay -eq 'Unknown' -or $delayRelease -eq 'Unknown') { $notes += 'Exchange did not return every hold flag.' }
    if ($status -eq 'Unknown') { $unknown++ }
    # A mailbox whose holds could not be read is always kept: leaving it out would read as "not on hold".
    if ($OnlyOnHold -and $status -eq 'NoMailboxHold') { continue }
    [pscustomobject]@{
        Mailbox = [string]$target.PrimarySmtpAddress
        RecipientTypeDetails = Get-Text (Get-Value $target 'RecipientTypeDetails')
        HoldStatus = $status
        LitigationHoldEnabled = $litigation
        LitigationHoldDate = Get-Text (Get-Value $target 'LitigationHoldDate')
        LitigationHoldOwner = Get-Text (Get-Value $target 'LitigationHoldOwner')
        LitigationHoldDuration = Get-Text (Get-Value $target 'LitigationHoldDuration')
        InPlaceHolds = $inPlace
        ComplianceTagHoldApplied = $tagHold
        DelayHoldApplied = $delay
        DelayReleaseHoldApplied = $delayRelease
        RetentionHoldEnabled = Get-Flag $target 'RetentionHoldEnabled'
        RetentionPolicy = Get-Text (Get-Value $target 'RetentionPolicy')
        OrganisationHolds = $orgHolds
        Notes = $notes -join ' '
    }
}
if ($unknown -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknown + ' mailbox(es) have hold values that were not returned. They are listed with HoldStatus Unknown and the reason.')
}
if ($orgHolds -eq 'Unknown') {
    Write-Warning 'BDIT:UNKNOWN The organisation-wide holds could not be read. OrganisationHolds is Unknown on every row.'
}
