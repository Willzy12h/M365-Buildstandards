<#
Mail forwarding: mailbox-level forwarding and, if ticked, forwarding or redirect inbox rules.
Read only. A target is External when its domain is not one of the tenant's accepted domains, and
Unknown when it is a recipient object rather than an address.
#>
param(
    [string[]]$Mailbox,
    [switch]$IncludeInboxRules,
    [int]$MaxMailboxes = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$accepted = @(Get-AcceptedDomain | ForEach-Object { ([string]$_.DomainName).ToLowerInvariant() })
function Get-Scope([string]$Address) {
    if ($Address -notmatch '@([^@\s>\]]+)$') { return 'Unknown' }
    if ($accepted -contains $Matches[1].ToLowerInvariant()) { return 'Internal' }
    return 'External'
}

$properties = @('ForwardingSmtpAddress', 'ForwardingAddress', 'DeliverToMailboxAndForward')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity -Properties $properties })
} else {
    $targets = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties $properties)
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
}

foreach ($target in $targets) {
    $address = [string]$target.PrimarySmtpAddress
    $smtp = [string]$target.ForwardingSmtpAddress
    if ($smtp) {
        $plain = $smtp -replace '^smtp:', ''
        [pscustomobject]@{
            Mailbox = $address; Source = 'MailboxForwarding'; RuleName = ''; Target = $plain
            Scope = Get-Scope $plain; KeepsCopy = [bool]$target.DeliverToMailboxAndForward
        }
    }
    $recipient = [string]$target.ForwardingAddress
    if ($recipient) {
        [pscustomobject]@{
            Mailbox = $address; Source = 'MailboxForwarding'; RuleName = ''; Target = $recipient
            Scope = 'Unknown'; KeepsCopy = [bool]$target.DeliverToMailboxAndForward
        }
    }
    if ($IncludeInboxRules) {
        foreach ($rule in @(Get-InboxRule -Mailbox $target.ExchangeGuid.ToString())) {
            $entries = @($rule.ForwardTo) + @($rule.ForwardAsAttachmentTo) + @($rule.RedirectTo) | Where-Object { $null -ne $_ }
            foreach ($entry in $entries) {
                $text = [string]$entry
                $plain = $text
                if ($text -match 'SMTP:([^\]\s]+)') { $plain = $Matches[1] }
                [pscustomobject]@{
                    Mailbox = $address; Source = 'InboxRule'; RuleName = [string]$rule.Name; Target = $plain
                    Scope = Get-Scope $plain; KeepsCopy = -not [bool]$rule.DeleteMessage
                }
            }
        }
    }
}
