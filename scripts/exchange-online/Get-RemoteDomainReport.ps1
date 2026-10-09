<#
Remote domains: whether automatic forwarding, automatic replies, delivery reports and non-delivery reports are allowed
to each remote domain, its TNEF and out-of-office settings and character sets.
Read only. A true or false value Exchange did not return is shown as Unknown, never as False. A setting returned empty
is NotSet. TNEFEnabled returned empty (null) means Exchange leaves TNEF to the sender's Outlook settings, so it is
shown as FollowsClient; a TNEFEnabled that was not returned at all is Unknown. The Default remote domain (*) applies
to every domain no other remote domain names. Outbound spam policy and transport rules can also block forwarding;
they are not read here.
#>
param(
    [switch]$OnlyAutoForwardAllowed
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

foreach ($domain in @(Get-RemoteDomain)) {
    $notes = @()
    $name = Get-Text (Get-Value $domain 'Name')
    $domainName = Get-Text (Get-Value $domain 'DomainName')
    if (-not $domainName) { $domainName = 'Unknown'; $notes += 'Exchange did not return the domain name this remote domain applies to.' }
    $autoForward = Get-Flag $domain 'AutoForwardEnabled'
    if ($OnlyAutoForwardAllowed -and $autoForward -eq 'False') { continue }
    $flags = @{}
    $missing = @()
    foreach ($flag in @('AutoForwardEnabled', 'AutoReplyEnabled', 'DeliveryReportEnabled', 'NDREnabled')) {
        $flags[$flag] = Get-Flag $domain $flag
        if ($flags[$flag] -eq 'Unknown') { $missing += $flag }
    }
    if ($missing.Count -gt 0) { $notes += ('Exchange did not return ' + ($missing -join ', ') + '.') }
    $tnef = 'Unknown'
    if ($null -ne $domain -and $domain.PSObject.Properties['TNEFEnabled']) {
        $raw = $domain.TNEFEnabled
        if ($null -eq $raw -or [string]$raw -eq '') { $tnef = 'FollowsClient' } else { $tnef = Get-Flag $domain 'TNEFEnabled' }
    }
    if ($tnef -eq 'Unknown') { $notes += 'Exchange did not return TNEFEnabled.' }
    $row = [pscustomobject]@{
        Name = $name
        DomainName = $domainName
        AutoForwardEnabled = $flags['AutoForwardEnabled']
        AutoReplyEnabled = $flags['AutoReplyEnabled']
        AllowedOOFType = Get-Setting $domain 'AllowedOOFType'
        DeliveryReportEnabled = $flags['DeliveryReportEnabled']
        NDREnabled = $flags['NDREnabled']
        TNEFEnabled = $tnef
        CharacterSet = Get-Setting $domain 'CharacterSet'
        NonMimeCharacterSet = Get-Setting $domain 'NonMimeCharacterSet'
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
