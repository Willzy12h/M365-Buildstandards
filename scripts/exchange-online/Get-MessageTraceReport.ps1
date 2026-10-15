<#
Message trace for up to 90 days, searched in windows of 10 days and joined.
Read only. Dates are UTC days and both are included. Leave optional fields blank to ignore them.
#>
param(
    [Parameter(Mandatory = $true)][string]$StartDate,
    [Parameter(Mandatory = $true)][string]$EndDate,
    [string[]]$SenderAddress,
    [string[]]$RecipientAddress,
    [string]$Subject,
    [string]$SubjectMatch = 'Contains',
    [string[]]$Status,
    [string]$FromIP,
    [string]$ToIP,
    [string]$MessageId,
    [int]$MaxRows = 1000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [Globalization.CultureInfo]::InvariantCulture
$utc = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
$from = [datetime]::ParseExact($StartDate, 'yyyy-MM-dd', $culture, $utc)
$until = [datetime]::ParseExact($EndDate, 'yyyy-MM-dd', $culture, $utc).AddDays(1)

$filters = @{}
if ($SenderAddress) { $filters.SenderAddress = $SenderAddress }
if ($RecipientAddress) { $filters.RecipientAddress = $RecipientAddress }
if ($Subject) { $filters.Subject = $Subject; $filters.SubjectFilterType = $SubjectMatch }
if ($Status) { $filters.Status = $Status }
if ($FromIP) { $filters.FromIP = $FromIP }
if ($ToIP) { $filters.ToIP = $ToIP }
if ($MessageId) { $filters.MessageId = $MessageId }

$count = 0
$windowStart = $from
while ($windowStart -lt $until -and $count -lt $MaxRows) {
    $windowEnd = $windowStart.AddDays(10)
    if ($windowEnd -gt $until) { $windowEnd = $until }
    $remaining = $MaxRows - $count
    $page = @(Get-MessageTraceV2 @filters -StartDate $windowStart -EndDate $windowEnd -ResultSize $remaining)
    if ($page.Count -ge $remaining) {
        Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' rows. Narrow the dates or filters to see everything.')
    }
    foreach ($message in $page) {
        $count++
        [pscustomobject]@{
            Received = ([datetime]$message.Received).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
            SenderAddress = [string]$message.SenderAddress
            RecipientAddress = [string]$message.RecipientAddress
            Subject = [string]$message.Subject
            Status = [string]$message.Status
            FromIP = [string]$message.FromIP
            ToIP = [string]$message.ToIP
            Size = $message.Size
            MessageId = [string]$message.MessageId
            MessageTraceId = [string]$message.MessageTraceId
        }
    }
    $windowStart = $windowEnd
}
