<#
Shared mailboxes with whether direct sign-in is blocked, address list visibility, holds and archive.
Read only. A shared mailbox whose sign-in is not blocked can be signed in to directly if its password is known.
A true or false value Exchange did not return is shown as Unknown with a note, never as False.
#>
param(
    [int]$MaxRows = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Flag([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $value = $Item.$Name
    if ($value -is [bool]) { if ($value) { return 'True' } else { return 'False' } }
    $text = [string]$value
    if ($text -eq 'True') { return 'True' }
    if ($text -eq 'False') { return 'False' }
    return 'Unknown'
}

$properties = @('HiddenFromAddressListsEnabled', 'LitigationHoldEnabled', 'ArchiveStatus', 'ProhibitSendReceiveQuota')
$mailboxes = @(Get-EXOMailbox -RecipientTypeDetails SharedMailbox -ResultSize ($MaxRows + 1) -Properties $properties)
if ($mailboxes.Count -gt $MaxRows) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxRows + ' shared mailboxes exist; only the first ' + $MaxRows + ' are listed.')
    $mailboxes = @($mailboxes | Select-Object -First $MaxRows)
}

foreach ($mailbox in $mailboxes) {
    $user = Get-User -Identity $mailbox.ExchangeGuid.ToString()
    $blocked = Get-Flag $user 'AccountDisabled'
    $hidden = Get-Flag $mailbox 'HiddenFromAddressListsEnabled'
    $hold = Get-Flag $mailbox 'LitigationHoldEnabled'
    $notes = @()
    if ($blocked -eq 'Unknown') { $notes += 'Exchange did not return whether sign-in is blocked; check the account in Entra.' }
    if ($hidden -eq 'Unknown') { $notes += 'Exchange did not return address list visibility.' }
    if ($hold -eq 'Unknown') { $notes += 'Exchange did not return whether litigation hold is on.' }
    [pscustomobject]@{
        DisplayName = $mailbox.DisplayName
        PrimarySmtpAddress = [string]$mailbox.PrimarySmtpAddress
        SignInBlocked = $blocked
        HiddenFromAddressLists = $hidden
        LitigationHoldEnabled = $hold
        ArchiveStatus = [string]$mailbox.ArchiveStatus
        ProhibitSendReceiveQuota = [string]$mailbox.ProhibitSendReceiveQuota
        Notes = $notes -join ' '
    }
}
