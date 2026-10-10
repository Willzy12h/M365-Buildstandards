<#
Synthetic schema 2 body for the runner regressions; not a shipped library item. Every read goes through the gate.
A statistics read that fails leaves that mailbox Partial with a fixed reason, never a zero size.
#>
param([bool]$IncludeArchive)
Set-StrictMode -Version Latest
$rows = [System.Collections.Generic.List[object]]::new()
$sectionStatus = 'Collected'
$sectionError = $null
$mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ ResultSize = 'Unlimited' })
foreach ($mailbox in $mailboxes) {
    $objectId = [string]$mailbox.ExternalDirectoryObjectId
    $row = [ordered]@{
        id = [string]$mailbox.ExchangeGuid; name = [string]$mailbox.DisplayName; readStatus = 'Collected'; error = $null
        externalDirectoryObjectId = $(if ([string]::IsNullOrEmpty($objectId)) { $null } else { $objectId })
        primarySmtpAddress = [string]$mailbox.PrimarySmtpAddress; mailboxType = [string]$mailbox.RecipientTypeDetails
        primarySizeReadStatus = 'NotAttempted'; primarySizeRaw = $null
        quotaReadStatus = 'Collected'; issueWarningQuotaRaw = [string]$mailbox.IssueWarningQuota
        prohibitSendQuotaRaw = [string]$mailbox.ProhibitSendQuota; prohibitSendReceiveQuotaRaw = [string]$mailbox.ProhibitSendReceiveQuota
        archiveReadStatus = 'NotAttempted'; hasArchive = $null; archiveSizeReadStatus = 'NotAttempted'; archiveSizeRaw = $null; archiveQuotaRaw = $null
    }
    if ($IncludeArchive) { $row.archiveReadStatus = 'Collected'; $row.hasArchive = $false }
    try {
        $statistics = Use-BditRead -Command 'Get-EXOMailboxStatistics' -Parameters @{ Identity = [string]$mailbox.ExchangeGuid }
        $row.primarySizeReadStatus = 'Collected'
        $row.primarySizeRaw = [string]$statistics.TotalItemSize
    }
    catch {
        $row.primarySizeReadStatus = 'Failed'
        $row.readStatus = 'Partial'
        $row.error = 'The mailbox size could not be read.'
        $sectionStatus = 'Partial'
        $sectionError = 'Some mailbox sizes could not be read.'
    }
    $rows.Add($row)
}
@{ Sections = @(@{ Id = 'mailboxes'; Status = $sectionStatus; Error = $sectionError; Rows = $rows.ToArray() }) }
