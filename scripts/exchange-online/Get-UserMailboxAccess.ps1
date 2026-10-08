<#
Mailboxes one user can access: Full Access, Send As and Send on Behalf.
Read only. Full Access and Send on Behalf are checked on each mailbox up to the limit.
#>
param(
    [Parameter(Mandatory = $true)][string]$User,
    [int]$MaxMailboxes = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$recipient = Get-EXORecipient -Identity $User -Properties DistinguishedName, Name, DisplayName
$names = @([string]$recipient.PrimarySmtpAddress, [string]$recipient.Name, [string]$recipient.DisplayName, [string]$recipient.DistinguishedName, $User) |
    Where-Object { $_ } | Sort-Object -Unique

$mailboxes = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties GrantSendOnBehalfTo)
if ($mailboxes.Count -gt $MaxMailboxes) {
    Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
    $mailboxes = @($mailboxes | Select-Object -First $MaxMailboxes)
}

foreach ($mailbox in $mailboxes) {
    $address = [string]$mailbox.PrimarySmtpAddress
    foreach ($entry in @(Get-EXOMailboxPermission -Identity $mailbox.ExchangeGuid.ToString())) {
        if ($names -notcontains [string]$entry.User) { continue }
        if (@($entry.AccessRights) -notcontains 'FullAccess') { continue }
        [pscustomobject]@{
            Mailbox = $address; MailboxType = [string]$mailbox.RecipientTypeDetails; Permission = 'FullAccess'; IsInherited = [bool]$entry.IsInherited
        }
    }
    foreach ($delegate in @($mailbox.GrantSendOnBehalfTo)) {
        if ($null -ne $delegate -and $names -contains [string]$delegate) {
            [pscustomobject]@{ Mailbox = $address; MailboxType = [string]$mailbox.RecipientTypeDetails; Permission = 'SendOnBehalf'; IsInherited = $false }
        }
    }
}

foreach ($entry in @(Get-EXORecipientPermission -Trustee $User -ResultSize 5000)) {
    if (@($entry.AccessRights) -notcontains 'SendAs') { continue }
    [pscustomobject]@{ Mailbox = [string]$entry.Identity; MailboxType = ''; Permission = 'SendAs'; IsInherited = [bool]$entry.IsInherited }
}
