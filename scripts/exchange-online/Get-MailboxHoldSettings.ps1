# INT-121: observed read-only mailbox settings. Exact identities and per-value read states only.
param()
Set-StrictMode -Version Latest
$cap = 10000
$rows = [System.Collections.Generic.List[object]]::new()
$reasons = [System.Collections.Generic.List[object]]::new()
$sectionStatus = 'Collected'
function TextOf($Item, [string]$Name) {
    if ($null -eq $Item) { return $null }
    $p = $Item.PSObject.Properties[$Name]
    if ($null -eq $p -or $null -eq $p.Value) { return $null }
    $text = [string]$p.Value
    if ([string]::IsNullOrWhiteSpace($text) -or $text.Length -gt 256 -or $text -match '[\x00-\x1F\x7F]') { return $null }
    return $text
}
function GuidOf([string]$Text) {
    if ($null -eq $Text -or $Text.Length -ne 36) { return $null }
    try { $guid = [guid]$Text; if ($guid -eq [guid]::Empty) { return $null }; return $guid.ToString('D') } catch { return $null }
}
function FlagOf($Item, $Row, $Problems, [string]$Property, [string]$Column) {
    $Row[$Column + 'ReadStatus'] = 'Failed'; $Row[$Column] = $null
    $p = $null
    if ($null -ne $Item) { $p = $Item.PSObject.Properties[$Property] }
    if ($null -ne $p -and $p.Value -is [bool]) { $Row[$Column + 'ReadStatus'] = 'Collected'; $Row[$Column] = $p.Value }
    else { $Problems.Add($Property + ' was not returned as a boolean.') }
}
$properties = @('ExchangeGuid', 'LitigationHoldEnabled', 'RetentionHoldEnabled', 'ComplianceTagHoldApplied', 'DelayHoldApplied', 'InPlaceHolds')
$mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ ResultSize = ($cap + 1); Properties = $properties })
if ($mailboxes.Count -gt $cap) {
    $mailboxes = @($mailboxes | Select-Object -First $cap); $sectionStatus = 'Partial'
    $reasons.Add('More than 10,000 mailboxes were found; only the first 10,000 are considered.')
}
foreach ($mailbox in $mailboxes) {
    $id = GuidOf (TextOf $mailbox 'ExchangeGuid')
    if ($null -eq $id) {
        $sectionStatus = 'Partial'
        if (-not $reasons.Contains('Mailboxes without an exact non-empty ExchangeGuid were excluded.')) { $reasons.Add('Mailboxes without an exact non-empty ExchangeGuid were excluded.') }
        continue
    }
    $problems = [System.Collections.Generic.List[object]]::new()
    $row = [ordered]@{ id = $id; name = (TextOf $mailbox 'DisplayName'); primarySmtpAddress = (TextOf $mailbox 'PrimarySmtpAddress'); readStatus = 'Collected'; error = $null }
    FlagOf $mailbox $row $problems 'LitigationHoldEnabled' 'litigationHoldEnabled'
    FlagOf $mailbox $row $problems 'RetentionHoldEnabled' 'retentionHoldEnabled'
    FlagOf $mailbox $row $problems 'ComplianceTagHoldApplied' 'complianceTagHoldApplied'
    FlagOf $mailbox $row $problems 'DelayHoldApplied' 'delayHoldApplied'
    $row.inPlaceHoldIdsReadStatus = 'Failed'; $row.inPlaceHoldIds = $null
    $p = $mailbox.PSObject.Properties['InPlaceHolds']
    if ($null -ne $p -and $null -ne $p.Value -and $p.Value -isnot [string]) {
        $ids = @($p.Value); $valid = $ids.Count -le 100
        foreach ($value in $ids) { if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value) -or $value.Length -gt 256 -or $value -match '[\x00-\x1F\x7F]') { $valid = $false } }
        if ($valid) { $row.inPlaceHoldIdsReadStatus = 'Collected'; $row.inPlaceHoldIds = $ids }
    }
    if ($row.inPlaceHoldIdsReadStatus -ne 'Collected') { $problems.Add('The hold identifiers were missing, malformed or exceeded 100; the collection is unknown, not empty.') }
    if ($problems.Count -gt 0) {
        $row.readStatus = 'Partial'; $row.error = [string]::Join(' ', $problems.ToArray()); $sectionStatus = 'Partial'
        if (-not $reasons.Contains('Some mailbox settings could not be read; inspect each row.')) { $reasons.Add('Some mailbox settings could not be read; inspect each row.') }
    }
    $rows.Add($row)
}
$errorText = $null
if ($reasons.Count -gt 0) { $errorText = [string]::Join(' ', $reasons.ToArray()) }
@{ Sections = @(@{ Id = 'holds'; Status = $sectionStatus; Error = $errorText; Rows = $rows.ToArray() }) }
