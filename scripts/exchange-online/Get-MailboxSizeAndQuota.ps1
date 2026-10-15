<#
Mailbox size, quotas and archive, read through the toolkit's own read-only runner (INT-088). This body cannot be copied:
it runs only inside the runner, which signs in, checks the tenant, account and sign-in before and after every read, and
keeps only a result that matches the registered report exactly.

Each value group has its own read state. A size or quota that could not be read is recorded as not read with a reason,
never as zero or blank; Unlimited stays Unlimited. An archive is present only when Exchange returns a non-empty archive
GUID. At most 10,000 mailboxes are listed; more makes the section Partial.
#>
param([bool]$IncludeArchive)
Set-StrictMode -Version Latest

$cap = 10000
$emptyGuid = '00000000-0000-0000-0000-000000000000'
$properties = @('ExchangeGuid', 'ExternalDirectoryObjectId', 'IssueWarningQuota', 'ProhibitSendQuota', 'ProhibitSendReceiveQuota', 'ArchiveGuid', 'ArchiveQuota')
$rows = [System.Collections.Generic.List[object]]::new()
$sectionStatus = 'Collected'
# Every limitation that applies is kept, so one never hides another.
$sectionReasons = [System.Collections.Generic.List[object]]::new()

# One property as text, or $null when Exchange did not return it or returned only whitespace.
function TextOf($Item, [string]$Name) {
    if ($null -eq $Item) { return $null }
    $property = $Item.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $null }
    $text = [string]$property.Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return $text
}

# A GUID in canonical form, or $null when the text is not a GUID.
function GuidOf([string]$Text) {
    if ($null -eq $Text) { return $null }
    try { return [string]([guid]$Text) } catch { return $null }
}

$mailboxes = @(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{ ResultSize = ($cap + 1); Properties = $properties })
if ($mailboxes.Count -gt $cap) {
    $mailboxes = @($mailboxes | Select-Object -First $cap)
    $sectionStatus = 'Partial'
    $sectionReasons.Add('More than 10,000 mailboxes were found; only the first 10,000 are listed.')
}
$missingGuid = 'Some mailboxes were returned without an Exchange mailbox GUID and are not listed.'
$unreadValues = 'Some mailbox values could not be read; each row says which.'

foreach ($mailbox in $mailboxes) {
    $problems = [System.Collections.Generic.List[object]]::new()
    $id = GuidOf (TextOf $mailbox 'ExchangeGuid')
    if ($null -eq $id) {
        # Without its exact mailbox GUID a row cannot be joined or compared, so it is not listed at all.
        $sectionStatus = 'Partial'
        if (-not $sectionReasons.Contains($missingGuid)) { $sectionReasons.Add($missingGuid) }
        continue
    }
    $objectIdText = TextOf $mailbox 'ExternalDirectoryObjectId'
    $objectId = GuidOf $objectIdText
    if ($null -ne $objectIdText -and $null -eq $objectId) { $problems.Add('The Entra object ID Exchange returned is not a GUID.') }
    $type = TextOf $mailbox 'RecipientTypeDetails'
    if ($null -eq $type) { $problems.Add('The mailbox type was not returned.') }

    $row = [ordered]@{
        id = $id; name = (TextOf $mailbox 'DisplayName'); readStatus = 'Collected'; error = $null
        externalDirectoryObjectId = $objectId; primarySmtpAddress = (TextOf $mailbox 'PrimarySmtpAddress'); mailboxType = $type
        primarySizeReadStatus = 'NotAttempted'; primarySizeRaw = $null
        quotaReadStatus = 'Failed'; issueWarningQuotaRaw = $null; prohibitSendQuotaRaw = $null; prohibitSendReceiveQuotaRaw = $null
        archiveReadStatus = 'NotAttempted'; hasArchive = $null; archiveSizeReadStatus = 'NotAttempted'; archiveSizeRaw = $null; archiveQuotaRaw = $null
    }

    $warning = TextOf $mailbox 'IssueWarningQuota'
    $send = TextOf $mailbox 'ProhibitSendQuota'
    $sendReceive = TextOf $mailbox 'ProhibitSendReceiveQuota'
    if ($null -ne $warning -and $null -ne $send -and $null -ne $sendReceive) {
        $row.quotaReadStatus = 'Collected'
        $row.issueWarningQuotaRaw = $warning; $row.prohibitSendQuotaRaw = $send; $row.prohibitSendReceiveQuotaRaw = $sendReceive
    }
    else { $problems.Add('The mailbox quotas were not all returned.') }

    try {
        $statistics = Use-BditRead -Command 'Get-EXOMailboxStatistics' -Parameters @{ Identity = $id }
        $size = TextOf $statistics 'TotalItemSize'
        if ($null -eq $size) { $row.primarySizeReadStatus = 'Failed'; $problems.Add('The mailbox size was not returned.') }
        else { $row.primarySizeReadStatus = 'Collected'; $row.primarySizeRaw = $size }
    }
    catch { $row.primarySizeReadStatus = 'Failed'; $problems.Add('The mailbox size could not be read.') }

    if ($IncludeArchive) {
        $archiveGuid = GuidOf (TextOf $mailbox 'ArchiveGuid')
        if ($null -eq $archiveGuid) { $row.archiveReadStatus = 'Failed'; $problems.Add('Whether the mailbox has an archive was not returned.') }
        else {
            $row.archiveReadStatus = 'Collected'
            $row.hasArchive = -not $archiveGuid.Equals($emptyGuid)
            if ($row.hasArchive) {
                $row.archiveQuotaRaw = TextOf $mailbox 'ArchiveQuota'
                if ($null -eq $row.archiveQuotaRaw) { $problems.Add('The archive quota was not returned.') }
                try {
                    $archive = Use-BditRead -Command 'Get-EXOMailboxStatistics' -Parameters @{ Identity = $id; Archive = $true }
                    $archiveSize = TextOf $archive 'TotalItemSize'
                    if ($null -eq $archiveSize) { $row.archiveSizeReadStatus = 'Failed'; $problems.Add('The archive size was not returned.') }
                    else { $row.archiveSizeReadStatus = 'Collected'; $row.archiveSizeRaw = $archiveSize }
                }
                catch { $row.archiveSizeReadStatus = 'Failed'; $problems.Add('The archive size could not be read.') }
            }
        }
    }

    if ($problems.Count -gt 0) {
        $row.readStatus = 'Partial'
        $row.error = [string]::Join(' ', $problems.ToArray())
        $sectionStatus = 'Partial'
        if (-not $sectionReasons.Contains($unreadValues)) { $sectionReasons.Add($unreadValues) }
    }
    $rows.Add($row)
}
$sectionError = $null
if ($sectionReasons.Count -gt 0) { $sectionError = [string]::Join(' ', $sectionReasons.ToArray()) }
@{ Sections = @(@{ Id = 'mailboxes'; Status = $sectionStatus; Error = $sectionError; Rows = $rows.ToArray() }) }
