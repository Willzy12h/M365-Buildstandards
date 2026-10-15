<#
Mail flow (transport) rules in priority order, with their state, mode and the actions that most often need review:
redirecting or copying mail, routing through a connector and bypassing spam filtering. BypassesSpamFiltering is True
only for the bypass spam filtering action (spam confidence level -1); a bypass made another way is not detected.
Read only. A true or false value Exchange did not return is shown as Unknown, never as False; with only enabled rules
ticked, a rule whose state is Unknown is kept.
#>
param(
    [switch]$OnlyEnabled,
    [int]$MaxRows = 1000
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

# True when any named action holds a value; False only when every one was returned and empty; otherwise Unknown.
function Get-AnySet([object]$Item, [string[]]$Names) {
    $missing = $false
    foreach ($name in $Names) {
        if ($null -eq $Item -or -not $Item.PSObject.Properties[$name]) { $missing = $true; continue }
        if ((Get-Text $Item.$name) -ne '') { return 'True' }
    }
    if ($missing) { return 'Unknown' }
    return 'False'
}

$rules = @(Get-TransportRule -ResultSize ($MaxRows + 1))
if ($rules.Count -gt $MaxRows) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxRows + ' transport rules exist; only the first ' + $MaxRows + ' are listed.')
    $rules = @($rules | Select-Object -First $MaxRows)
}

foreach ($rule in $rules) {
    $state = Get-Text (Get-Value $rule 'State')
    $notes = @()
    if (-not $state) { $state = 'Unknown'; $notes += 'Exchange did not return whether this rule is enabled.' }
    if ($OnlyEnabled -and $state -eq 'Disabled') { continue }
    $bypass = 'Unknown'
    if ($rule.PSObject.Properties['SetSCL']) {
        $bypass = 'False'
        if ((Get-Text $rule.SetSCL) -eq '-1') { $bypass = 'True' }
    }
    $copies = Get-AnySet $rule @('RedirectMessageTo', 'BlindCopyTo', 'AddToRecipients', 'CopyTo')
    if ($copies -eq 'Unknown') { $notes += 'Exchange did not return every redirect and copy action.' }
    $row = [pscustomobject]@{
        Priority = Get-Text (Get-Value $rule 'Priority')
        Name = Get-Text (Get-Value $rule 'Name')
        State = $state
        Mode = Get-Text (Get-Value $rule 'Mode')
        RedirectsOrCopies = $copies
        RoutesViaConnector = Get-AnySet $rule @('RouteMessageOutboundConnector')
        BypassesSpamFiltering = $bypass
        StopRuleProcessing = Get-Flag $rule 'StopRuleProcessing'
        ActivationDate = Get-Text (Get-Value $rule 'ActivationDate')
        ExpiryDate = Get-Text (Get-Value $rule 'ExpiryDate')
        Description = Get-Text (Get-Value $rule 'Description')
        Comments = Get-Text (Get-Value $rule 'Comments')
        WhenChanged = Get-Text (Get-Value $rule 'WhenChanged')
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
