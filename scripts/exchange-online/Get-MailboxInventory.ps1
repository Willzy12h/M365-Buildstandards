<#
Mailbox inventory: size, item count, quotas, archive and litigation hold for each mailbox.
Read only. Quotas are reported exactly as Exchange returns them, including Unlimited.
#>
param(
    [string[]]$RecipientType = @('UserMailbox', 'SharedMailbox'),
    [switch]$SkipSizes,
    [int]$MaxRows = 500
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Bytes([object]$Size) {
    $text = [string]$Size
    if ($text -match '\(([0-9,]+) bytes\)') { return [int64]($Matches[1] -replace ',', '') }
    return $null
}

$query = @{
    ResultSize = $MaxRows + 1
    RecipientTypeDetails = $RecipientType
    Properties = @('IssueWarningQuota', 'ProhibitSendQuota', 'ProhibitSendReceiveQuota', 'ArchiveStatus', 'LitigationHoldEnabled')
}
$mailboxes = @(Get-EXOMailbox @query)
if ($mailboxes.Count -gt $MaxRows) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxRows + ' mailboxes match; only the first ' + $MaxRows + ' are listed.')
    $mailboxes = @($mailboxes | Select-Object -First $MaxRows)
}

foreach ($mailbox in $mailboxes) {
    $bytes = $null
    $items = $null
    if (-not $SkipSizes) {
        $statistics = Get-EXOMailboxStatistics -Identity $mailbox.ExchangeGuid.ToString()
        $bytes = Get-Bytes $statistics.TotalItemSize
        $items = $statistics.ItemCount
    }
    [pscustomobject]@{
        DisplayName = $mailbox.DisplayName
        PrimarySmtpAddress = [string]$mailbox.PrimarySmtpAddress
        RecipientTypeDetails = [string]$mailbox.RecipientTypeDetails
        TotalItemSizeBytes = $bytes
        ItemCount = $items
        IssueWarningQuota = [string]$mailbox.IssueWarningQuota
        ProhibitSendQuota = [string]$mailbox.ProhibitSendQuota
        ProhibitSendReceiveQuota = [string]$mailbox.ProhibitSendReceiveQuota
        ArchiveStatus = [string]$mailbox.ArchiveStatus
        LitigationHoldEnabled = [bool]$mailbox.LitigationHoldEnabled
    }
}
