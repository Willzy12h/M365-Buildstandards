<#
Shared mailboxes with whether direct sign-in is blocked, address list visibility, holds and archive.
Read only. A shared mailbox whose sign-in is not blocked can be signed in to directly if its password is known.
#>
param(
    [int]$MaxRows = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$properties = @('HiddenFromAddressListsEnabled', 'LitigationHoldEnabled', 'ArchiveStatus', 'ProhibitSendReceiveQuota')
$mailboxes = @(Get-EXOMailbox -RecipientTypeDetails SharedMailbox -ResultSize ($MaxRows + 1) -Properties $properties)
if ($mailboxes.Count -gt $MaxRows) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxRows + ' shared mailboxes exist; only the first ' + $MaxRows + ' are listed.')
    $mailboxes = @($mailboxes | Select-Object -First $MaxRows)
}

foreach ($mailbox in $mailboxes) {
    $user = Get-User -Identity $mailbox.ExchangeGuid.ToString()
    [pscustomobject]@{
        DisplayName = $mailbox.DisplayName
        PrimarySmtpAddress = [string]$mailbox.PrimarySmtpAddress
        SignInBlocked = [bool]$user.AccountDisabled
        HiddenFromAddressLists = [bool]$mailbox.HiddenFromAddressListsEnabled
        LitigationHoldEnabled = [bool]$mailbox.LitigationHoldEnabled
        ArchiveStatus = [string]$mailbox.ArchiveStatus
        ProhibitSendReceiveQuota = [string]$mailbox.ProhibitSendReceiveQuota
    }
}
