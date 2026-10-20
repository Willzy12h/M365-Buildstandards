# INT-128/131: exact selected mailbox GUIDs, typed observations and explicit per-target diagnostics.
# Engine-owned Run only. All source access passes through the private, bounded read gate.
param([string]$MailboxGuids, [bool]$IncludeHidden = $false)
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
    try {
        $g = [guid]$p.Value
        if ($g -eq [guid]::Empty -or ([string]$p.Value).Length -ne 36) { return $null }
        return $g.ToString('D')
    } catch { return $null }
}
function Unknown([string]$State, [string]$Reason) { return @{ state = $State; value = $null; reason = $Reason } }
function ValueOf($Item, [string]$Property, [string]$Kind) {
    $p = $Item.PSObject.Properties[$Property]
    if ($null -eq $p) { return (Unknown 'Missing' ($Property + ' was not returned.')) }
    if ($null -eq $p.Value) { return (Unknown 'ReturnedNull' ($Property + ' returned null.')) }
    $v = $p.Value
    if ($Kind -ceq 'Flag') {
        if ($v -is [bool]) { return @{ state = 'Value'; value = $v; reason = $null } }
    } elseif ($Kind -ceq 'Priority') {
        if (($v -is [int] -or $v -is [long]) -and $v -ge 0 -and $v -le 2147483647) {
            return @{ state = 'Value'; value = $v; reason = $null }
        }
    } elseif ($Kind -ceq 'References' -and $v -is [array]) {
        $references = [Collections.Generic.List[object]]::new()
        foreach ($reference in $v) {
            if ($reference -is [string] -and -not [string]::IsNullOrWhiteSpace($reference)) {
                $references.Add(@{ namespace = 'ReturnedText'; value = $reference; externalDirectoryObjectId = $null })
            } elseif ($null -ne $reference) {
                $source = TextOf $reference 'Source'; $text = TextOf $reference 'Value'
                if ($null -eq $text -or @('RawIdentity','DistinguishedName','SpecialPrincipal','UnresolvedDisplayLabel') -cnotcontains $source) {
                    return (Unknown 'Unsupported' ($Property + ' contains an unsupported reference.'))
                }
                $references.Add(@{ namespace = $source; value = $text; externalDirectoryObjectId = $null })
            } else { return (Unknown 'Unsupported' ($Property + ' contains a null reference.')) }
        }
        return @{ state = 'Value'; value = $references.ToArray(); reason = $null }
    }
    return (Unknown 'Unsupported' ($Property + ' returned an unsupported type.'))
}
$targets = [Collections.Generic.List[object]]::new()
$rules = [Collections.Generic.List[object]]::new()
# Allocate all diagnostic rows before the first source call; a failed later target is never silently absent.
foreach ($id in $MailboxGuids.Split(',')) {
    $targets.Add([ordered]@{ id = $null; name = $null; readStatus = 'NotAttempted'; error = 'Target read not attempted.';
        requestedExchangeGuid = $id; observedExchangeGuid = $null; externalDirectoryObjectId = $null; primarySmtpAddress = $null;
        lookupReadStatus = 'NotAttempted'; childReadStatus = 'NotAttempted'; returnedEntries = $null; excludedEntries = $null })
}
foreach ($target in $targets) {
    $id = $target.requestedExchangeGuid
    try {
        $mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ Identity = $id })
        if ($mailboxes.Count -ne 1 -or (GuidOf $mailboxes[0] 'ExchangeGuid') -cne $id) { throw 'Unverified mailbox lookup' }
        $target.observedExchangeGuid = $id; $target.lookupReadStatus = 'Collected'
        $target.name = TextOf $mailboxes[0] 'DisplayName' $true
        $target.externalDirectoryObjectId = GuidOf $mailboxes[0] 'ExternalDirectoryObjectId'
        $target.primarySmtpAddress = TextOf $mailboxes[0] 'PrimarySmtpAddress'
    } catch {
        $target.lookupReadStatus = 'Failed'; $target.readStatus = 'Failed'
        $target.error = 'Exact mailbox lookup failed or returned an unsupported identity; no child read was admitted.'
        continue
    }
    try {
        $source = @(Use-BditRead -Command 'Get-InboxRule' -Parameters @{ Mailbox = $id; IncludeHidden = $IncludeHidden })
        $target.returnedEntries = $source.Count; $target.excludedEntries = 0
        $target.childReadStatus = 'Collected'; $target.readStatus = 'Collected'; $target.error = $null
        $seen = [Collections.Generic.List[object]]::new()
        foreach ($item in $source) {
            $identity = TextOf $item 'RuleIdentity'
            # Documented UInt64 identities are opaque decimal strings. Never parse a returned string as a number.
            $identityProperty = $item.PSObject.Properties['RuleIdentity']
            if ($null -eq $identity -and $null -ne $identityProperty -and $identityProperty.Value -is [uint64]) {
                $identity = $identityProperty.Value.ToString()
            }
            if ($null -eq $identity -or $seen.Contains($identity)) {
                $target.excludedEntries++; $target.readStatus = 'Partial'; $target.childReadStatus = 'Partial'
                $target.error = 'Some entries lacked an exact unique opaque RuleIdentity; excluded counts are recorded.'
                continue
            }
            $seen.Add($identity)
            $row = [ordered]@{ id = $null; name = (TextOf $item 'Name' $true); readStatus = 'Collected'; error = $null;
                mailboxExchangeGuid = $id; ruleIdentity = $identity; actualIdentityRaw = (TextOf $item 'Identity');
                enabled = (ValueOf $item 'Enabled' 'Flag'); priority = (ValueOf $item 'Priority' 'Priority');
                forwardTo = (ValueOf $item 'ForwardTo' 'References'); forwardAsAttachmentTo = (ValueOf $item 'ForwardAsAttachmentTo' 'References');
                redirectTo = (ValueOf $item 'RedirectTo' 'References'); deleteMessage = (ValueOf $item 'DeleteMessage' 'Flag');
                markAsRead = (ValueOf $item 'MarkAsRead' 'Flag'); stopProcessingRules = (ValueOf $item 'StopProcessingRules' 'Flag') }
            foreach ($column in @('enabled','priority','forwardTo','forwardAsAttachmentTo','redirectTo','deleteMessage','markAsRead','stopProcessingRules')) {
                if ($row[$column].state -cne 'Value') { $row.readStatus = 'Partial' }
            }
            foreach ($column in @('forwardTo','forwardAsAttachmentTo','redirectTo')) {
                if ($row[$column].state -ceq 'Value') {
                    foreach ($reference in $row[$column].value) {
                        if ($reference.namespace -ceq 'UnresolvedDisplayLabel') { $row.readStatus = 'Partial' }
                    }
                }
            }
            if ($row.readStatus -ceq 'Partial') {
                $row.error = 'Some values are unavailable or references unresolved; inspect the individual reported states.'
                $target.readStatus = 'Partial'; $target.childReadStatus = 'Partial'
                $target.error = 'Some rule observations are incomplete; inspect the original value states.'
            }
            $rules.Add($row)
        }
    } catch {
        $target.childReadStatus = 'Failed'; $target.readStatus = 'Failed'; $target.returnedEntries = $null; $target.excludedEntries = $null
        $target.error = 'The bounded child read failed, stopped or reached its limit; this is unknown, not an empty rule inventory.'
    }
}
$status = 'Collected'; $reason = $null
foreach ($target in $targets) { if ($target.readStatus -cne 'Collected') { $status = 'Partial'; $reason = 'One or more selected target reads are incomplete; inspect target diagnostics and value states.' } }
@{ Sections = @(
    @{ Id = 'target-reads'; Status = $status; Error = $reason; Rows = $targets.ToArray() },
    @{ Id = 'rules'; Status = $status; Error = $reason; Rows = $rules.ToArray() }
) }
