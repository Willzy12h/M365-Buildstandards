<#
Client access per mailbox: POP, IMAP, MAPI, EWS, ActiveSync, Outlook on the web and SMTP client authentication, with
the organisation's SMTP AUTH setting.
Read only. A true or false value Exchange did not return is shown as Unknown, never as False. A mailbox's SMTP client
authentication setting returned empty (null) means the mailbox follows the organisation setting, so it is shown as
FollowsOrganisation; a setting that was not returned at all is Unknown. EffectiveSmtpClientAuth is worked out only from
returned values. Authentication policies and Conditional Access, which can also block a protocol, are not read.
#>
param(
    [string[]]$Mailbox,
    [int]$MaxMailboxes = 2000
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

# The organisation setting: True means SMTP client authentication is turned off for every mailbox that does not override it.
$transport = Get-TransportConfig
$orgSmtpDisabled = Get-Flag $transport 'SmtpClientAuthenticationDisabled'

$protocols = @('PopEnabled', 'ImapEnabled', 'MAPIEnabled', 'EwsEnabled', 'ActiveSyncEnabled', 'OWAEnabled')
$properties = $protocols + @('SmtpClientAuthenticationDisabled')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOCASMailbox -Identity $identity -Properties $properties })
} else {
    # One more than the limit is asked for, so a longer list is reported as partial rather than cut short silently.
    $targets = @(Get-EXOCASMailbox -ResultSize ($MaxMailboxes + 1) -Properties $properties)
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
}

foreach ($target in $targets) {
    $notes = @()
    $address = Get-Text (Get-Value $target 'PrimarySmtpAddress')
    if (-not $address) { $address = 'Unknown'; $notes += 'Exchange did not return the primary SMTP address for this mailbox.' }
    $flags = @{}
    $missing = @()
    foreach ($protocol in $protocols) {
        $flags[$protocol] = Get-Flag $target $protocol
        if ($flags[$protocol] -eq 'Unknown') { $missing += $protocol }
    }
    if ($missing.Count -gt 0) { $notes += ('Exchange did not return ' + ($missing -join ', ') + '.') }
    # Null is a returned value with a documented meaning here: the mailbox has no setting of its own.
    $smtp = 'Unknown'
    if ($null -ne $target -and $target.PSObject.Properties['SmtpClientAuthenticationDisabled']) {
        $raw = $target.SmtpClientAuthenticationDisabled
        if ($null -eq $raw -or [string]$raw -eq '') { $smtp = 'FollowsOrganisation' }
        else { $smtp = Get-Flag $target 'SmtpClientAuthenticationDisabled' }
    }
    if ($smtp -eq 'Unknown') { $notes += 'Exchange did not return SmtpClientAuthenticationDisabled for this mailbox.' }
    $effective = 'Unknown'
    if ($smtp -eq 'True') { $effective = 'Disabled' }
    if ($smtp -eq 'False') { $effective = 'Enabled' }
    if ($smtp -eq 'FollowsOrganisation') {
        if ($orgSmtpDisabled -eq 'True') { $effective = 'Disabled' }
        if ($orgSmtpDisabled -eq 'False') { $effective = 'Enabled' }
        if ($orgSmtpDisabled -eq 'Unknown') { $notes += 'The mailbox follows the organisation SMTP AUTH setting, which could not be read.' }
    }
    $row = [pscustomobject]@{
        Mailbox = $address
        DisplayName = Get-Text (Get-Value $target 'DisplayName')
        PopEnabled = $flags['PopEnabled']
        ImapEnabled = $flags['ImapEnabled']
        MapiEnabled = $flags['MAPIEnabled']
        EwsEnabled = $flags['EwsEnabled']
        ActiveSyncEnabled = $flags['ActiveSyncEnabled']
        OwaEnabled = $flags['OWAEnabled']
        SmtpClientAuthDisabled = $smtp
        OrganisationSmtpClientAuthDisabled = $orgSmtpDisabled
        EffectiveSmtpClientAuth = $effective
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($orgSmtpDisabled -eq 'Unknown') {
    Write-Warning 'BDIT:UNKNOWN The organisation SMTP AUTH setting (SmtpClientAuthenticationDisabled) could not be read. OrganisationSmtpClientAuthDisabled is Unknown on every row.'
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
