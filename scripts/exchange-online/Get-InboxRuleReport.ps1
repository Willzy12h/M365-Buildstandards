<#
Inbox rules for one or more mailboxes, including forwarding, redirect and delete actions.
Read only. A true or false value Exchange did not return is shown as Unknown, never as False; with only risky
rules ticked, a rule whose delete or mark-as-read action is Unknown is kept.
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

function Get-Flag([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name]) { return 'Unknown' }
    $value = $Item.$Name
    if ($value -is [bool]) { if ($value) { return 'True' } else { return 'False' } }
    $text = [string]$value
    if ($text -eq 'True') { return 'True' }
    if ($text -eq 'False') { return 'False' }
    return 'Unknown'
}

foreach ($identity in $Mailbox) {
    foreach ($rule in @(Get-InboxRule -Mailbox $identity)) {
        $forwards = (Get-Text $rule.ForwardTo) + (Get-Text $rule.ForwardAsAttachmentTo) + (Get-Text $rule.RedirectTo)
        $delete = Get-Flag $rule 'DeleteMessage'
        $markAsRead = Get-Flag $rule 'MarkAsRead'
        $risky = $forwards -ne '' -or $delete -ne 'False' -or $markAsRead -ne 'False'
        if ($OnlyRiskyRules -and -not $risky) { continue }
        [pscustomobject]@{
            Mailbox = $identity
            Name = [string]$rule.Name
            Enabled = Get-Flag $rule 'Enabled'
            Priority = $rule.Priority
            From = Get-Text $rule.From
            SubjectContainsWords = Get-Text $rule.SubjectContainsWords
            ForwardTo = Get-Text $rule.ForwardTo
            ForwardAsAttachmentTo = Get-Text $rule.ForwardAsAttachmentTo
            RedirectTo = Get-Text $rule.RedirectTo
            MoveToFolder = [string]$rule.MoveToFolder
            DeleteMessage = $delete
            MarkAsRead = $markAsRead
            StopProcessingRules = Get-Flag $rule 'StopProcessingRules'
        }
    }
}
