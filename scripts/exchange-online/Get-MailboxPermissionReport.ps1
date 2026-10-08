<#
Delegated access on mailboxes: Full Access, Send As and Send on Behalf.
Read only. Leave Mailbox blank to check every mailbox up to the limit.
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

$rows = [System.Collections.Generic.List[object]]::new()
function Get-Row([string]$Target, [string]$Kind, [string]$User, [string]$Rights, [bool]$Inherited, [bool]$Deny) {
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
            if ($entry.IsInherited -and -not $IncludeInherited) { continue }
            if (@($entry.AccessRights) -notcontains 'FullAccess') { continue }
            $rows.Add((Get-Row $address 'FullAccess' $user ((@($entry.AccessRights) | ForEach-Object { [string]$_ }) -join ', ') ([bool]$entry.IsInherited) ([bool]$entry.Deny)))
        }
    }
    if ($SendAs) {
        foreach ($entry in @(Get-EXORecipientPermission -Identity $identity)) {
            $user = [string]$entry.Trustee
            if ($user -eq 'NT AUTHORITY\SELF' -or $user -like 'S-1-5-*') { continue }
            if ($entry.IsInherited -and -not $IncludeInherited) { continue }
            if (@($entry.AccessRights) -notcontains 'SendAs') { continue }
            $rows.Add((Get-Row $address 'SendAs' $user 'SendAs' ([bool]$entry.IsInherited) ([string]$entry.AccessControlType -eq 'Deny')))
        }
    }
    if ($SendOnBehalf) {
        foreach ($delegate in @($target.GrantSendOnBehalfTo)) {
            if ($null -eq $delegate -or [string]$delegate -eq '') { continue }
            $rows.Add((Get-Row $address 'SendOnBehalf' ([string]$delegate) 'SendOnBehalf' $false $false))
        }
    }
}
if ($rows.Count -ge $MaxRows) { Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' rows.') }
$rows | Select-Object -First $MaxRows
