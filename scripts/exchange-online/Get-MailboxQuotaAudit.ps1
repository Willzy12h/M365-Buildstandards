<#
Quota audit: mailboxes using at least the chosen percentage of their prohibit send and receive quota.
Read only. An Unlimited or unreadable quota is reported as unknown, never guessed. A 100 GB quota observed
here is a configured value, not proof of a licence entitlement.
#>
param(
    [int]$ThresholdPercent = 90,
    [string[]]$RecipientType = @('UserMailbox', 'SharedMailbox'),
    [switch]$IncludeAll,
    [int]$MaxMailboxes = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Bytes([object]$Size) {
    $text = [string]$Size
    if ($text -match '\(([0-9,]+) bytes\)') { return [int64]($Matches[1] -replace ',', '') }
    return $null
}

$query = @{
    ResultSize = $MaxMailboxes + 1
    RecipientTypeDetails = $RecipientType
    Properties = @('ProhibitSendQuota', 'ProhibitSendReceiveQuota', 'IssueWarningQuota')
}
$mailboxes = @(Get-EXOMailbox @query)
if ($mailboxes.Count -gt $MaxMailboxes) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes match; only the first ' + $MaxMailboxes + ' were checked.')
    $mailboxes = @($mailboxes | Select-Object -First $MaxMailboxes)
}

$hundredGb = [int64]100 * 1024 * 1024 * 1024
foreach ($mailbox in $mailboxes) {
    $statistics = Get-EXOMailboxStatistics -Identity $mailbox.ExchangeGuid.ToString()
    $used = Get-Bytes $statistics.TotalItemSize
    $quota = Get-Bytes $mailbox.ProhibitSendReceiveQuota
    $percent = $null
    if ($null -ne $used -and $null -ne $quota -and $quota -gt 0) { $percent = [math]::Round(100 * $used / $quota, 1) }
    $quotaIs100Gb = 'Unknown'
    if ($null -ne $quota) { $quotaIs100Gb = [string]($quota -eq $hundredGb) }
    if (-not $IncludeAll -and ($null -eq $percent -or $percent -lt $ThresholdPercent)) { continue }
    [pscustomobject]@{
        DisplayName = $mailbox.DisplayName
        PrimarySmtpAddress = [string]$mailbox.PrimarySmtpAddress
        RecipientTypeDetails = [string]$mailbox.RecipientTypeDetails
        TotalItemSizeBytes = $used
        ProhibitSendReceiveQuota = [string]$mailbox.ProhibitSendReceiveQuota
        ProhibitSendQuota = [string]$mailbox.ProhibitSendQuota
        IssueWarningQuota = [string]$mailbox.IssueWarningQuota
        PercentOfQuota = $percent
        QuotaIs100GB = $quotaIs100Gb
    }
}
