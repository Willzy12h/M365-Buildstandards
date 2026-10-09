<#
Inbound and outbound mail connectors with the domains, smart hosts or IP addresses they cover and their TLS settings.
Read only. A true or false value Exchange did not return is shown as Unknown, never as False. A TLS setting that was
returned empty is shown as NotSet; what Exchange does when none is set is not inferred here.
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

# The value as returned, NotSet when it was returned empty, Unknown when it was not returned.
function Get-Setting([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $text = Get-Text $Item.$Name
    if ($text -eq '') { return 'NotSet' }
    return $text
}

function Get-ConnectorRow([object]$Connector, [string]$Direction, [string]$Domains, [string]$Hosts, [string]$Tls, [string]$Certificate) {
    $enabled = Get-Flag $Connector 'Enabled'
    if ($OnlyEnabled -and $enabled -eq 'False') { return }
    $notes = @()
    if ($enabled -eq 'Unknown') { $notes += 'Exchange did not return whether this connector is enabled.' }
    [pscustomobject]@{
        Direction = $Direction
        Name = Get-Text (Get-Value $Connector 'Name')
        Enabled = $enabled
        ConnectorType = Get-Setting $Connector 'ConnectorType'
        ConnectorSource = Get-Setting $Connector 'ConnectorSource'
        Domains = $Domains
        SmartHostsOrIPs = $Hosts
        TlsRequirement = $Tls
        TlsCertificateName = $Certificate
        TransportRuleScoped = Get-Flag $Connector 'IsTransportRuleScoped'
        Comment = Get-Text (Get-Value $Connector 'Comment')
        WhenChanged = Get-Text (Get-Value $Connector 'WhenChanged')
        Notes = $notes -join ' '
    }
}

foreach ($connector in @(Get-InboundConnector)) {
    $tls = 'Unknown'
    $require = Get-Flag $connector 'RequireTls'
    if ($require -eq 'True') { $tls = 'Required' }
    if ($require -eq 'False') { $tls = 'NotRequired' }
    $row = Get-ConnectorRow $connector 'Inbound' (Get-Setting $connector 'SenderDomains') (Get-Setting $connector 'SenderIPAddresses') $tls (Get-Setting $connector 'TlsSenderCertificateName')
    if ($null -ne $row -and (Test-UnknownValue $row)) { $unknownRows++ }
    $row
}
foreach ($connector in @(Get-OutboundConnector)) {
    $hosts = Get-Setting $connector 'SmartHosts'
    if ((Get-Flag $connector 'UseMXRecord') -eq 'True') { $hosts = 'MX record' }
    $row = Get-ConnectorRow $connector 'Outbound' (Get-Setting $connector 'RecipientDomains') $hosts (Get-Setting $connector 'TlsSettings') (Get-Setting $connector 'TlsDomain')
    if ($null -ne $row -and (Test-UnknownValue $row)) { $unknownRows++ }
    $row
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
