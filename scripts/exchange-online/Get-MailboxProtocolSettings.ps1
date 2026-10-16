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
$properties = @('ExchangeGuid', 'ExternalDirectoryObjectId')
$mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ ResultSize = ($cap + 1); Properties = $properties })
if ($mailboxes.Count -gt $cap) {
    $mailboxes = @($mailboxes | Select-Object -First $cap); $sectionStatus = 'Partial'
    $reasons.Add('More than 10,000 mailboxes were found; only the first 10,000 are considered.')
}
$organisation = $null
try {
    $configs = @(Use-BditRead -Command 'Get-TransportConfig' -Parameters @{})
    if ($configs.Count -eq 1) { $p = $configs[0].PSObject.Properties['SmtpClientAuthenticationDisabled']; if ($null -ne $p -and $p.Value -is [bool]) { $organisation = $p.Value } }
} catch { $organisation = $null }
if ($null -eq $organisation) { $sectionStatus = 'Partial'; $reasons.Add('The organisation SMTP AUTH setting could not be read.') }
foreach ($mailbox in $mailboxes) {
    $id = GuidOf (TextOf $mailbox 'ExchangeGuid')
    if ($null -eq $id) {
        $sectionStatus = 'Partial'
        if (-not $reasons.Contains('Mailboxes without an exact non-empty ExchangeGuid were excluded.')) { $reasons.Add('Mailboxes without an exact non-empty ExchangeGuid were excluded.') }
        continue
    }
    $problems = [System.Collections.Generic.List[object]]::new()
    $row = [ordered]@{ id = $id; name = (TextOf $mailbox 'DisplayName'); primarySmtpAddress = (TextOf $mailbox 'PrimarySmtpAddress'); readStatus = 'Collected'; error = $null }
    $settings = $null
    $objectId = GuidOf (TextOf $mailbox 'ExternalDirectoryObjectId')
    if ($null -eq $objectId) { $problems.Add('The mailbox did not return an exact Entra object ID for the protocol read.') }
    else {
        try {
            $matches = @(Use-BditRead -Command 'Get-CASMailbox' -Parameters @{ Identity = $id })
            if ($matches.Count -eq 1 -and (GuidOf (TextOf $matches[0] 'ExternalDirectoryObjectId')) -ceq $objectId) { $settings = $matches[0] }
            else { $problems.Add('The protocol read did not return the exact expected Entra object identity.') }
        } catch { $problems.Add('The mailbox protocol settings could not be read.') }
    }
    FlagOf $settings $row $problems 'PopEnabled' 'popEnabled'
    FlagOf $settings $row $problems 'ImapEnabled' 'imapEnabled'
    FlagOf $settings $row $problems 'MAPIEnabled' 'mapiEnabled'
    FlagOf $settings $row $problems 'EwsEnabled' 'ewsEnabled'
    FlagOf $settings $row $problems 'ActiveSyncEnabled' 'activeSyncEnabled'
    FlagOf $settings $row $problems 'OWAEnabled' 'owaEnabled'
    $row.smtpAuthOverrideReadStatus = 'Failed'; $row.smtpAuthOverride = 'Unknown'
    $p = $null
    if ($null -ne $settings) { $p = $settings.PSObject.Properties['SmtpClientAuthenticationDisabled'] }
    if ($null -ne $p) {
        if ($null -eq $p.Value) { $row.smtpAuthOverrideReadStatus = 'Collected'; $row.smtpAuthOverride = 'Inherit' }
        elseif ($p.Value -is [bool]) { $row.smtpAuthOverrideReadStatus = 'Collected'; $row.smtpAuthOverride = 'Enabled'; if ($p.Value) { $row.smtpAuthOverride = 'Disabled' } }
    }
    if ($row.smtpAuthOverride -eq 'Unknown') { $problems.Add('The mailbox SMTP AUTH override was absent or malformed; inheritance is unknown.') }
    $row.organisationSmtpAuthDisabledReadStatus = 'Failed'; $row.organisationSmtpAuthDisabled = $organisation
    if ($null -ne $organisation) { $row.organisationSmtpAuthDisabledReadStatus = 'Collected' }
    else { $problems.Add('The organisation SMTP AUTH setting was not returned as a boolean.') }
    $row.effectiveSmtpAuthDisabledReadStatus = 'Failed'; $row.effectiveSmtpAuthDisabled = $null
    if ($row.smtpAuthOverride -eq 'Enabled') { $row.effectiveSmtpAuthDisabled = $false }
    elseif ($row.smtpAuthOverride -eq 'Disabled') { $row.effectiveSmtpAuthDisabled = $true }
    elseif ($row.smtpAuthOverride -eq 'Inherit') { $row.effectiveSmtpAuthDisabled = $organisation }
    if ($null -ne $row.effectiveSmtpAuthDisabled) { $row.effectiveSmtpAuthDisabledReadStatus = 'Collected' }
    if ($problems.Count -gt 0) {
        $row.readStatus = 'Partial'; $row.error = [string]::Join(' ', $problems.ToArray()); $sectionStatus = 'Partial'
        if (-not $reasons.Contains('Some mailbox settings could not be read; inspect each row.')) { $reasons.Add('Some mailbox settings could not be read; inspect each row.') }
    }
    $rows.Add($row)
}
$errorText = $null
if ($reasons.Count -gt 0) { $errorText = [string]::Join(' ', $reasons.ToArray()) }
@{ Sections = @(@{ Id = 'protocols'; Status = $sectionStatus; Error = $errorText; Rows = $rows.ToArray() }) }
