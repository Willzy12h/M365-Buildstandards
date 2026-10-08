<#
Send on Behalf delegates across mailboxes, each resolved to an exact identity or marked Unresolved.
Read only. Exchange returns delegates as names, so each is looked up once and is Resolved only when Exchange finds
exactly one recipient whose name, alias, address or ID is that value; a value that matches only a display name,
several recipients or none is Unresolved, never guessed. A mailbox whose delegate list was not returned (or came
back null rather than empty) is listed with Status Unknown. This is the delegate list only, not effective access.
#>
param(
    [string[]]$Mailbox,
    [int]$MaxMailboxes = 2000,
    [int]$MaxRows = 5000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Value([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
    return $null
}

$lookups = @{}
# Exchange's own identity lookup, accepted only when the value is a unique identity of the one recipient it finds.
function Get-HolderIdentity([string]$Value) {
    if ($lookups.ContainsKey($Value)) { return $lookups[$Value] }
    $found = @()
    $failure = ''
    try { $found = @(Get-EXORecipient -Identity $Value -Properties Alias, DistinguishedName, Name, DisplayName) }
    catch { $failure = $_.Exception.Message }
    $result = [pscustomobject]@{ Status = 'Unresolved'; Address = ''; ObjectId = ''; Type = ''; DisplayName = ''; Note = '' }
    if ($found.Count -eq 1) {
        $candidate = $found[0]
        $unique = @('Name', 'Alias', 'PrimarySmtpAddress', 'DistinguishedName', 'ExternalDirectoryObjectId', 'Guid', 'Identity') |
            ForEach-Object { [string](Get-Value $candidate $_) } | Where-Object { $_ }
        $exact = @($unique | Where-Object { [string]::Equals($_, $Value, [StringComparison]::OrdinalIgnoreCase) })
        if ($exact.Count -gt 0) {
            $result.Status = 'Resolved'
            $result.Address = [string](Get-Value $candidate 'PrimarySmtpAddress')
            $result.ObjectId = [string](Get-Value $candidate 'ExternalDirectoryObjectId')
            $result.Type = [string](Get-Value $candidate 'RecipientTypeDetails')
            $result.DisplayName = [string](Get-Value $candidate 'DisplayName')
        } else {
            $result.Note = 'Exchange matched this value only on a display name, which another recipient can share. Confirm who it is before acting.'
        }
    } elseif ($found.Count -gt 1) {
        $result.Note = 'This value matches more than one recipient. Confirm who it is before acting.'
    } else {
        $result.Note = 'Exchange could not resolve this value to a recipient.'
        if ($failure) { $result.Note = $result.Note + ' ' + $failure }
    }
    $lookups[$Value] = $result
    return $result
}

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

$rows = 0
$unknown = 0
$unresolved = 0
$stopped = $false
foreach ($target in $targets) {
    if ($stopped) { break }
    $address = [string]$target.PrimarySmtpAddress
    $type = [string](Get-Value $target 'RecipientTypeDetails')
    if (-not $target.PSObject.Properties['GrantSendOnBehalfTo'] -or $null -eq $target.GrantSendOnBehalfTo) {
        if ($rows -ge $MaxRows) { $stopped = $true; break }
        $rows++
        $unknown++
        [pscustomobject]@{
            Mailbox = $address; MailboxType = $type; DelegateAsReturned = ''; Status = 'Unknown'; DelegateName = ''; DelegateAddress = ''
            DelegateObjectId = ''; DelegateType = ''; Notes = 'Exchange did not return the Send on Behalf list for this mailbox.'
        }
        continue
    }
    foreach ($delegate in @($target.GrantSendOnBehalfTo)) {
        if ($null -eq $delegate -or [string]$delegate -eq '') { continue }
        if ($rows -ge $MaxRows) { $stopped = $true; break }
        $rows++
        $resolved = Get-HolderIdentity ([string]$delegate)
        if ($resolved.Status -ne 'Resolved') { $unresolved++ }
        [pscustomobject]@{
            Mailbox = $address; MailboxType = $type; DelegateAsReturned = [string]$delegate; Status = $resolved.Status
            DelegateName = $resolved.DisplayName; DelegateAddress = $resolved.Address; DelegateObjectId = $resolved.ObjectId
            DelegateType = $resolved.Type; Notes = $resolved.Note
        }
    }
}
if ($stopped) { Write-Warning ('BDIT:PARTIAL Stopped at ' + $MaxRows + ' rows.') }
if ($unknown -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknown + ' mailbox(es) did not return a Send on Behalf list. They are listed with Status Unknown.')
}
if ($unresolved -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unresolved + ' delegate(s) could not be resolved to exactly one recipient. They are listed as Unresolved.')
}
