<#
Journal rules: each rule's scope, the recipient it journals, the journal mailbox it sends reports to, and whether it is
enabled.
Read only. No journal rules is a valid result: the run returns no rows and no warning. A true or false value Exchange
did not return is shown as Unknown, never as False. A rule's recipient returned empty (null) means the rule journals
messages for every recipient, so it is shown as AllRecipients; a recipient that was not returned at all is Unknown.
A journal address is shown as Exchange returned it; whether that mailbox accepts journal reports is not checked.
#>
param(
    [switch]$OnlyEnabled
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# True when any column holds Unknown, alone or as a name=Unknown setting, so the run can say what was not measured.
function Test-UnknownValue([object]$Row) {
    foreach ($property in @($Row.PSObject.Properties)) {
        if ([string]$property.Value -cmatch '(^|=)Unknown(;|$)') { return $true }
    }
    return $false
}
$unknownRows = 0

function Get-Value([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
    return $null
}

function Get-Text([object]$Value) {
    if ($null -eq $Value) { return '' }
    return ((@($Value) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ }) -join '; ') -replace '\s+', ' '
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

# The value as returned; NotSet when it was returned empty; Unknown when it was not returned or was returned null.
function Get-Setting([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name] -or $null -eq $Item.$Name) { return 'Unknown' }
    $text = Get-Text $Item.$Name
    if ($text -eq '') { return 'NotSet' }
    return $text
}

foreach ($rule in @(Get-JournalRule)) {
    $enabled = Get-Flag $rule 'Enabled'
    if ($OnlyEnabled -and $enabled -eq 'False') { continue }
    $notes = @()
    if ($enabled -eq 'Unknown') { $notes += 'Exchange did not return whether this rule is enabled.' }
    $name = Get-Text (Get-Value $rule 'Name')
    if (-not $name) { $name = 'Unknown'; $notes += 'Exchange did not return the rule name.' }
    $scope = Get-Setting $rule 'Scope'
    if ($scope -eq 'Unknown') { $notes += 'Exchange did not return the rule scope.' }
    # Null is a returned value with a documented meaning here: the rule is not limited to one recipient.
    $recipient = 'Unknown'
    if ($null -ne $rule -and $rule.PSObject.Properties['Recipient']) {
        $recipient = Get-Text $rule.Recipient
        if (-not $recipient) { $recipient = 'AllRecipients' }
    }
    if ($recipient -eq 'Unknown') { $notes += 'Exchange did not return the recipient this rule journals.' }
    $journal = Get-Setting $rule 'JournalEmailAddress'
    if ($journal -eq 'Unknown') { $notes += 'Exchange did not return the journal address this rule sends reports to.' }
    if ($journal -eq 'NotSet') { $notes += 'Exchange returned an empty journal address for this rule.' }
    $row = [pscustomobject]@{
        Name = $name
        Enabled = $enabled
        Scope = $scope
        Recipient = $recipient
        JournalEmailAddress = $journal
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
