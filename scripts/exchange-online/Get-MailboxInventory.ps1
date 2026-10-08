<#
Mailbox inventory: size, item count, quotas, archive and litigation hold for each mailbox.
Read only. Quotas are reported exactly as Exchange returns them, including Unlimited. A true or false value Exchange
did not return is shown as Unknown with a note, never as False.
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

function Get-Flag([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $value = $Item.$Name
    if ($value -is [bool]) { if ($value) { return 'True' } else { return 'False' } }
    $text = [string]$value
    if ($text -eq 'True') { return 'True' }
    if ($text -eq 'False') { return 'False' }
    return 'Unknown'
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
        if ($null -ne $statistics -and $statistics.PSObject.Properties['TotalItemSize']) { $bytes = Get-Bytes $statistics.TotalItemSize }
        if ($null -ne $statistics -and $statistics.PSObject.Properties['ItemCount']) { $items = $statistics.ItemCount }
    }
    $notes = @()
    if (-not $SkipSizes -and $null -eq $bytes) { $notes += 'The mailbox size could not be read.' }
    $hold = Get-Flag $mailbox 'LitigationHoldEnabled'
    if ($hold -eq 'Unknown') { $notes += 'Exchange did not return whether litigation hold is on.' }
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
        LitigationHoldEnabled = $hold
        Notes = $notes -join ' '
    }
}
