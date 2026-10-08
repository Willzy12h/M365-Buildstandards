<#
Unified audit log search by user, operation, record type, object or free text.
Read only. Dates are UTC days and both are included. Results are fetched in pages of up to 5,000. When the
limit is reached exactly at the end of a page, one more page is read to prove whether anything is left.
#>
param(
    [Parameter(Mandatory = $true)][string]$StartDate,
    [Parameter(Mandatory = $true)][string]$EndDate,
    [string[]]$UserIds,
    [string[]]$Operations,
    [string]$RecordType,
    [string[]]$ObjectIds,
    [string]$FreeText,
    [int]$MaxRows = 1000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$culture = [Globalization.CultureInfo]::InvariantCulture
$utc = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
$search = @{
    StartDate = [datetime]::ParseExact($StartDate, 'yyyy-MM-dd', $culture, $utc)
    EndDate = [datetime]::ParseExact($EndDate, 'yyyy-MM-dd', $culture, $utc).AddDays(1)
    SessionId = [guid]::NewGuid().ToString()
    SessionCommand = 'ReturnLargeSet'
    ResultSize = 5000
}
if ($UserIds) { $search.UserIds = $UserIds }
if ($Operations) { $search.Operations = $Operations }
if ($RecordType) { $search.RecordType = $RecordType }
if ($ObjectIds) { $search.ObjectIds = $ObjectIds }
if ($FreeText) { $search.FreeText = $FreeText }

function Get-Field([object]$Data, [string]$Name) {
    if ($null -ne $Data -and $Data.PSObject.Properties[$Name]) { return [string]$Data.$Name }
    return ''
}

# Pages are read until an empty page proves the session is finished. Reaching the limit inside a page, or finding a
# further page after the limit, marks the result partial; it is never presented as complete without that proof.
$count = 0
$partial = $false
while ($true) {
    $page = @(Search-UnifiedAuditLog @search)
    if ($page.Count -eq 0) { break }
    if ($count -ge $MaxRows) { $partial = $true; break }
    foreach ($record in $page) {
        if ($count -ge $MaxRows) { $partial = $true; break }
        $count++
        $raw = [string]$record.AuditData
        $data = $null
        try { $data = $raw | ConvertFrom-Json } catch { $data = $null }
        if ($raw.Length -gt 8000) { $raw = $raw.Substring(0, 8000) + ' [truncated]' }
        [pscustomobject]@{
            CreationDate = ([datetime]$record.CreationDate).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
            UserId = [string]$record.UserIds
            Operation = [string]$record.Operations
            RecordType = [string]$record.RecordType
            Workload = Get-Field $data 'Workload'
            ObjectId = Get-Field $data 'ObjectId'
            ClientIP = Get-Field $data 'ClientIP'
            AuditData = $raw
        }
    }
    if ($partial) { break }
}
if ($partial) {
    Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' rows and more records exist. Narrow the dates or filters to see everything.')
}
