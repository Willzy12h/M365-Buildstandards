<#
Accepted domains with their DKIM signing configuration as Exchange Online holds it.
Read only. No DNS query is made: the selector CNAME values are what Exchange expects to be published, not proof that
they are. A domain with no DKIM signing configuration is listed with DkimConfigured False and DkimEnabled
NotConfigured. A true or false value Exchange did not return is shown as Unknown, never as False. When Exchange
returns a signing configuration without its domain, a domain with no matched configuration is Unknown, not False.
#>
param(
    [string[]]$Domain
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

$accepted = @(Get-AcceptedDomain)
$signing = @(Get-DkimSigningConfig)
$wanted = @($Domain | Where-Object { $_ } | ForEach-Object { ([string]$_).ToLowerInvariant() })
# A signing configuration returned without its domain cannot be matched, so no domain can be said to have none.
$unidentified = @($signing | Where-Object { -not (Get-Text (Get-Value $_ 'Domain')) }).Count

foreach ($item in $accepted) {
    $name = (Get-Text (Get-Value $item 'DomainName')).ToLowerInvariant()
    if ($wanted.Count -gt 0 -and $wanted -notcontains $name) { continue }
    $configs = @($signing | Where-Object { (Get-Text (Get-Value $_ 'Domain')).ToLowerInvariant() -eq $name })
    $notes = @()
    $config = $null
    $configured = 'False'
    if ($configs.Count -ge 1) { $configured = 'True'; $config = $configs[0] }
    if ($configs.Count -gt 1) { $notes += 'Exchange returned more than one DKIM signing configuration for this domain; the first is shown.' }
    $enabled = 'NotConfigured'
    if ($null -ne $config) { $enabled = Get-Flag $config 'Enabled' }
    elseif ($unidentified -gt 0) {
        $configured = 'Unknown'; $enabled = 'Unknown'
        $notes += ('Exchange returned ' + $unidentified + ' DKIM signing configuration(s) without a domain, so whether one of them is for this domain is unknown.')
    }
    else { $notes += 'Exchange returned no DKIM signing configuration for this domain.' }
    if ($enabled -eq 'Unknown') { $notes += 'Exchange did not return whether DKIM signing is enabled.' }
    $row = [pscustomobject]@{
        Domain = $name
        DomainType = Get-Text (Get-Value $item 'DomainType')
        IsDefault = Get-Flag $item 'Default'
        DkimConfigured = $configured
        DkimEnabled = $enabled
        DkimStatus = Get-Text (Get-Value $config 'Status')
        Selector1CNAME = Get-Text (Get-Value $config 'Selector1CNAME')
        Selector2CNAME = Get-Text (Get-Value $config 'Selector2CNAME')
        Selector1KeySize = Get-Text (Get-Value $config 'Selector1KeySize')
        RotateOnDate = Get-Text (Get-Value $config 'RotateOnDate')
        LastChecked = Get-Text (Get-Value $config 'LastChecked')
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
foreach ($name in $wanted) {
    if (@($accepted | Where-Object { (Get-Text (Get-Value $_ 'DomainName')).ToLowerInvariant() -eq $name }).Count -gt 0) { continue }
    $row = [pscustomobject]@{
        Domain = $name; DomainType = 'NotAccepted'; IsDefault = 'False'; DkimConfigured = 'NotApplicable'; DkimEnabled = 'NotApplicable'; DkimStatus = ''
        Selector1CNAME = ''; Selector2CNAME = ''; Selector1KeySize = ''; RotateOnDate = ''; LastChecked = ''
        Notes = 'This domain is not an accepted domain in this tenant, so its DKIM signing configuration was not read.'
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
