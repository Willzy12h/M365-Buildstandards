<#
Unified audit log search by user, operation, record type, object or free text.
Read only. Dates are UTC days and both are included. Results are fetched in pages of up to 5,000.
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

$count = 0
while ($count -lt $MaxRows) {
    $page = @(Search-UnifiedAuditLog @search)
    if ($page.Count -eq 0) { break }
    foreach ($record in $page) {
        if ($count -ge $MaxRows) {
            Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' rows. Narrow the dates or filters to see everything.')
            break
        }
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
}
