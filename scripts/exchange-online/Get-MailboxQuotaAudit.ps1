<#
Quota audit: mailboxes using at least the chosen percentage of their prohibit send and receive quota.
Read only. A mailbox whose size or quota cannot be measured (an Unlimited or unreadable quota, or an unreadable size) is
always listed with Status Unknown and the reason, never guessed and never left out. A 100 GB quota observed here is a
configured value, not proof of a licence entitlement.
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

function Get-Property([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
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
$unknown = 0
foreach ($mailbox in $mailboxes) {
    $statistics = Get-EXOMailboxStatistics -Identity $mailbox.ExchangeGuid.ToString()
    $used = Get-Bytes (Get-Property $statistics 'TotalItemSize')
    $quotaText = [string](Get-Property $mailbox 'ProhibitSendReceiveQuota')
    $quota = Get-Bytes $quotaText
    $reasons = @()
    if ($null -eq $used) { $reasons += 'The mailbox size could not be read.' }
    if ($quotaText.Trim() -eq 'Unlimited') { $reasons += 'The prohibit send and receive quota is Unlimited, so no percentage applies.' }
    elseif ($null -eq $quota) { $reasons += 'The prohibit send and receive quota could not be read.' }
    elseif ($quota -le 0) { $reasons += 'The prohibit send and receive quota is zero.' }
    $percent = $null
    $status = 'Unknown'
    if ($reasons.Count -eq 0) {
        $percent = [math]::Round(100 * $used / $quota, 1)
        $status = 'BelowThreshold'
        if ($percent -ge $ThresholdPercent) { $status = 'AtOrAboveThreshold' }
    } else {
        $unknown++
    }
    $quotaIs100Gb = 'Unknown'
    if ($null -ne $quota) { $quotaIs100Gb = [string]($quota -eq $hundredGb) }
    # A mailbox that could not be measured is always kept: leaving it out would read as "under the threshold".
    if (-not $IncludeAll -and $status -eq 'BelowThreshold') { continue }
    [pscustomobject]@{
        DisplayName = $mailbox.DisplayName
        PrimarySmtpAddress = [string]$mailbox.PrimarySmtpAddress
        RecipientTypeDetails = [string]$mailbox.RecipientTypeDetails
        TotalItemSizeBytes = $used
        ProhibitSendReceiveQuota = $quotaText
        ProhibitSendQuota = [string](Get-Property $mailbox 'ProhibitSendQuota')
        IssueWarningQuota = [string](Get-Property $mailbox 'IssueWarningQuota')
        PercentOfQuota = $percent
        QuotaIs100GB = $quotaIs100Gb
        Status = $status
        Reason = $reasons -join ' '
    }
}
if ($unknown -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknown + ' mailbox(es) could not be measured against their quota. They are listed with Status Unknown and the reason; they were not checked.')
}
