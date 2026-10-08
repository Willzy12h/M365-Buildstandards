<#
Delegated access on mailboxes: Full Access, Send As and Send on Behalf.
Read only. Leave Mailbox blank to check every mailbox up to the limit. IsInherited and Deny are Unknown, never False,
when Exchange did not return them; an entry with unknown inheritance is always listed.
#>
param(
    [string[]]$Mailbox,
    [switch]$FullAccess,
    [switch]$SendAs,
    [switch]$SendOnBehalf,
    [switch]$IncludeInherited,
    [int]$MaxMailboxes = 500,
    [int]$MaxRows = 5000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not ($FullAccess -or $SendAs -or $SendOnBehalf)) { throw 'Tick at least one of Full Access, Send As or Send on Behalf.' }

$properties = @('GrantSendOnBehalfTo')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity -Properties $properties })
} else {
    $targets = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties $properties)
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
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

$rows = [System.Collections.Generic.List[object]]::new()
function Get-Row([string]$Target, [string]$Kind, [string]$User, [string]$Rights, [string]$Inherited, [string]$Deny) {
    [pscustomobject]@{ Mailbox = $Target; Permission = $Kind; User = $User; AccessRights = $Rights; IsInherited = $Inherited; Deny = $Deny }
}

foreach ($target in $targets) {
    if ($rows.Count -ge $MaxRows) { break }
    $address = [string]$target.PrimarySmtpAddress
    $identity = $target.ExchangeGuid.ToString()
    if ($FullAccess) {
        foreach ($entry in @(Get-EXOMailboxPermission -Identity $identity)) {
            $user = [string]$entry.User
            if ($user -eq 'NT AUTHORITY\SELF' -or $user -like 'S-1-5-*') { continue }
            $inherited = Get-Flag $entry 'IsInherited'
            if ($inherited -eq 'True' -and -not $IncludeInherited) { continue }
            if (@($entry.AccessRights) -notcontains 'FullAccess') { continue }
            $rows.Add((Get-Row $address 'FullAccess' $user ((@($entry.AccessRights) | ForEach-Object { [string]$_ }) -join ', ') $inherited (Get-Flag $entry 'Deny')))
        }
    }
    if ($SendAs) {
        foreach ($entry in @(Get-EXORecipientPermission -Identity $identity)) {
            $user = [string]$entry.Trustee
            if ($user -eq 'NT AUTHORITY\SELF' -or $user -like 'S-1-5-*') { continue }
            $inherited = Get-Flag $entry 'IsInherited'
            if ($inherited -eq 'True' -and -not $IncludeInherited) { continue }
            if (@($entry.AccessRights) -notcontains 'SendAs') { continue }
            $deny = 'Unknown'
            if ($entry.PSObject.Properties['AccessControlType']) {
                if ([string]$entry.AccessControlType -eq 'Deny') { $deny = 'True' }
                if ([string]$entry.AccessControlType -eq 'Allow') { $deny = 'False' }
            }
            $rows.Add((Get-Row $address 'SendAs' $user 'SendAs' $inherited $deny))
        }
    }
    if ($SendOnBehalf) {
        foreach ($delegate in @($target.GrantSendOnBehalfTo)) {
            if ($null -eq $delegate -or [string]$delegate -eq '') { continue }
            $rows.Add((Get-Row $address 'SendOnBehalf' ([string]$delegate) 'SendOnBehalf' 'False' 'False'))
        }
    }
}
if ($rows.Count -ge $MaxRows) { Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' rows.') }
$rows | Select-Object -First $MaxRows
