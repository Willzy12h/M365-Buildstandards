# INT-128/131: selected distribution groups and direct members, never recursive or effective delivery.
param([string]$GroupGuids)
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
function Setting($Item, [string]$Property, [bool]$Boolean = $false) {
    $p = $Item.PSObject.Properties[$Property]
    if ($null -eq $p) { return (Unknown 'Missing' ($Property + ' was not returned.')) }
    if ($null -eq $p.Value) { return (Unknown 'ReturnedNull' ($Property + ' returned null.')) }
    if ($Boolean -and $p.Value -is [bool]) { return @{ state = 'Value'; value = $p.Value; reason = $null } }
    if (-not $Boolean -and $p.Value -is [string] -and -not [string]::IsNullOrWhiteSpace($p.Value)) { return @{ state = 'Value'; value = $p.Value; reason = $null } }
    return (Unknown 'Unsupported' ($Property + ' returned an unsupported value.'))
}
function OwnersOf($Item) {
    $p = $Item.PSObject.Properties['ManagedBy']
    if ($null -eq $p) { return (Unknown 'Missing' 'ManagedBy was not returned.') }
    if ($null -eq $p.Value) { return (Unknown 'ReturnedNull' 'ManagedBy returned null.') }
    if ($p.Value -isnot [array]) { return (Unknown 'Unsupported' 'ManagedBy was not a supported list.') }
    $owners = [Collections.Generic.List[object]]::new()
    foreach ($value in $p.Value) {
        $owner = PrincipalOf $value
        if ($null -eq $owner -or $owner.namespace -ceq 'SpecialPrincipal') { return (Unknown 'Unsupported' 'A ManagedBy reference had no admissible source identity; owners are unknown, not empty.') }
        $owners.Add($owner)
    }
    return @{ state = 'Value'; value = $owners.ToArray(); reason = $null }
}
$targets = [Collections.Generic.List[object]]::new(); $members = [Collections.Generic.List[object]]::new()
foreach ($id in $GroupGuids.Split(',')) {
    $targets.Add([ordered]@{ id = $null; name = $null; readStatus = 'NotAttempted'; error = 'Group lookup not attempted.';
        requestedGroupGuid = $id; observedGroupGuid = $null; externalDirectoryObjectId = $null; primarySmtpAddress = $null;
        lookupReadStatus = 'NotAttempted'; membershipReadStatus = 'NotAttempted'; returnedEntries = $null; excludedEntries = $null;
        recipientTypeDetails = (Unknown 'NotAttempted' 'Group lookup not completed.'); managedBy = (Unknown 'NotAttempted' 'Group lookup not completed.');
        memberJoinRestriction = (Unknown 'NotAttempted' 'Group lookup not completed.'); memberDepartRestriction = (Unknown 'NotAttempted' 'Group lookup not completed.');
        requireSenderAuthenticationEnabled = (Unknown 'NotAttempted' 'Group lookup not completed.'); hiddenFromAddressListsEnabled = (Unknown 'NotAttempted' 'Group lookup not completed.') })
}
foreach ($target in $targets) {
    $id = $target.requestedGroupGuid; $unknown = $false
    try {
        $groups = @(Use-BditRead -Command 'Get-DistributionGroup' -Parameters @{ Identity = $id })
        if ($groups.Count -ne 1 -or (GuidOf $groups[0] 'Guid') -cne $id) { throw 'Unverified selected group lookup' }
        $group = $groups[0]; $target.observedGroupGuid = $id; $target.lookupReadStatus = 'Collected'; $target.readStatus = 'Partial'; $target.error = 'Direct membership not completed.'
        $target.name = TextOf $group 'DisplayName' $true; $target.externalDirectoryObjectId = GuidOf $group 'ExternalDirectoryObjectId'; $target.primarySmtpAddress = TextOf $group 'PrimarySmtpAddress'
        $target.recipientTypeDetails = Setting $group 'RecipientTypeDetails'; $target.managedBy = OwnersOf $group
        $target.memberJoinRestriction = Setting $group 'MemberJoinRestriction'; $target.memberDepartRestriction = Setting $group 'MemberDepartRestriction'
        $target.requireSenderAuthenticationEnabled = Setting $group 'RequireSenderAuthenticationEnabled' $true; $target.hiddenFromAddressListsEnabled = Setting $group 'HiddenFromAddressListsEnabled' $true
        foreach ($name in @('recipientTypeDetails','managedBy','memberJoinRestriction','memberDepartRestriction','requireSenderAuthenticationEnabled','hiddenFromAddressListsEnabled')) { if ($target[$name].state -cne 'Value') { $unknown = $true } }
        if ($target.managedBy.state -ceq 'Value') { foreach ($owner in $target.managedBy.value) { if ($owner.namespace -ceq 'UnresolvedDisplayLabel') { $unknown = $true } } }
    } catch { $target.lookupReadStatus = 'Failed'; $target.readStatus = 'Failed'; $target.error = 'Exact bounded group lookup failed; no direct-member read admitted.'; continue }
    if ($target.recipientTypeDetails.state -cne 'Value' -or @('MailUniversalDistributionGroup','MailUniversalSecurityGroup','RoomList') -cnotcontains $target.recipientTypeDetails.value) {
        $target.error = 'Group type is unsupported or unknown. Dynamic/Microsoft365 groups are not this adapter population; membership was not read.'; continue
    }
    $batch = [Collections.Generic.List[object]]::new()
    try {
        $source = @(Use-BditRead -Command 'Get-DistributionGroupMember' -Parameters @{ Identity = $id })
        $target.membershipReadStatus = 'Collected'; $target.returnedEntries = $source.Count; $target.excludedEntries = 0
        foreach ($item in $source) {
            $principal = $null; $guid = GuidOf $item 'Guid'
            if ($null -ne $guid) { $principal = @{ namespace = 'ExchangeRecipientGuid'; value = $guid; externalDirectoryObjectId = (GuidOf $item 'ExternalDirectoryObjectId') } }
            else {
                $p = $item.PSObject.Properties['Identity']; if ($null -ne $p) { $principal = PrincipalOf $p.Value }
                if ($null -eq $principal) { $label = TextOf $item 'DisplayName'; if ($null -ne $label) { $principal = @{ namespace = 'UnresolvedDisplayLabel'; value = $label; externalDirectoryObjectId = $null } } }
            }
            if ($null -eq $principal -or $principal.namespace -ceq 'SpecialPrincipal') { $target.excludedEntries++; $target.membershipReadStatus = 'Partial'; continue }
            # Keep a returned directory ID independently, including when the Exchange identity is a raw reference.
            $principal.externalDirectoryObjectId = GuidOf $item 'ExternalDirectoryObjectId'
            foreach ($previous in $batch) { if ($previous.sourceIdentity.namespace -ceq $principal.namespace -and $previous.sourceIdentity.value -ceq $principal.value) { throw 'Duplicate direct-member identity' } }
            $kind = Setting $item 'RecipientTypeDetails'
            $row = [ordered]@{ id = $null; name = (TextOf $item 'DisplayName' $true); readStatus = 'Collected'; error = $null; groupGuid = $id;
                sourceIdentity = $principal; primarySmtpAddress = (TextOf $item 'PrimarySmtpAddress'); recipientTypeDetails = $kind }
            if ($principal.namespace -ceq 'UnresolvedDisplayLabel' -or $kind.state -cne 'Value') { $row.readStatus = 'Partial'; $row.error = 'Original member identity/type is unresolved or unavailable; no name join or effective delivery inferred.'; $target.membershipReadStatus = 'Partial' }
            $batch.Add($row)
        }
        foreach ($row in $batch) { $members.Add($row) }
        if (-not $unknown -and $target.membershipReadStatus -ceq 'Collected') { $target.readStatus = 'Collected'; $target.error = $null }
        else { $target.error = 'Group configuration or direct membership contains unknown/excluded observations; inspect original source states.' }
    } catch { $target.membershipReadStatus = 'Failed'; $target.returnedEntries = $null; $target.excludedEntries = $null; $target.error = 'Direct membership failed, stopped, exceeded its bound or repeated an ambiguous identity; no empty-success claim.' }
}
$status = 'Collected'; $reason = $null
foreach ($target in $targets) { if ($target.readStatus -cne 'Collected') { $status = 'Partial'; $reason = 'Selected group configuration or direct-member reads are incomplete. No recursive/effective-delivery claim.' } }
@{ Sections = @(
    @{ Id = 'target-reads'; Status = $status; Error = $reason; Rows = $targets.ToArray() },
    @{ Id = 'members'; Status = $status; Error = $reason; Rows = $members.ToArray() }
) }
