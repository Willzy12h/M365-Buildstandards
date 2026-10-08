<#
Inbox rules for one or more mailboxes, including forwarding, redirect and delete actions.
Read only.
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Mailbox,
    [switch]$OnlyRiskyRules
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Text([object]$Value) {
    if ($null -eq $Value) { return '' }
    return (@($Value) | ForEach-Object { [string]$_ }) -join '; '
}

foreach ($identity in $Mailbox) {
    foreach ($rule in @(Get-InboxRule -Mailbox $identity)) {
        $forwards = (Get-Text $rule.ForwardTo) + (Get-Text $rule.ForwardAsAttachmentTo) + (Get-Text $rule.RedirectTo)
        $risky = $forwards -ne '' -or [bool]$rule.DeleteMessage -or [bool]$rule.MarkAsRead
        if ($OnlyRiskyRules -and -not $risky) { continue }
        [pscustomobject]@{
            Mailbox = $identity
            Name = [string]$rule.Name
            Enabled = [bool]$rule.Enabled
            Priority = $rule.Priority
            From = Get-Text $rule.From
            SubjectContainsWords = Get-Text $rule.SubjectContainsWords
            ForwardTo = Get-Text $rule.ForwardTo
            ForwardAsAttachmentTo = Get-Text $rule.ForwardAsAttachmentTo
            RedirectTo = Get-Text $rule.RedirectTo
            MoveToFolder = [string]$rule.MoveToFolder
            DeleteMessage = [bool]$rule.DeleteMessage
            MarkAsRead = [bool]$rule.MarkAsRead
            StopProcessingRules = [bool]$rule.StopProcessingRules
        }
    }
}
