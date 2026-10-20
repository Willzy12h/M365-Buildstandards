# INT-128/131: explicitly selected, separately observed mailbox permission sources. No effective-access calculation.
param([string]$MailboxGuids, [bool]$FullAccess = $false, [bool]$SendAs = $false, [bool]$SendOnBehalf = $false)
Set-StrictMode -Version Latest
function TextOf($Item, [string]$Name, [bool]$AllowBlank = $false) {
    $p = $Item.PSObject.Properties[$Name]
    if ($null -eq $p -or $p.Value -isnot [string] -or $p.Value.Length -gt 1024 -or $p.Value -match '[\x00-\x1F\x7F]') { return $null }
    if (-not $AllowBlank -and [string]::IsNullOrWhiteSpace($p.Value)) { return $null }
    return $p.Value
}
function GuidOf($Item, [string]$Name) {
    $p = $Item.PSObject.Properties[$Name]
    if ($null -eq $p -or ($p.Value -isnot [guid] -and $p.Value -isnot [string])) { return $null }
    try { $g = [guid]$p.Value; if ($g -eq [guid]::Empty -or ([string]$p.Value).Length -ne 36) { return $null }; return $g.ToString('D') } catch { return $null }
}
function Unknown([string]$State, [string]$Reason) { return @{ state = $State; value = $null; reason = $Reason } }
function PrincipalOf($Value) {
    if ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value)) {
        return @{ namespace = 'ReturnedText'; value = $Value; externalDirectoryObjectId = $null }
    }
    if ($null -eq $Value) { return $null }
    $source = TextOf $Value 'Source'; $text = TextOf $Value 'Value'
    if ($null -eq $text -or @('RawIdentity','DistinguishedName','SpecialPrincipal','UnresolvedDisplayLabel') -cnotcontains $source) { return $null }
    return @{ namespace = $source; value = $text; externalDirectoryObjectId = $null }
}
function ValueOf($Item, [string]$Property, [bool]$IsRights = $false) {
    $p = $Item.PSObject.Properties[$Property]
    if ($null -eq $p) { return (Unknown 'Missing' ($Property + ' was not returned.')) }
    if ($null -eq $p.Value) { return (Unknown 'ReturnedNull' ($Property + ' returned null.')) }
    if (-not $IsRights -and $p.Value -is [bool]) { return @{ state = 'Value'; value = $p.Value; reason = $null } }
    if ($IsRights -and $p.Value -is [array]) {
        foreach ($right in $p.Value) {
            if ($right -isnot [string] -or [string]::IsNullOrWhiteSpace($right)) { return (Unknown 'Unsupported' 'AccessRights contains an unsupported value.') }
        }
        return @{ state = 'Value'; value = $p.Value; reason = $null }
    }
    return (Unknown 'Unsupported' ($Property + ' returned an unsupported type.'))
}
$choices = @{ FullAccess = $FullAccess; SendAs = $SendAs; SendOnBehalf = $SendOnBehalf }
$targets = [Collections.Generic.List[object]]::new(); $edges = [Collections.Generic.List[object]]::new()
foreach ($id in $MailboxGuids.Split(',')) {
    $reads = [Collections.Generic.List[object]]::new()
    foreach ($kind in @('FullAccess','SendAs','SendOnBehalf')) {
        $reads.Add([ordered]@{ kind = $kind; selected = $choices[$kind]; status = 'NotAttempted'; error = 'Not selected or read not attempted.'; returnedEntries = $null; excludedEntries = $null })
    }
    $targets.Add([ordered]@{ id = $null; name = $null; readStatus = 'NotAttempted'; error = 'Mailbox lookup not attempted.';
        requestedExchangeGuid = $id; observedExchangeGuid = $null; externalDirectoryObjectId = $null; primarySmtpAddress = $null; lookupReadStatus = 'NotAttempted'; permissionReads = $reads.ToArray() })
}
foreach ($target in $targets) {
    $id = $target.requestedExchangeGuid
    try {
        $mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ Identity = $id })
        if ($mailboxes.Count -ne 1 -or (GuidOf $mailboxes[0] 'ExchangeGuid') -cne $id) { throw 'Unverified mailbox lookup' }
        $mailbox = $mailboxes[0]; $target.observedExchangeGuid = $id; $target.lookupReadStatus = 'Collected'
        $target.name = TextOf $mailbox 'DisplayName' $true; $target.externalDirectoryObjectId = GuidOf $mailbox 'ExternalDirectoryObjectId'
        $target.primarySmtpAddress = TextOf $mailbox 'PrimarySmtpAddress'; $target.readStatus = 'Collected'; $target.error = $null
    } catch {
        $target.lookupReadStatus = 'Failed'; $target.readStatus = 'Failed'
        $target.error = 'Exact bounded mailbox lookup failed; no permission source was admitted.'
        continue
    }
    foreach ($read in $target.permissionReads) {
        if (-not $read.selected) { continue }
        $batch = [Collections.Generic.List[object]]::new()
        try {
            if ($read.kind -ceq 'FullAccess') {
                $source = @(Use-BditRead -Command 'Get-EXOMailboxPermission' -Parameters @{ Identity = $id })
            } elseif ($read.kind -ceq 'SendAs') {
                $source = @(Use-BditRead -Command 'Get-EXORecipientPermission' -Parameters @{ Identity = $id })
            } else {
                $p = $mailbox.PSObject.Properties['GrantSendOnBehalfTo']
                if ($null -eq $p -or $p.Value -isnot [array]) { throw 'The selected attribute was not returned as a list' }
                $source = $p.Value
            }
            $read.status = 'Collected'; $read.error = $null; $read.returnedEntries = $source.Count; $read.excludedEntries = 0
            foreach ($item in $source) {
                $principalValue = $item
                if ($read.kind -cne 'SendOnBehalf') {
                    $property = if ($read.kind -ceq 'FullAccess') { 'User' } else { 'Trustee' }
                    $p = $item.PSObject.Properties[$property]; $principalValue = $null
                    if ($null -ne $p) { $principalValue = $p.Value }
                }
                $principal = PrincipalOf $principalValue
                if ($null -eq $principal) {
                    $read.excludedEntries++; $read.status = 'Partial'; $read.error = 'Some entries lacked an admissible original principal; counted exclusions are not an empty successful inventory.'
                    continue
                }
                $rights = Unknown 'Unsupported' 'GrantSendOnBehalfTo does not expose ACL rights.'
                $inherited = Unknown 'Unsupported' 'GrantSendOnBehalfTo does not expose ACL inheritance.'
                $deny = Unknown 'Unsupported' 'GrantSendOnBehalfTo does not expose an ACL deny flag.'
                if ($read.kind -cne 'SendOnBehalf') {
                    $rights = ValueOf $item 'AccessRights' $true; $inherited = ValueOf $item 'IsInherited'; $deny = ValueOf $item 'Deny'
                }
                $row = [ordered]@{ id = $null; name = $null; readStatus = 'Collected'; error = $null; mailboxExchangeGuid = $id;
                    permissionKind = $read.kind; principal = $principal; rights = $rights; isInherited = $inherited; deny = $deny }
                if ($principal.namespace -ceq 'UnresolvedDisplayLabel' -or $rights.state -cne 'Value' -or $inherited.state -cne 'Value' -or $deny.state -cne 'Value') {
                    $row.readStatus = 'Partial'; $row.error = 'Unresolved principal or unavailable ACL fields; inspect each source state. This is not an effective-access verdict.'
                    $read.status = 'Partial'; $read.error = 'Some source values are unavailable or not exposed by the selected source; no false flags or effective rights are inferred.'
                }
                $batch.Add($row)
            }
            foreach ($row in $batch) { $edges.Add($row) }
        } catch {
            $read.status = 'Failed'; $read.error = 'This bounded permission source failed, stopped or reached its limit; returned counts are unknown.'
            $read.returnedEntries = $null; $read.excludedEntries = $null
        }
        if ($read.status -cne 'Collected') { $target.readStatus = 'Partial'; $target.error = 'One or more selected permission sources are incomplete; inspect separate source diagnostics.' }
    }
}
$status = 'Collected'; $reason = $null
foreach ($target in $targets) { if ($target.readStatus -cne 'Collected') { $status = 'Partial'; $reason = 'Selected permission sources are incomplete; failed/unselected sources and unavailable values are not empty-success or effective-access claims.' } }
@{ Sections = @(
    @{ Id = 'target-reads'; Status = $status; Error = $reason; Rows = $targets.ToArray() },
    @{ Id = 'permissions'; Status = $status; Error = $reason; Rows = $edges.ToArray() }
) }
