# INT-128/131: selected unique default calendars; original folder/principal identities, no effective-access verdict.
param([string]$MailboxGuids)
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
function ListOf($Item, [string]$Property, [bool]$ScalarAllowed = $false) {
    $p = $Item.PSObject.Properties[$Property]
    if ($null -eq $p) { return (Unknown 'Missing' ($Property + ' was not returned.')) }
    if ($null -eq $p.Value) { return (Unknown 'ReturnedNull' ($Property + ' returned null.')) }
    $values = $p.Value
    if ($ScalarAllowed -and $values -is [string]) { $values = @($values) }
    if ($values -isnot [array]) { return (Unknown 'Unsupported' ($Property + ' was not a supported list.')) }
    foreach ($value in $values) { if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) { return (Unknown 'Unsupported' ($Property + ' contains an unsupported value.')) } }
    return @{ state = 'Value'; value = $values; reason = $null }
}
$targets = [Collections.Generic.List[object]]::new(); $edges = [Collections.Generic.List[object]]::new()
foreach ($id in $MailboxGuids.Split(',')) {
    $targets.Add([ordered]@{ id = $null; name = $null; readStatus = 'NotAttempted'; error = 'Mailbox lookup not attempted.';
        requestedExchangeGuid = $id; observedExchangeGuid = $null; externalDirectoryObjectId = $null; primarySmtpAddress = $null;
        lookupReadStatus = 'NotAttempted'; folderReadStatus = 'NotAttempted'; returnedFolders = $null; defaultFolderMatches = $null;
        folderId = $null; folderPath = $null; nativeFolderIdentity = $null; permissionReadStatus = 'NotAttempted'; returnedEntries = $null; excludedEntries = $null })
}
foreach ($target in $targets) {
    $id = $target.requestedExchangeGuid
    try {
        $mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ Identity = $id })
        if ($mailboxes.Count -ne 1 -or (GuidOf $mailboxes[0] 'ExchangeGuid') -cne $id) { throw 'Unverified mailbox lookup' }
        $target.observedExchangeGuid = $id; $target.lookupReadStatus = 'Collected'
        $target.name = TextOf $mailboxes[0] 'DisplayName' $true; $target.externalDirectoryObjectId = GuidOf $mailboxes[0] 'ExternalDirectoryObjectId'
        $target.primarySmtpAddress = TextOf $mailboxes[0] 'PrimarySmtpAddress'; $target.readStatus = 'Partial'; $target.error = 'Calendar discovery and ACL read not completed.'
    } catch { $target.lookupReadStatus = 'Failed'; $target.readStatus = 'Failed'; $target.error = 'Exact mailbox lookup failed; no calendar source admitted.'; continue }
    try {
        $folders = @(Use-BditRead -Command 'Get-EXOMailboxFolderStatistics' -Parameters @{ Identity = $id })
        $target.returnedFolders = $folders.Count; $target.defaultFolderMatches = 0; $calendar = $null; $typesKnown = $true
        foreach ($folder in $folders) {
            $type = TextOf $folder 'FolderType'
            if ($null -eq $type) { $typesKnown = $false }
            if ($type -ceq 'Calendar') { $target.defaultFolderMatches++; $calendar = $folder }
        }
        $target.folderReadStatus = 'Partial'
        if (-not $typesKnown -or $target.defaultFolderMatches -ne 1) { $target.error = 'Default calendar is missing, ambiguous or uncheckable; no English-name or first-folder guess.'; continue }
        $path = TextOf $calendar 'FolderPath'; $folderId = TextOf $calendar 'FolderId'
        $valid = $null -ne $path -and $path.Length -ge 2 -and $path.Length -le 980 -and $path[0] -ceq '/' -and -not $path.Contains('\') -and -not $path.Contains(':')
        if ($valid) { foreach ($part in $path.Split('/')) { if (($part.Length -gt 0 -and [string]::IsNullOrWhiteSpace($part)) -or $part -ceq '.' -or $part -ceq '..') { $valid = $false } }; if ($path.Contains('//') -or $path.EndsWith('/')) { $valid = $false } }
        if (-not $valid -or $null -eq $folderId) { $target.error = 'Unique calendar has no admissible returned folder ID/path; no ACL target fabricated.'; continue }
        $target.folderId = $folderId; $target.folderPath = $path; $target.nativeFolderIdentity = $id + ':' + $path.Replace('/','\'); $target.folderReadStatus = 'Collected'
    } catch { $target.folderReadStatus = 'Failed'; $target.returnedFolders = $null; $target.defaultFolderMatches = $null; $target.error = 'Calendar discovery failed, stopped or reached its bound; counts and ACL are unknown.'; continue }
    $batch = [Collections.Generic.List[object]]::new()
    try {
        $source = @(Use-BditRead -Command 'Get-EXOMailboxFolderPermission' -Parameters @{ Identity = $target.nativeFolderIdentity })
        $target.permissionReadStatus = 'Collected'; $target.returnedEntries = $source.Count; $target.excludedEntries = 0
        foreach ($item in $source) {
            $p = $item.PSObject.Properties['User']; $principal = $null
            if ($null -ne $p) { $principal = PrincipalOf $p.Value }
            if ($null -eq $principal) { $target.excludedEntries++; $target.permissionReadStatus = 'Partial'; continue }
            foreach ($previous in $batch) { if ($previous.principal.namespace -ceq $principal.namespace -and $previous.principal.value -ceq $principal.value) { throw 'Source cannot distinguish duplicate principal ACL observations' } }
            $rights = ListOf $item 'AccessRights'; $flags = ListOf $item 'SharingPermissionFlags' $true
            $row = [ordered]@{ id = $null; name = $null; readStatus = 'Collected'; error = $null; mailboxExchangeGuid = $id;
                nativeFolderIdentity = $target.nativeFolderIdentity; principal = $principal; rights = $rights; sharingPermissionFlags = $flags }
            if ($principal.namespace -ceq 'UnresolvedDisplayLabel' -or $rights.state -cne 'Value' -or $flags.state -cne 'Value') {
                $row.readStatus = 'Partial'; $row.error = 'Original principal or ACL values remain unresolved/unknown; no effective delegate access inferred.'; $target.permissionReadStatus = 'Partial'
            }
            $batch.Add($row)
        }
        foreach ($row in $batch) { $edges.Add($row) }
        if ($target.permissionReadStatus -ceq 'Collected') { $target.readStatus = 'Collected'; $target.error = $null }
        else { $target.error = 'Calendar ACL has excluded/unavailable source values; inspect original counts and states.' }
    } catch { $target.permissionReadStatus = 'Failed'; $target.returnedEntries = $null; $target.excludedEntries = $null; $target.error = 'Calendar ACL failed, stopped, exceeded a bound or had duplicate principal identities; no complete or empty-success claim.' }
}
$status = 'Collected'; $reason = $null
foreach ($target in $targets) { if ($target.readStatus -cne 'Collected') { $status = 'Partial'; $reason = 'Selected calendar discovery or ACL reads are incomplete; inspect target diagnostics. No effective-access verdict.' } }
@{ Sections = @(
    @{ Id = 'target-reads'; Status = $status; Error = $reason; Rows = $targets.ToArray() },
    @{ Id = 'permissions'; Status = $status; Error = $reason; Rows = $edges.ToArray() }
) }
