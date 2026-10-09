<#
Calendar and contact sharing: sharing policies with each domain and the sharing level they allow, and organisation
relationships with their domains, whether they are enabled and the free/busy level they share.
Read only. A true or false value Exchange did not return is shown as Unknown, never as False. A list returned empty is
NotSet; one that was not returned, or was returned null, is Unknown. A sharing policy domain entry is split at its first
colon into the domain and the sharing actions; an entry without a colon is shown as returned with Access Unknown.
Which mailboxes use each sharing policy is not read here. Individual mailbox calendar permissions are a separate item.
#>
param(
    [string[]]$Kind = @('SharingPolicy', 'OrganizationRelationship')
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

function Get-SharingRow([string]$RowKind, [object]$Item, [string]$IsDefault, [string]$Domain, [string]$Access, [string]$FreeBusyEnabled, [string]$FreeBusyLevel, [string[]]$Notes) {
    $notes = @($Notes)
    $name = Get-Text (Get-Value $Item 'Name')
    if (-not $name) { $name = 'Unknown'; $notes += 'Exchange did not return the name.' }
    $enabled = Get-Flag $Item 'Enabled'
    if ($enabled -eq 'Unknown') { $notes += 'Exchange did not return whether this is enabled.' }
    [pscustomobject]@{
        Kind = $RowKind
        Name = $name
        Enabled = $enabled
        IsDefault = $IsDefault
        Domain = $Domain
        Access = $Access
        FreeBusyAccessEnabled = $FreeBusyEnabled
        FreeBusyAccessLevel = $FreeBusyLevel
        Notes = $notes -join ' '
    }
}

if ($Kind -contains 'SharingPolicy') {
    foreach ($policy in @(Get-SharingPolicy)) {
        $isDefault = Get-Flag $policy 'Default'
        $entries = $null
        if ($null -ne $policy -and $policy.PSObject.Properties['Domains']) { $entries = $policy.Domains }
        $list = @()
        if ($null -ne $entries) { $list = @(@($entries) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ } | Where-Object { $_ -ne '' }) }
        $rows = @()
        if ($false) {
            $rows += Get-SharingRow 'SharingPolicy' $policy $isDefault 'Unknown' 'Unknown' 'NotApplicable' 'NotApplicable' @('Exchange did not return the domains this policy shares with.')
        } elseif ($list.Count -eq 0) {
            $rows += Get-SharingRow 'SharingPolicy' $policy $isDefault 'NotSet' 'NotSet' 'NotApplicable' 'NotApplicable' @('Exchange returned no domains for this policy.')
        } else {
            foreach ($entry in $list) {
                $at = $entry.IndexOf(':')
                if ($at -gt 0) {
                    $rows += Get-SharingRow 'SharingPolicy' $policy $isDefault $entry.Substring(0, $at).Trim() ($entry.Substring($at + 1).Trim() -replace '\s+', ' ') 'NotApplicable' 'NotApplicable' @()
                } else {
                    $rows += Get-SharingRow 'SharingPolicy' $policy $isDefault $entry 'Unknown' 'NotApplicable' 'NotApplicable' @('This domain entry has no sharing actions in the expected domain:actions form; it is shown as returned.')
                }
            }
        }
        foreach ($row in $rows) {
            if (Test-UnknownValue $row) { $unknownRows++ }
            $row
        }
    }
}
if ($Kind -contains 'OrganizationRelationship') {
    foreach ($relationship in @(Get-OrganizationRelationship)) {
        $notes = @()
        $domains = Get-Setting $relationship 'DomainNames'
        if ($domains -eq 'Unknown') { $notes += 'Exchange did not return the domains of this relationship.' }
        $freeBusy = Get-Flag $relationship 'FreeBusyAccessEnabled'
        if ($freeBusy -eq 'Unknown') { $notes += 'Exchange did not return whether free/busy access is enabled.' }
        $level = Get-Setting $relationship 'FreeBusyAccessLevel'
        if ($level -eq 'Unknown') { $notes += 'Exchange did not return the free/busy access level.' }
        $row = Get-SharingRow 'OrganizationRelationship' $relationship 'NotApplicable' $domains 'NotApplicable' $freeBusy $level $notes
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
