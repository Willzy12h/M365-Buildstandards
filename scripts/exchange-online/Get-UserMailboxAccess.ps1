<#
Mailboxes one user has direct entries on: Full Access, Send As and Send on Behalf.
Read only. Entries are matched on the user's exact identities (sign-in name, primary SMTP address, distinguished name or
object ID), never on a display name. An entry that names the user only by name is kept as Unresolved, a deny entry as
Denied and an entry that does not say whether it allows or denies as Unknown. This is not effective access: rights held
through a group, or inherited from elsewhere, are shown against that group or source, not against this user.
#>
param(
    [Parameter(Mandatory = $true)][string]$User,
    [int]$MaxMailboxes = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Flag([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $value = $Item.$Name
    if ($value -is [bool]) { if ($value) { return 'True' } else { return 'False' } }
    $text = [string]$value
    if ($text -eq 'True') { return 'True' }
    if ($text -eq 'False') { return 'False' }
    return 'Unknown'
}

function Get-Property([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return [string]$Item.$Name }
    return ''
}

$recipient = Get-EXORecipient -Identity $User -Properties DistinguishedName, Name, DisplayName
# Exact identities only. Names and display names are not unique enough to prove an entry is this user's.
$exact = @($User, (Get-Property $recipient 'PrimarySmtpAddress'), (Get-Property $recipient 'DistinguishedName'),
    (Get-Property $recipient 'ExternalDirectoryObjectId'), (Get-Property $recipient 'Guid')) | Where-Object { $_ } | Sort-Object -Unique
$namesOnly = @((Get-Property $recipient 'Name'), (Get-Property $recipient 'DisplayName')) | Where-Object { $_ -and $exact -notcontains $_ } | Sort-Object -Unique
$unresolvedNote = 'Exchange named the holder only by name, which another recipient can share. Confirm who holds it before acting.'

function Get-Match([string]$Holder) {
    if ($exact -contains $Holder) { return 'Exact' }
    if ($namesOnly -contains $Holder) { return 'NameOnly' }
    return ''
}

function Get-AccessRow([string]$Mailbox, [string]$Type, [string]$Permission, [string]$Status, [string]$Holder, [string]$Inherited, [string[]]$Notes) {
    if ($Inherited -eq 'True') { $Notes += 'Inherited from a parent object rather than set on this mailbox.' }
    if ($Inherited -eq 'Unknown') { $Notes += 'Exchange did not return whether this entry is inherited.' }
    [pscustomobject]@{
        Mailbox = $Mailbox; MailboxType = $Type; Permission = $Permission; Status = $Status
        Holder = $Holder; IsInherited = $Inherited; Notes = (@($Notes) | Where-Object { $_ }) -join ' '
    }
}

$mailboxes = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties GrantSendOnBehalfTo)
if ($mailboxes.Count -gt $MaxMailboxes) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
    $mailboxes = @($mailboxes | Select-Object -First $MaxMailboxes)
}

foreach ($mailbox in $mailboxes) {
    $address = [string]$mailbox.PrimarySmtpAddress
    $type = [string]$mailbox.RecipientTypeDetails
    foreach ($entry in @(Get-EXOMailboxPermission -Identity $mailbox.ExchangeGuid.ToString())) {
        $holder = [string]$entry.User
        $match = Get-Match $holder
        if (-not $match) { continue }
        if (@($entry.AccessRights) -notcontains 'FullAccess') { continue }
        $notes = @()
        $deny = Get-Flag $entry 'Deny'
        $status = 'Unknown'
        if ($deny -eq 'False') { $status = 'Granted' }
        if ($deny -eq 'True') { $status = 'Denied' }
        if ($deny -eq 'Unknown') { $notes += 'Exchange did not say whether this entry allows or denies access.' }
        if ($match -eq 'NameOnly') { $status = 'Unresolved'; $notes += $unresolvedNote }
        Get-AccessRow $address $type 'FullAccess' $status $holder (Get-Flag $entry 'IsInherited') $notes
    }
    foreach ($delegate in @($mailbox.GrantSendOnBehalfTo)) {
        if ($null -eq $delegate) { continue }
        $holder = [string]$delegate
        $match = Get-Match $holder
        if (-not $match) { continue }
        $status = 'Granted'
        $notes = @()
        if ($match -eq 'NameOnly') { $status = 'Unresolved'; $notes += $unresolvedNote }
        Get-AccessRow $address $type 'SendOnBehalf' $status $holder 'False' $notes
    }
}

# Send As is read by trustee. One more entry than the limit is requested so a full result is known to be partial.
$sendAsLimit = 5000
$sendAs = @(Get-EXORecipientPermission -Trustee $User -ResultSize ($sendAsLimit + 1))
if ($sendAs.Count -gt $sendAsLimit) {
    Write-Warning ('BDIT:PARTIAL The user has more than ' + $sendAsLimit + ' Send As entries; only the first ' + $sendAsLimit + ' are listed.')
    $sendAs = @($sendAs | Select-Object -First $sendAsLimit)
}
foreach ($entry in $sendAs) {
    if (@($entry.AccessRights) -notcontains 'SendAs') { continue }
    $notes = @()
    $status = 'Unknown'
    $control = Get-Property $entry 'AccessControlType'
    if ($control -eq 'Allow') { $status = 'Granted' }
    if ($control -eq 'Deny') { $status = 'Denied' }
    if ($status -eq 'Unknown') { $notes += 'Exchange did not say whether this entry allows or denies access.' }
    Get-AccessRow ([string]$entry.Identity) '' 'SendAs' $status (Get-Property $entry 'Trustee') (Get-Flag $entry 'IsInherited') $notes
}
