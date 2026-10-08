#requires -Version 5.1
<#
Runs every library Copy script end to end against a synthetic stand-in for the ExchangeOnlineManagement module.

No Microsoft module is loaded and no tenant is contacted: a temporary module with the same name and version answers
each read with fixed synthetic objects. This proves the generated wrapper, the tenant check, the argument binding,
the reviewed bodies and the CSV output work together. It does not prove the real cmdlets behave the same way;
live status stays unverified until an engineer runs each item in a test tenant.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Bdit,
    [Parameter(Mandatory = $true)][string]$Library,
    [string]$PowerShell = (Get-Process -Id $PID).Path
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tenant = '3f2504e0-4f89-11d3-9a0c-0305e82c3301'
$today = [DateTime]::UtcNow.Date
$from = $today.AddDays(-20).ToString('yyyy-MM-dd')
$to = $today.ToString('yyyy-MM-dd')

# Each item's sample form, as the engineer would fill it. A new library item without a sample fails here.
# Values avoid embedded double quotes, which Windows PowerShell 5.1 does not pass reliably to native programs;
# the engine tests cover hostile quoting directly.
$samples = @{
    'exo.mailbox-inventory' = @()
    'exo.quota-audit' = @('--ThresholdPercent', '50')
    'exo.mailbox-permissions' = @('--IncludeInherited', 'true')
    'exo.user-mailbox-access' = @('--User', 'alex@contoso.example')
    'exo.calendar-permissions' = @('--Mailbox', 'room1@contoso.example, alex')
    'exo.inbox-rules' = @('--Mailbox', 'alex@contoso.example')
    'exo.external-forwarding' = @('--IncludeInboxRules', 'true')
    'exo.shared-mailboxes' = @()
    'exo.message-trace' = @('--StartDate', $from, '--EndDate', $to, '--Subject', 'Don''t; $(calc)', '--Status', 'Delivered,Failed')
    'exo.unified-audit-search' = @('--StartDate', $from, '--EndDate', $to, '--UserIds', "o'brien@contoso.example")
    'exo.group-members' = @('--Group', 'staff@contoso.example, team@contoso.example, dynamic@contoso.example')
    'exo.transport-rules' = @()
    'exo.connectors' = @()
    'exo.domains-dkim' = @()
    'exo.mobile-devices' = @()
    'exo.mailbox-audit' = @()
    'exo.mailbox-holds' = @()
    'exo.protection-policies' = @()
    'exo.resource-mailboxes' = @()
    'exo.send-on-behalf' = @()
    'exo.archive-mailboxes' = @()
}

$stub = @'
$script:auditPages = 0
function Get-Scenario { [string]$env:BDIT_STUB_SCENARIO }
# Every tenant read is logged, so a refused run can be shown to have read nothing.
function Write-StubRead([string]$Name) { if ($env:BDIT_STUB_READS) { [IO.File]::AppendAllText($env:BDIT_STUB_READS, $Name + "`n") } }
function Connect-ExchangeOnline { }
function Disconnect-ExchangeOnline { [IO.File]::WriteAllText($env:BDIT_STUB_DISCONNECT, 'disconnected') }
function Get-ConnectionInformation {
    $upn = 'admin@contoso.example'
    if ($env:BDIT_STUB_UPN) { $upn = $env:BDIT_STUB_UPN }
    [pscustomobject]@{ State = 'Connected'; IsEopSession = $false; TenantID = $env:BDIT_STUB_TENANT; UserPrincipalName = $upn }
}
function New-StubMailbox([string]$Name, [string]$Smtp, [string]$Type, [string]$Guid, [string]$Quota, [object]$Hold, [object]$Hidden, [object]$KeepCopy, [object]$OnBehalf) {
    [pscustomobject]@{
        DisplayName = $Name; PrimarySmtpAddress = $Smtp; RecipientTypeDetails = $Type; ExchangeGuid = [guid]$Guid
        IssueWarningQuota = $Quota; ProhibitSendQuota = $Quota; ProhibitSendReceiveQuota = $Quota; ArchiveStatus = 'None'
        LitigationHoldEnabled = $Hold; GrantSendOnBehalfTo = $OnBehalf; ForwardingSmtpAddress = 'smtp:out@external.example'
        ForwardingAddress = $null; DeliverToMailboxAndForward = $KeepCopy; HiddenFromAddressListsEnabled = $Hidden
        AuditEnabled = $Hold; AuditLogAgeLimit = '90.00:00:00'; DefaultAuditSet = @('Admin', 'Delegate', 'Owner'); AuditOwner = @('Update')
        AuditDelegate = @('SendAs'); AuditAdmin = @('Update'); ArchiveGuid = (Get-StubArchiveGuid $Hold); ArchiveName = 'In-Place Archive'
        ArchiveQuota = '110 GB (118,111,600,640 bytes)'; ArchiveWarningQuota = '100 GB (107,374,182,400 bytes)'
        AutoExpandingArchiveEnabled = $Hold; InPlaceHolds = (Get-StubHolds $Hold); ComplianceTagHoldApplied = $Hold; DelayHoldApplied = $Hold
        DelayReleaseHoldApplied = $Hold; RetentionHoldEnabled = $Hold; RetentionPolicy = 'Default MRM Policy'
        LitigationHoldDate = $null; LitigationHoldOwner = ''; LitigationHoldDuration = 'Unlimited'
    }
}
# True, false and not returned map to an archive, no archive (empty GUID) and no GUID at all.
function Get-StubArchiveGuid([object]$Flag) {
    if ($Flag -eq $true) { return [guid]'abcdabcd-0000-4000-8000-000000000001' }
    if ($Flag -eq $false) { return [guid]::Empty }
    return $null
}
function Get-StubHolds([object]$Flag) {
    if ($Flag -eq $true) { return ,@('mbx0a1b2c3d4e5f') }
    if ($Flag -eq $false) { return ,@() }
    return $null
}
function Get-Mailboxes {
    $hundred = '100 GB (107,374,182,400 bytes)'
    switch (Get-Scenario) {
        'quota-unknown' {
            return @(
                (New-StubMailbox 'Unlimited' 'unlimited@contoso.example' 'SharedMailbox' '33333333-3333-3333-3333-333333333333' 'Unlimited' $false $false $false @()),
                (New-StubMailbox 'Malformed quota' 'malformed@contoso.example' 'UserMailbox' '44444444-4444-4444-4444-444444444444' 'about 100 GB' $false $false $false @()),
                (New-StubMailbox 'No size' 'nosize@contoso.example' 'UserMailbox' '55555555-5555-5555-5555-555555555555' $hundred $false $false $false @()),
                (New-StubMailbox 'Over' 'over@contoso.example' 'UserMailbox' '66666666-6666-6666-6666-666666666666' $hundred $false $false $false @())
            )
        }
        'flags' {
            return @(
                (New-StubMailbox 'Flags true' 'true@contoso.example' 'SharedMailbox' '77777777-7777-7777-7777-777777777777' $hundred $true $true $true @()),
                (New-StubMailbox 'Flags false' 'false@contoso.example' 'SharedMailbox' '88888888-8888-8888-8888-888888888888' $hundred $false $false $false @()),
                (New-StubMailbox 'Flags null' 'null@contoso.example' 'SharedMailbox' '99999999-9999-9999-9999-999999999999' $hundred $null $null $null @())
            )
        }
        'delegates' {
            return @(
                (New-StubMailbox 'Delegated' 'delegated@contoso.example' 'SharedMailbox' 'dddddddd-dddd-dddd-dddd-dddddddddddd' $hundred $false $false $false @('megan@contoso.example', 'megan', 'Megan Bowen', 'Shared Name', 'ghost')),
                (New-StubMailbox 'Also megan' 'also@contoso.example' 'SharedMailbox' 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee' $hundred $false $false $false @('megan')),
                [pscustomobject]@{ DisplayName = 'No list'; PrimarySmtpAddress = 'nolist@contoso.example'; RecipientTypeDetails = 'UserMailbox'; ExchangeGuid = [guid]'ffffffff-ffff-ffff-ffff-ffffffffffff' }
            )
        }
        'access' {
            return @(
                (New-StubMailbox 'Exact' 'exact@contoso.example' 'SharedMailbox' 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' $hundred $false $false $false @('Alex Wilber')),
                (New-StubMailbox 'Namesake' 'namesake@contoso.example' 'SharedMailbox' 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' $hundred $false $false $false @('alex.other@contoso.example')),
                (New-StubMailbox 'Denied' 'denied@contoso.example' 'SharedMailbox' 'cccccccc-cccc-cccc-cccc-cccccccccccc' $hundred $false $false $false @('alex@contoso.example'))
            )
        }
    }
    @(
        [pscustomobject]@{
            DisplayName = 'Alex Wilber'; PrimarySmtpAddress = 'alex@contoso.example'; RecipientTypeDetails = 'UserMailbox'
            ExchangeGuid = [guid]'11111111-1111-1111-1111-111111111111'
            IssueWarningQuota = '98 GB (105,226,698,752 bytes)'; ProhibitSendQuota = '99 GB (106,300,440,576 bytes)'
            ProhibitSendReceiveQuota = '100 GB (107,374,182,400 bytes)'; ArchiveStatus = 'Active'; LitigationHoldEnabled = $false
            GrantSendOnBehalfTo = @('Megan Bowen'); ForwardingSmtpAddress = 'smtp:out@external.example'; ForwardingAddress = $null
            DeliverToMailboxAndForward = $true; HiddenFromAddressListsEnabled = $false
            AuditEnabled = $true; AuditLogAgeLimit = '90.00:00:00'; DefaultAuditSet = @('Admin', 'Delegate', 'Owner'); AuditOwner = @('Update')
            AuditDelegate = @('SendAs'); AuditAdmin = @('Update'); ArchiveGuid = [guid]'abcdabcd-0000-4000-8000-000000000002'
            ArchiveName = 'In-Place Archive - Alex Wilber'; ArchiveQuota = '110 GB (118,111,600,640 bytes)'
            ArchiveWarningQuota = '100 GB (107,374,182,400 bytes)'; AutoExpandingArchiveEnabled = $false; InPlaceHolds = @('-mbxexcluded0001')
            ComplianceTagHoldApplied = $false; DelayHoldApplied = $false; DelayReleaseHoldApplied = $false; RetentionHoldEnabled = $false
            RetentionPolicy = 'Default MRM Policy'; LitigationHoldDate = $null; LitigationHoldOwner = ''; LitigationHoldDuration = 'Unlimited'
        },
        [pscustomobject]@{
            DisplayName = 'Reception'; PrimarySmtpAddress = 'reception@contoso.example'; RecipientTypeDetails = 'SharedMailbox'
            ExchangeGuid = [guid]'22222222-2222-2222-2222-222222222222'
            IssueWarningQuota = 'Unlimited'; ProhibitSendQuota = 'Unlimited'; ProhibitSendReceiveQuota = 'Unlimited'
            ArchiveStatus = 'None'; LitigationHoldEnabled = $true; GrantSendOnBehalfTo = @(); ForwardingSmtpAddress = $null
            ForwardingAddress = 'Internal Contact'; DeliverToMailboxAndForward = $false; HiddenFromAddressListsEnabled = $true
            AuditEnabled = $true; AuditLogAgeLimit = '90.00:00:00'; DefaultAuditSet = @('Admin', 'Delegate', 'Owner'); AuditOwner = @()
            AuditDelegate = @(); AuditAdmin = @(); ArchiveGuid = [guid]::Empty; ArchiveName = ''; ArchiveQuota = '0 B (0 bytes)'
            ArchiveWarningQuota = '0 B (0 bytes)'; AutoExpandingArchiveEnabled = $false; InPlaceHolds = @(); ComplianceTagHoldApplied = $false
            DelayHoldApplied = $false; DelayReleaseHoldApplied = $false; RetentionHoldEnabled = $false; RetentionPolicy = 'Default MRM Policy'
            LitigationHoldDate = [datetime]'2026-01-15'; LitigationHoldOwner = 'admin@contoso.example'; LitigationHoldDuration = 'Unlimited'
        }
    )
}
function Get-ResourceMailboxes {
    @(
        [pscustomobject]@{ DisplayName = 'Room 1'; PrimarySmtpAddress = 'room1@contoso.example'; RecipientTypeDetails = 'RoomMailbox'; ExchangeGuid = [guid]'12121212-1212-1212-1212-121212121212'; ResourceCapacity = 8 },
        [pscustomobject]@{ DisplayName = 'Pool van'; PrimarySmtpAddress = 'van@contoso.example'; RecipientTypeDetails = 'EquipmentMailbox'; ExchangeGuid = [guid]'13131313-1313-1313-1313-131313131313'; ResourceCapacity = $null }
    )
}
function Get-EXOMailbox {
    param($Identity, $ResultSize, $RecipientTypeDetails, $Properties)
    Write-StubRead 'Get-EXOMailbox'
    # A named mailbox is found by address; a type filter is applied as Exchange would. Otherwise the scenario's mailboxes.
    if ($null -ne $Identity) {
        $hit = @(@(Get-Mailboxes) + @(Get-ResourceMailboxes) | Where-Object { [string]$_.PrimarySmtpAddress -eq [string]$Identity })
        if ($hit.Count -eq 0) { throw ("The operation couldn't be performed because object '" + $Identity + "' couldn't be found.") }
        return $hit
    }
    if ($null -ne $RecipientTypeDetails) {
        $types = @($RecipientTypeDetails | ForEach-Object { [string]$_ })
        return @(@(Get-Mailboxes) + @(Get-ResourceMailboxes) | Where-Object { $types -contains [string]$_.RecipientTypeDetails })
    }
    Get-Mailboxes
}
function Get-EXOMailboxStatistics {
    param($Identity)
    Write-StubRead 'Get-EXOMailboxStatistics'
    if ((Get-Scenario) -eq 'quota-unknown' -and [string]$Identity -eq '55555555-5555-5555-5555-555555555555') {
        return [pscustomobject]@{ TotalItemSize = $null; ItemCount = $null }
    }
    [pscustomobject]@{ TotalItemSize = '96.5 GB (103,616,086,016 bytes)'; ItemCount = 1234 }
}
function Get-EXOMailboxPermission {
    param($Identity)
    Write-StubRead 'Get-EXOMailboxPermission'
    switch (Get-Scenario) {
        'flags' {
            return @([pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $null; Deny = $null })
        }
        'access' {
            switch ([string]$Identity) {
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' { return @([pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false }) }
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' {
                    # A different recipient with the same display name, named once by sign-in name and once by display name only.
                    return @(
                        [pscustomobject]@{ User = 'alex.other@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false },
                        [pscustomobject]@{ User = 'Alex Wilber'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false }
                    )
                }
                'cccccccc-cccc-cccc-cccc-cccccccccccc' { return @([pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $true }) }
            }
            return @()
        }
    }
    @(
        [pscustomobject]@{ User = 'NT AUTHORITY\SELF'; AccessRights = @('FullAccess', 'ReadPermission'); IsInherited = $false; Deny = $false },
        [pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false },
        [pscustomobject]@{ User = 'admins@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $true; Deny = $false }
    )
}
function Get-EXORecipientPermission {
    param($Identity, $Trustee, $ResultSize)
    Write-StubRead 'Get-EXORecipientPermission'
    switch (Get-Scenario) {
        'flags' {
            return @([pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $null; AccessControlType = $null })
        }
        'access' {
            return @(
                [pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' },
                [pscustomobject]@{ Identity = 'finance@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Deny' }
            )
        }
        'sendas-saturated' {
            # More entries exist than any one request may return: only the requested number comes back.
            $available = 6000
            if ($null -ne $ResultSize -and [int]$ResultSize -lt $available) { $available = [int]$ResultSize }
            return @(1..$available | ForEach-Object {
                [pscustomobject]@{ Identity = ('shared' + $_ + '@contoso.example'); Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' }
            })
        }
    }
    [pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' }
}
function New-StubRecipient([string]$Name, [string]$Display, [string]$Smtp, [string]$ObjectId, [string]$Type) {
    [pscustomobject]@{
        Name = $Name; Alias = $Name; DisplayName = $Display; PrimarySmtpAddress = $Smtp; ExternalDirectoryObjectId = $ObjectId
        RecipientTypeDetails = $Type; DistinguishedName = ('CN=' + $Name)
    }
}
function Get-EXORecipient {
    param($Identity, $Properties)
    Write-StubRead 'Get-EXORecipient'
    switch ([string]$Identity) {
        'staff@contoso.example' { return New-StubRecipient 'staff' 'Staff' 'staff@contoso.example' 'obj-staff' 'MailUniversalDistributionGroup' }
        'team@contoso.example' { return New-StubRecipient 'team' 'Team' 'team@contoso.example' 'obj-team' 'GroupMailbox' }
        'dynamic@contoso.example' { return New-StubRecipient 'dynamic' 'Dynamic' 'dynamic@contoso.example' 'obj-dynamic' 'DynamicDistributionGroup' }
        'alex@contoso.example' { break }
        # Exchange's lookup finds megan by her address, her name and her display name; only the address is an exact identity.
        'megan' { return New-StubRecipient 'megan' 'Megan Bowen' 'megan@contoso.example' 'obj-megan' 'UserMailbox' }
        'megan@contoso.example' { return New-StubRecipient 'megan' 'Megan Bowen' 'megan@contoso.example' 'obj-megan' 'UserMailbox' }
        'Megan Bowen' { return New-StubRecipient 'megan' 'Megan Bowen' 'megan@contoso.example' 'obj-megan' 'UserMailbox' }
        'Shared Name' {
            return @(
                (New-StubRecipient 'shared1' 'Shared Name' 'shared1@contoso.example' 'obj-shared1' 'UserMailbox'),
                (New-StubRecipient 'shared2' 'Shared Name' 'shared2@contoso.example' 'obj-shared2' 'UserMailbox')
            )
        }
        'ghost' { throw "The operation couldn't be performed because object 'ghost' couldn't be found." }
    }
    [pscustomobject]@{ PrimarySmtpAddress = 'alex@contoso.example'; Name = 'alex'; DisplayName = 'Alex Wilber'; DistinguishedName = 'CN=alex' }
}
function Get-EXOMailboxFolderStatistics {
    Write-StubRead 'Get-EXOMailboxFolderStatistics'
    @([pscustomobject]@{ FolderType = 'Inbox'; FolderPath = '/Inbox' }, [pscustomobject]@{ FolderType = 'Calendar'; FolderPath = '/Calendar' })
}
function Get-EXOMailboxFolderPermission {
    Write-StubRead 'Get-EXOMailboxFolderPermission'
    [pscustomobject]@{ User = 'Default'; AccessRights = @('AvailabilityOnly'); SharingPermissionFlags = $null }
}
function Get-InboxRule {
    Write-StubRead 'Get-InboxRule'
    if ((Get-Scenario) -eq 'flags') {
        return @([pscustomobject]@{
            Name = 'Unreadable flags'; Enabled = $null; Priority = 1; From = $null; SubjectContainsWords = $null
            ForwardTo = @('"Outside" [SMTP:x@external.example]'); ForwardAsAttachmentTo = $null; RedirectTo = $null
            MoveToFolder = $null; DeleteMessage = $null; MarkAsRead = $null; StopProcessingRules = $null
        })
    }
    @(
        [pscustomobject]@{
            Name = 'Forward everything'; Enabled = $true; Priority = 1; From = $null; SubjectContainsWords = $null
            ForwardTo = @('"Outside" [SMTP:x@external.example]'); ForwardAsAttachmentTo = $null; RedirectTo = $null
            MoveToFolder = $null; DeleteMessage = $false; MarkAsRead = $true; StopProcessingRules = $false
        },
        [pscustomobject]@{
            Name = 'Newsletters'; Enabled = $true; Priority = 2; From = @('news@example.org'); SubjectContainsWords = $null
            ForwardTo = $null; ForwardAsAttachmentTo = $null; RedirectTo = $null
            MoveToFolder = 'Newsletters'; DeleteMessage = $false; MarkAsRead = $false; StopProcessingRules = $false
        }
    )
}
function Get-AcceptedDomain {
    Write-StubRead 'Get-AcceptedDomain'
    @(
        [pscustomobject]@{ DomainName = 'contoso.example'; DomainType = 'Authoritative'; Default = $true },
        [pscustomobject]@{ DomainName = 'contoso.onmicrosoft.com'; DomainType = 'Authoritative'; Default = $false },
        [pscustomobject]@{ DomainName = 'nodkim.example'; DomainType = 'InternalRelay'; Default = $null }
    )
}
function Get-User {
    param($Identity)
    Write-StubRead 'Get-User'
    if ((Get-Scenario) -eq 'flags') {
        switch ([string]$Identity) {
            '77777777-7777-7777-7777-777777777777' { return [pscustomobject]@{ AccountDisabled = $true } }
            '88888888-8888-8888-8888-888888888888' { return [pscustomobject]@{ AccountDisabled = $false } }
            default { return [pscustomobject]@{ AccountDisabled = $null } }
        }
    }
    [pscustomobject]@{ AccountDisabled = $true }
}
function Get-MessageTraceV2 {
    Write-StubRead 'Get-MessageTraceV2'
    [pscustomobject]@{
        Received = [datetime]::UtcNow.AddDays(-1); SenderAddress = 'alex@contoso.example'; RecipientAddress = 'x@external.example'
        Subject = 'Quarterly report'; Status = 'Delivered'; FromIP = '203.0.113.10'; ToIP = '198.51.100.20'; Size = 20480
        MessageId = '<abc@contoso.example>'; MessageTraceId = [guid]::NewGuid()
    }
}
function Search-UnifiedAuditLog {
    Write-StubRead 'Search-UnifiedAuditLog'
    # Pages of a fixed size; an empty page means the session has no more records.
    $size = 1
    $pages = 1
    if ($env:BDIT_STUB_AUDIT_PAGE_SIZE) { $size = [int]$env:BDIT_STUB_AUDIT_PAGE_SIZE }
    if ($env:BDIT_STUB_AUDIT_PAGES) { $pages = [int]$env:BDIT_STUB_AUDIT_PAGES }
    $script:auditPages++
    if ($script:auditPages -gt $pages) { return }
    foreach ($index in 1..$size) {
        [pscustomobject]@{
            CreationDate = [datetime]::UtcNow.AddDays(-2); UserIds = 'alex@contoso.example'; Operations = 'MailItemsAccessed'
            RecordType = 'ExchangeItem'; AuditData = '{"Workload":"Exchange","ObjectId":"alex@contoso.example","ClientIP":"203.0.113.10"}'
        }
    }
}
# Second Exchange Online pack. Each read is logged like the first pack's.
function Get-DistributionGroupMember {
    param($Identity, $ResultSize)
    Write-StubRead 'Get-DistributionGroupMember'
    $members = @(
        (New-StubRecipient 'alex' 'Alex Wilber' 'alex@contoso.example' 'obj-alex' 'UserMailbox'),
        [pscustomobject]@{ Name = 'Orphan entry'; DisplayName = 'Orphan entry'; PrimarySmtpAddress = ''; ExternalDirectoryObjectId = $null; RecipientTypeDetails = 'MailContact' },
        (New-StubRecipient 'nested' 'Nested group' 'nested@contoso.example' 'obj-nested' 'MailUniversalSecurityGroup')
    )
    if ($null -ne $ResultSize -and [int]$ResultSize -lt $members.Count) { return @($members | Select-Object -First ([int]$ResultSize)) }
    $members
}
function Get-DistributionGroup {
    param($Identity)
    Write-StubRead 'Get-DistributionGroup'
    if ((Get-Scenario) -eq 'flags') { return [pscustomobject]@{ Name = 'staff' } }
    [pscustomobject]@{ Name = 'staff'; ManagedBy = @('megan@contoso.example', 'megan', 'Megan Bowen', 'Shared Name', 'ghost') }
}
function Get-UnifiedGroupLinks {
    param($Identity, $LinkType, $ResultSize)
    Write-StubRead 'Get-UnifiedGroupLinks'
    if ([string]$LinkType -eq 'Owners') { return New-StubRecipient 'megan' 'Megan Bowen' 'megan@contoso.example' 'obj-megan' 'UserMailbox' }
    New-StubRecipient 'alex' 'Alex Wilber' 'alex@contoso.example' 'obj-alex' 'UserMailbox'
}
function Get-TransportRule {
    param($ResultSize)
    Write-StubRead 'Get-TransportRule'
    $rules = @(
        [pscustomobject]@{
            Name = 'Copy finance mail'; Priority = 0; State = 'Enabled'; Mode = 'Enforce'; RedirectMessageTo = $null; BlindCopyTo = @('audit@contoso.example')
            AddToRecipients = $null; CopyTo = $null; RouteMessageOutboundConnector = $null; SetSCL = $null; StopRuleProcessing = $false
            ActivationDate = $null; ExpiryDate = $null; Description = "If the message:`r`n  Is sent to 'finance'`r`nTake the following actions:`r`n  Blind carbon copy"
            Comments = ''; WhenChanged = [datetime]'2026-09-01'
        },
        [pscustomobject]@{
            Name = 'Trust partner'; Priority = 1; State = 'Disabled'; Mode = 'Enforce'; RedirectMessageTo = $null; BlindCopyTo = $null
            AddToRecipients = $null; CopyTo = $null; RouteMessageOutboundConnector = $null; SetSCL = -1; StopRuleProcessing = $true
            ActivationDate = $null; ExpiryDate = $null; Description = 'Set the spam confidence level to -1'; Comments = 'Partner'; WhenChanged = [datetime]'2026-08-01'
        },
        [pscustomobject]@{ Name = 'Unreadable rule'; Priority = 2; Mode = 'Audit'; BlindCopyTo = $null; Description = 'Partly returned' }
    )
    if ($null -ne $ResultSize -and [int]$ResultSize -lt $rules.Count) { return @($rules | Select-Object -First ([int]$ResultSize)) }
    $rules
}
function Get-InboundConnector {
    Write-StubRead 'Get-InboundConnector'
    if ((Get-Scenario) -eq 'flags') {
        return [pscustomobject]@{ Name = 'Half returned'; Enabled = $null; ConnectorType = 'Partner'; SenderDomains = @('partner.example') }
    }
    [pscustomobject]@{
        Name = 'From partner'; Enabled = $true; ConnectorType = 'Partner'; ConnectorSource = 'Default'; SenderDomains = @('partner.example')
        SenderIPAddresses = @('203.0.113.0/24'); RequireTls = $true; TlsSenderCertificateName = 'mail.partner.example'
        IsTransportRuleScoped = $false; Comment = ''; WhenChanged = [datetime]'2026-07-01'
    }
}
function Get-OutboundConnector {
    Write-StubRead 'Get-OutboundConnector'
    [pscustomobject]@{
        Name = 'To smart host'; Enabled = $false; ConnectorType = 'Partner'; ConnectorSource = 'Default'; RecipientDomains = @('*')
        SmartHosts = @('relay.contoso.example'); UseMXRecord = $false; TlsSettings = $null; TlsDomain = $null
        IsTransportRuleScoped = $true; Comment = 'Old relay'; WhenChanged = [datetime]'2026-06-01'
    }
}
function Get-DkimSigningConfig {
    Write-StubRead 'Get-DkimSigningConfig'
    @(
        [pscustomobject]@{
            Domain = 'contoso.example'; Enabled = $true; Status = 'Valid'; Selector1CNAME = 'selector1-contoso-example._domainkey.contoso.onmicrosoft.com'
            Selector2CNAME = 'selector2-contoso-example._domainkey.contoso.onmicrosoft.com'; Selector1KeySize = 2048
            RotateOnDate = [datetime]'2026-03-01'; LastChecked = [datetime]'2026-10-01'
        },
        [pscustomobject]@{ Domain = 'contoso.onmicrosoft.com'; Status = 'Valid' }
    )
}
function Get-MobileDevice {
    param($Mailbox, $ResultSize)
    Write-StubRead 'Get-MobileDevice'
    if ([string]$Mailbox -ne '11111111-1111-1111-1111-111111111111') { return @() }
    $devices = @(
        [pscustomobject]@{ Guid = [guid]'21212121-0000-4000-8000-000000000001'; FriendlyName = 'Recent phone'; DeviceModel = 'iPhone'; DeviceOS = 'iOS 19'; DeviceType = 'iPhone'; ClientType = 'Outlook'; DeviceId = 'R1'; DeviceAccessState = 'Allowed'; IsManaged = $true; IsCompliant = $true; FirstSyncTime = [datetime]'2026-01-01' },
        [pscustomobject]@{ Guid = [guid]'21212121-0000-4000-8000-000000000002'; FriendlyName = 'Old tablet'; DeviceModel = 'Tablet'; DeviceOS = 'Android 14'; DeviceType = 'Android'; ClientType = 'EAS'; DeviceId = 'O2'; DeviceAccessState = 'Allowed'; IsManaged = $false; IsCompliant = $false; FirstSyncTime = [datetime]'2025-01-01' },
        [pscustomobject]@{ Guid = [guid]'21212121-0000-4000-8000-000000000003'; FriendlyName = 'Undated'; DeviceModel = 'Unknown'; DeviceOS = ''; DeviceType = 'EAS'; ClientType = 'EAS'; DeviceId = 'U3'; DeviceAccessState = 'Quarantined'; IsManaged = $null; IsCompliant = $null; FirstSyncTime = $null },
        [pscustomobject]@{ Guid = [guid]'21212121-0000-4000-8000-000000000004'; FriendlyName = 'Unreadable'; DeviceModel = 'Phone'; DeviceOS = 'Android 15'; DeviceType = 'Android'; ClientType = 'EAS'; DeviceId = 'F4'; DeviceAccessState = 'Allowed'; IsManaged = $false; IsCompliant = $false; FirstSyncTime = [datetime]'2026-02-01' }
    )
    # Like Exchange, a read that names no size returns only a default number, here 2, so an unbounded read is caught.
    $size = 2
    if ($null -ne $ResultSize) { $size = [int]$ResultSize }
    if ($size -lt $devices.Count) { return @($devices | Select-Object -First $size) }
    $devices
}
function Get-MobileDeviceStatistics {
    param($Identity)
    Write-StubRead 'Get-MobileDeviceStatistics'
    switch ([string]$Identity) {
        '21212121-0000-4000-8000-000000000001' { return [pscustomobject]@{ LastSuccessSync = [datetime]::UtcNow.AddDays(-2) } }
        '21212121-0000-4000-8000-000000000002' { return [pscustomobject]@{ LastSuccessSync = [datetime]::UtcNow.AddDays(-100) } }
        '21212121-0000-4000-8000-000000000004' { throw 'The statistics for this device are not available.' }
    }
    [pscustomobject]@{ LastSuccessSync = $null }
}
function Get-OrganizationConfig {
    Write-StubRead 'Get-OrganizationConfig'
    if ((Get-Scenario) -eq 'flags') { return [pscustomobject]@{ Name = 'contoso' } }
    [pscustomobject]@{ Name = 'contoso'; AuditDisabled = $false; InPlaceHolds = @('mbxorgwide0001'); AutoExpandingArchiveEnabled = $true }
}
function Get-AdminAuditLogConfig {
    Write-StubRead 'Get-AdminAuditLogConfig'
    if ((Get-Scenario) -eq 'flags') { throw 'The term is not available to your role.' }
    [pscustomobject]@{ UnifiedAuditLogIngestionEnabled = $true }
}
function Get-MailboxAuditBypassAssociation {
    param($Identity)
    Write-StubRead 'Get-MailboxAuditBypassAssociation'
    switch ([string]$Identity) {
        '22222222-2222-2222-2222-222222222222' { return [pscustomobject]@{ AuditBypassEnabled = $true } }
        '77777777-7777-7777-7777-777777777777' { return [pscustomobject]@{ AuditBypassEnabled = $true } }
        '99999999-9999-9999-9999-999999999999' { return [pscustomobject]@{ AuditBypassEnabled = $null } }
    }
    [pscustomobject]@{ AuditBypassEnabled = $false }
}
function Get-HostedContentFilterPolicy {
    Write-StubRead 'Get-HostedContentFilterPolicy'
    @(
        [pscustomobject]@{ Name = 'Default'; Identity = 'Default'; IsDefault = $true; SpamAction = 'MoveToJmf'; HighConfidenceSpamAction = 'Quarantine'; PhishSpamAction = 'Quarantine'; HighConfidencePhishAction = 'Quarantine'; BulkThreshold = 7; QuarantineRetentionPeriod = 30; AllowedSenders = @(); AllowedSenderDomains = @() },
        [pscustomobject]@{ Name = 'Finance strict'; Identity = 'Finance strict'; IsDefault = $false; SpamAction = 'Quarantine'; HighConfidenceSpamAction = 'Quarantine'; PhishSpamAction = 'Quarantine'; HighConfidencePhishAction = 'Quarantine'; QuarantineRetentionPeriod = 30; AllowedSenders = @(); AllowedSenderDomains = @('partner.example', 'supplier.example') },
        [pscustomobject]@{ Name = 'Unused'; Identity = 'Unused'; IsDefault = $false; RecommendedPolicyType = 'Custom'; SpamAction = 'MoveToJmf' },
        [pscustomobject]@{ Name = 'Standard Preset Security Policy1'; Identity = 'Standard Preset Security Policy1'; IsDefault = $false; RecommendedPolicyType = 'Standard'; SpamAction = 'MoveToJmf' },
        [pscustomobject]@{ Name = 'Unmarked'; Identity = 'Unmarked'; IsDefault = $false; SpamAction = 'MoveToJmf' }
    )
}
function Get-HostedContentFilterRule {
    Write-StubRead 'Get-HostedContentFilterRule'
    @(
        [pscustomobject]@{ Name = 'Finance rule'; HostedContentFilterPolicy = 'Finance strict'; State = 'Enabled'; Priority = 0; SentTo = $null; SentToMemberOf = @('finance@contoso.example'); RecipientDomainIs = $null; ExceptIfSentTo = @('cfo@contoso.example'); ExceptIfSentToMemberOf = $null; ExceptIfRecipientDomainIs = $null },
        [pscustomobject]@{ Name = 'Dangling rule'; HostedContentFilterPolicy = 'Removed policy'; Priority = 1; SentTo = $null; SentToMemberOf = $null; RecipientDomainIs = @('contoso.example'); ExceptIfSentTo = $null; ExceptIfSentToMemberOf = $null; ExceptIfRecipientDomainIs = $null }
    )
}
function Get-HostedOutboundSpamFilterPolicy {
    Write-StubRead 'Get-HostedOutboundSpamFilterPolicy'
    [pscustomobject]@{ Name = 'Default'; Identity = 'Default'; IsDefault = $true; RecipientLimitExternalPerHour = 0; RecipientLimitInternalPerHour = 0; RecipientLimitPerDay = 0; ActionWhenThresholdReached = 'BlockUserForToday'; AutoForwardingMode = 'Off' }
}
function Get-HostedOutboundSpamFilterRule { Write-StubRead 'Get-HostedOutboundSpamFilterRule'; @() }
function Get-AntiPhishPolicy {
    Write-StubRead 'Get-AntiPhishPolicy'
    $default = $true
    if ((Get-Scenario) -eq 'flags') { $default = $null }
    [pscustomobject]@{ Name = 'Office365 AntiPhish Default'; Identity = 'Office365 AntiPhish Default'; IsDefault = $default; Enabled = $true; PhishThresholdLevel = 1; EnableSpoofIntelligence = $true; EnableMailboxIntelligence = $true; EnableTargetedUserProtection = $false; EnableOrganizationDomainsProtection = $false; HonorDmarcPolicy = $true }
}
function Get-AntiPhishRule { Write-StubRead 'Get-AntiPhishRule'; @() }
function Get-MalwareFilterPolicy {
    Write-StubRead 'Get-MalwareFilterPolicy'
    [pscustomobject]@{ Name = 'Default'; Identity = 'Default'; IsDefault = $true; EnableFileFilter = $true; FileTypeAction = 'Reject'; ZapEnabled = $true; EnableInternalSenderAdminNotifications = $false }
}
function Get-MalwareFilterRule { Write-StubRead 'Get-MalwareFilterRule'; @() }
function Get-CalendarProcessing {
    param($Identity)
    Write-StubRead 'Get-CalendarProcessing'
    if ([string]$Identity -eq '12121212-1212-1212-1212-121212121212') {
        return [pscustomobject]@{
            AutomateProcessing = 'AutoAccept'; AllowConflicts = $false; AllowRecurringMeetings = $true; BookingWindowInDays = 180
            MaximumDurationInMinutes = 1440; AllBookInPolicy = $true; AllRequestInPolicy = $false; ProcessExternalMeetingMessages = $false
            ResourceDelegates = @('Megan Bowen')
        }
    }
    [pscustomobject]@{ AllowConflicts = $null; BookingWindowInDays = 180 }
}
Export-ModuleMember -Function *
'@

$work = Join-Path ([IO.Path]::GetTempPath()) ('bdit-library-' + [guid]::NewGuid().ToString('N'))
$moduleDirectory = Join-Path (Join-Path (Join-Path $work 'modules') 'ExchangeOnlineManagement') '99.0.0'
[void][IO.Directory]::CreateDirectory($moduleDirectory)

# Produces a Copy script with bdit exactly as the desktop page would, for the synthetic tenant.
function New-Copy([string]$Id, [string]$Name, [string[]]$Form = @(), [string]$Account = 'admin@contoso.example') {
    $path = Join-Path $work ($Name + '.ps1')
    $signIn = @()
    if ($Account) { $signIn = @('--account', $Account) }
    & dotnet $Bdit script --id $Id --copy --tenant $tenant --tenant-name 'Contoso (synthetic)' @signIn --out $path @Form | Out-Host
    if ($LASTEXITCODE -ne 0) { throw ($Id + ': bdit could not produce a Copy script.') }
    return $path
}

# Runs a Copy script in a child PowerShell against the stand-in module and reports what it did.
function Invoke-Copy([string]$Path, [string]$Name, [string]$Scenario = '', [string]$Upn = 'admin@contoso.example', [string]$Tenant = $tenant, [hashtable]$Environment = @{}) {
    $csv = Join-Path $work ($Name + '.csv')
    $reads = Join-Path $work ($Name + '.reads')
    $env:BDIT_STUB_TENANT = $Tenant
    $env:BDIT_STUB_UPN = $Upn
    $env:BDIT_STUB_SCENARIO = $Scenario
    $env:BDIT_STUB_DISCONNECT = Join-Path $work ($Name + '.disconnected')
    $env:BDIT_STUB_READS = $reads
    $env:BDIT_STUB_AUDIT_PAGE_SIZE = ''
    $env:BDIT_STUB_AUDIT_PAGES = ''
    foreach ($key in $Environment.Keys) { Set-Item -LiteralPath ('env:' + $key) -Value $Environment[$key] }
    # Windows PowerShell 5.1 turns a child's stderr into a terminating error under 'Stop'; the exit code is what is checked.
    $output = & { $ErrorActionPreference = 'Continue'; & $PowerShell -NoLogo -NoProfile -NonInteractive -File $Path -OutputCsv $csv 2>&1 | Out-String }
    $code = $LASTEXITCODE
    $rows = @()
    if (Test-Path -LiteralPath $csv) { $rows = @(Import-Csv -LiteralPath $csv) }
    $readList = @()
    if (Test-Path -LiteralPath $reads) { $readList = @(Get-Content -LiteralPath $reads | Where-Object { $_ }) }
    [pscustomobject]@{
        # Whitespace is removed because Windows PowerShell 5.1 may wrap a child's error or warning text at any column.
        Code = $code; Output = ([string]$output -replace '\s', ''); Csv = $csv; CsvWritten = (Test-Path -LiteralPath $csv); Rows = $rows
        Disconnected = (Test-Path -LiteralPath $env:BDIT_STUB_DISCONNECT); Reads = $readList
    }
}

# A missing column reads as $null, so a check against an older output shape fails rather than stopping the run.
function Get-Cell([object]$Row, [string]$Column) {
    if ($null -ne $Row -and $Row.PSObject.Properties[$Column]) { return [string]$Row.$Column }
    return $null
}

function Find-Row([object[]]$Rows, [string]$Column, [string]$Value) {
    return @($Rows | Where-Object { (Get-Cell $_ $Column) -eq $Value })
}

$failures = [System.Collections.Generic.List[string]]::new()
function Test-Case([string]$Case, [scriptblock]$Check) {
    $passed = $false
    try { $passed = [bool](& $Check) } catch { $passed = $false; $Case = $Case + ' (' + $_.Exception.Message + ')' }
    if ($passed) { Write-Output ('  pass: ' + $Case) } else { $failures.Add($Case); Write-Output ('  FAIL: ' + $Case) }
}

try {
    [IO.File]::WriteAllText((Join-Path $moduleDirectory 'ExchangeOnlineManagement.psm1'), $stub)
    [IO.File]::WriteAllText((Join-Path $moduleDirectory 'ExchangeOnlineManagement.psd1'),
        "@{ ModuleVersion = '99.0.0'; RootModule = 'ExchangeOnlineManagement.psm1'; GUID = '$([guid]::NewGuid())'; FunctionsToExport = '*' }")
    $modulePath = Join-Path $work 'modules'
    $env:PSModulePath = $modulePath + [IO.Path]::PathSeparator + $env:PSModulePath

    $manifests = @(Get-ChildItem -LiteralPath $Library -Recurse -Filter '*.json' | Where-Object { $_.Name -ne 'registry.json' })
    $copies = @()
    $copyById = @{}
    foreach ($manifestFile in $manifests) {
        $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
        if (-not $samples.ContainsKey($manifest.id)) { throw ($manifest.id + ' has no sample form in this test.') }
        $copy = New-Copy $manifest.id $manifest.id @($samples[$manifest.id])
        $copies += $copy
        $copyById[$manifest.id] = $copy

        $run = Invoke-Copy $copy $manifest.id
        if ($run.Code -ne 0) { throw ($manifest.id + ': the Copy script failed against the synthetic module.' + [Environment]::NewLine + $run.Output) }
        if (-not $run.Disconnected) { throw ($manifest.id + ': the Copy script did not disconnect.') }
        $header = ((Get-Content -LiteralPath $run.Csv -TotalCount 1) -replace '"', '') -split ','
        if (($header -join ',') -cne (@($manifest.outputSchema.columns) -join ',')) {
            throw ($manifest.id + ': CSV columns ' + ($header -join ',') + ' differ from the manifest.')
        }
        if ($run.Rows.Count -lt 1) { throw ($manifest.id + ': no synthetic rows were written.') }
        Write-Output ($manifest.id + ': ' + $run.Rows.Count + ' synthetic row(s), manifest columns, disconnected.')

        # Signed in to another tenant: the wrapper must refuse before reading, still disconnect, and write nothing.
        $wrong = Invoke-Copy $copy ($manifest.id + '-wrong') -Tenant '9b2c1d4e-0000-4000-8000-000000000000'
        if ($wrong.Code -eq 0 -or $wrong.CsvWritten) { throw ($manifest.id + ': a different tenant was not refused.') }
        if (-not $wrong.Disconnected) { throw ($manifest.id + ': the refused run did not disconnect.') }
        if ($wrong.Reads.Count -ne 0) { throw ($manifest.id + ': the refused run read ' + ($wrong.Reads -join ', ') + '.') }
    }
    $check = Join-Path $PSScriptRoot 'Test-ScriptLibrary.ps1'
    & $check -Library $Library -CopyScript $copies

    # Regressions for Astra's review of PR #46 (AST-20261008-05 to -09). Each case is recorded, then all failures are reported together.
    Write-Output 'Signed-in account (AST-20261008-07):'
    foreach ($id in @($copyById.Keys | Sort-Object)) {
        $other = Invoke-Copy $copyById[$id] ($id + '-account') -Upn 'other@contoso.example'
        Test-Case ($id + ': another account in the same tenant is refused, nothing read or written, disconnected') {
            $other.Code -ne 0 -and -not $other.CsvWritten -and $other.Reads.Count -eq 0 -and $other.Disconnected -and
                $other.Output.Contains('other@contoso.example') -and $other.Output.Contains('Nothingwasread')
        }
    }
    $upperCase = Invoke-Copy $copyById['exo.mailbox-inventory'] 'account-case' -Upn 'ADMIN@Contoso.Example'
    Test-Case 'the reviewed account matches regardless of letter case' { $upperCase.Code -eq 0 -and $upperCase.Rows.Count -ge 1 }
    $anyCopy = New-Copy 'exo.mailbox-inventory' 'any-account' -Account ''
    $any = Invoke-Copy $anyCopy 'any-account' -Upn 'other@contoso.example'
    Test-Case 'with no reviewed account, the account chosen at the prompt is used' { $any.Code -eq 0 -and $any.Rows.Count -ge 1 }

    Write-Output 'Quota audit keeps what it cannot measure (AST-20261008-05):'
    $quotaDefault = New-Copy 'exo.quota-audit' 'quota-default'
    $quotaAll = New-Copy 'exo.quota-audit' 'quota-all' @('--IncludeAll', 'true')
    foreach ($pair in @(@($quotaDefault, 'default inputs'), @($quotaAll, 'IncludeAll'))) {
        $run = Invoke-Copy $pair[0] ('quota-' + $pair[1].Replace(' ', '-')) -Scenario 'quota-unknown'
        $label = $pair[1]
        Test-Case ('quota (' + $label + '): Unlimited quota listed as Unknown with a reason') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'unlimited@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and (Get-Cell $row[0] 'Reason') -like '*Unlimited*'
        }
        Test-Case ('quota (' + $label + '): malformed quota listed as Unknown with a reason') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'malformed@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and (Get-Cell $row[0] 'Reason') -like '*quota*'
        }
        Test-Case ('quota (' + $label + '): missing size listed as Unknown with a reason') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'nosize@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and (Get-Cell $row[0] 'Reason') -like '*size*'
        }
        Test-Case ('quota (' + $label + '): a measured mailbox over the threshold is still listed, and the run says some were not measured') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'over@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'AtOrAboveThreshold' -and $run.Output.Contains('BDIT:UNKNOWN')
        }
    }

    Write-Output 'True, false and not returned stay distinct (AST-20261008-06):'
    $flagForms = @{
        'exo.mailbox-inventory' = @()
        'exo.shared-mailboxes' = @()
        'exo.external-forwarding' = @('--IncludeInboxRules', 'true')
        'exo.inbox-rules' = @('--Mailbox', 'null@contoso.example')
        'exo.mailbox-permissions' = @('--IncludeInherited', 'true')
        'exo.user-mailbox-access' = @('--User', 'alex@contoso.example')
    }
    $flags = @{}
    foreach ($id in $flagForms.Keys) { $flags[$id] = Invoke-Copy (New-Copy $id ($id + '-flags') $flagForms[$id]) ($id + '-flags') -Scenario 'flags' }
    foreach ($expect in @(@('true@contoso.example', 'True'), @('false@contoso.example', 'False'), @('null@contoso.example', 'Unknown'))) {
        $address = $expect[0]; $value = $expect[1]
        Test-Case ('inventory: litigation hold ' + $value + ' for ' + $address) {
            $row = @(Find-Row $flags['exo.mailbox-inventory'].Rows 'PrimarySmtpAddress' $address)
            $row.Count -eq 1 -and (Get-Cell $row[0] 'LitigationHoldEnabled') -ceq $value -and (((Get-Cell $row[0] 'Notes') -ne '') -eq ($value -eq 'Unknown'))
        }
        Test-Case ('shared: sign-in blocked, hidden and hold ' + $value + ' for ' + $address) {
            $row = @(Find-Row $flags['exo.shared-mailboxes'].Rows 'PrimarySmtpAddress' $address)
            $row.Count -eq 1 -and (Get-Cell $row[0] 'SignInBlocked') -ceq $value -and (Get-Cell $row[0] 'HiddenFromAddressLists') -ceq $value -and
                (Get-Cell $row[0] 'LitigationHoldEnabled') -ceq $value -and (((Get-Cell $row[0] 'Notes') -ne '') -eq ($value -eq 'Unknown'))
        }
        Test-Case ('forwarding: keeps a copy ' + $value + ' for ' + $address) {
            $row = @(Find-Row $flags['exo.external-forwarding'].Rows 'Mailbox' $address | Where-Object { (Get-Cell $_ 'Source') -eq 'MailboxForwarding' })
            $row.Count -eq 1 -and (Get-Cell $row[0] 'KeepsCopy') -ceq $value
        }
    }
    Test-Case 'forwarding: an inbox rule whose delete action was not returned does not claim a copy is kept' {
        $rows = @($flags['exo.external-forwarding'].Rows | Where-Object { (Get-Cell $_ 'Source') -eq 'InboxRule' })
        $rows.Count -ge 1 -and @($rows | Where-Object { (Get-Cell $_ 'KeepsCopy') -cne 'Unknown' }).Count -eq 0
    }
    Test-Case 'inbox rules: flags that were not returned are Unknown, not False' {
        $row = @($flags['exo.inbox-rules'].Rows)
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Enabled') -ceq 'Unknown' -and (Get-Cell $row[0] 'DeleteMessage') -ceq 'Unknown' -and
            (Get-Cell $row[0] 'MarkAsRead') -ceq 'Unknown' -and (Get-Cell $row[0] 'StopProcessingRules') -ceq 'Unknown'
    }
    Test-Case 'mailbox permissions: inheritance and deny that were not returned are Unknown' {
        $rows = @($flags['exo.mailbox-permissions'].Rows | Where-Object { (Get-Cell $_ 'Permission') -ne 'SendOnBehalf' })
        $rows.Count -ge 2 -and @($rows | Where-Object { (Get-Cell $_ 'IsInherited') -cne 'Unknown' -or (Get-Cell $_ 'Deny') -cne 'Unknown' }).Count -eq 0
    }
    Test-Case 'user mailbox access: inheritance that was not returned is Unknown and the entry is not called granted' {
        $rows = @($flags['exo.user-mailbox-access'].Rows)
        $rows.Count -ge 1 -and @($rows | Where-Object { (Get-Cell $_ 'IsInherited') -cne 'Unknown' -or (Get-Cell $_ 'Status') -eq 'Granted' }).Count -eq 0
    }
    Test-Case 'the default run keeps known values as True and False' {
        $row = @(Find-Row (Invoke-Copy $copyById['exo.shared-mailboxes'] 'shared-known').Rows 'PrimarySmtpAddress' 'reception@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'HiddenFromAddressLists') -ceq 'True' -and (Get-Cell $row[0] 'Notes') -eq ''
    }

    Write-Output 'A user''s mailbox access uses exact identities and keeps deny entries (AST-20261008-08):'
    $access = Invoke-Copy $copyById['exo.user-mailbox-access'] 'access' -Scenario 'access'
    Test-Case 'access: an exact identity with an allow entry is Granted' {
        $row = @(Find-Row $access.Rows 'Mailbox' 'exact@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'FullAccess' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Granted'
    }
    Test-Case 'access: another recipient with the same display name is never Granted to this user' {
        $rows = @(Find-Row $access.Rows 'Mailbox' 'namesake@contoso.example')
        @($rows | Where-Object { (Get-Cell $_ 'Status') -ne 'Unresolved' }).Count -eq 0 -and
            @($rows | Where-Object { (Get-Cell $_ 'Holder') -eq 'alex.other@contoso.example' }).Count -eq 0
    }
    Test-Case 'access: an entry naming only the display name is kept as Unresolved' {
        $rows = @(Find-Row $access.Rows 'Mailbox' 'namesake@contoso.example' | Where-Object { (Get-Cell $_ 'Holder') -eq 'Alex Wilber' })
        $rows.Count -eq 1 -and (Get-Cell $rows[0] 'Status') -eq 'Unresolved' -and (Get-Cell $rows[0] 'Notes') -ne ''
    }
    Test-Case 'access: a display-name Send on Behalf delegate is Unresolved, an exact one Granted' {
        $name = @(Find-Row $access.Rows 'Mailbox' 'exact@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'SendOnBehalf' })
        $exact = @(Find-Row $access.Rows 'Mailbox' 'denied@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'SendOnBehalf' })
        $name.Count -eq 1 -and (Get-Cell $name[0] 'Status') -eq 'Unresolved' -and $exact.Count -eq 1 -and (Get-Cell $exact[0] 'Status') -eq 'Granted'
    }
    Test-Case 'access: a deny Full Access entry is kept as Denied' {
        $row = @(Find-Row $access.Rows 'Mailbox' 'denied@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'FullAccess' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Denied'
    }
    Test-Case 'access: a deny Send As entry is kept as Denied, an allow one Granted' {
        $deny = @(Find-Row $access.Rows 'Mailbox' 'finance@contoso.example')
        $allow = @(Find-Row $access.Rows 'Mailbox' 'reception@contoso.example')
        $deny.Count -eq 1 -and (Get-Cell $deny[0] 'Status') -eq 'Denied' -and $allow.Count -eq 1 -and (Get-Cell $allow[0] 'Status') -eq 'Granted'
    }

    Write-Output 'A stop at a limit is never presented as complete (AST-20261008-09):'
    $auditCopy = New-Copy 'exo.unified-audit-search' 'audit-cap' @('--StartDate', $from, '--EndDate', $to, '--UserIds', 'alex@contoso.example', '--MaxRows', '3')
    $atCap = Invoke-Copy $auditCopy 'audit-more' -Environment @{ BDIT_STUB_AUDIT_PAGE_SIZE = '3'; BDIT_STUB_AUDIT_PAGES = '2' }
    Test-Case 'audit: a page ending exactly at the limit with another page waiting is marked partial' {
        $atCap.Code -eq 0 -and $atCap.Rows.Count -eq 3 -and $atCap.Output.Contains('BDIT:PARTIAL')
    }
    $exact = Invoke-Copy $auditCopy 'audit-exact' -Environment @{ BDIT_STUB_AUDIT_PAGE_SIZE = '3'; BDIT_STUB_AUDIT_PAGES = '1' }
    Test-Case 'audit: exactly the limit with nothing more is complete, proved by an empty next page' {
        $exact.Code -eq 0 -and $exact.Rows.Count -eq 3 -and -not $exact.Output.Contains('BDIT:PARTIAL')
    }
    $midPage = Invoke-Copy $auditCopy 'audit-mid' -Environment @{ BDIT_STUB_AUDIT_PAGE_SIZE = '5'; BDIT_STUB_AUDIT_PAGES = '1' }
    Test-Case 'audit: a stop inside a page is marked partial' { $midPage.Code -eq 0 -and $midPage.Rows.Count -eq 3 -and $midPage.Output.Contains('BDIT:PARTIAL') }
    $saturated = Invoke-Copy $copyById['exo.user-mailbox-access'] 'sendas-saturated' -Scenario 'sendas-saturated'
    Test-Case 'access: a Send As result at the request limit is marked partial' {
        $saturated.Code -eq 0 -and $saturated.Output.Contains('BDIT:PARTIAL') -and @(Find-Row $saturated.Rows 'Permission' 'SendAs').Count -le 5000
    }

    # Second Exchange Online pack: the same rules applied to each new item.
    Write-Output 'Group members and owners use exact identities only:'
    $groups = Invoke-Copy $copyById['exo.group-members'] 'groups'
    Test-Case 'groups: a member returned with an address and object ID is Exact' {
        $row = @(Find-Row $groups.Rows 'Group' 'staff@contoso.example' | Where-Object { (Get-Cell $_ 'Relationship') -eq 'Member' -and (Get-Cell $_ 'MemberAddress') -eq 'alex@contoso.example' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Exact' -and (Get-Cell $row[0] 'MemberObjectId') -eq 'obj-alex'
    }
    Test-Case 'groups: a member returned without an address or object ID is Unresolved, not dropped' {
        $row = @(Find-Row $groups.Rows 'AsReturned' 'Orphan entry')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unresolved' -and (Get-Cell $row[0] 'Notes') -ne ''
    }
    Test-Case 'groups: an owner returned as an exact address is Resolved to that recipient' {
        $row = @(Find-Row $groups.Rows 'AsReturned' 'megan@contoso.example' | Where-Object { (Get-Cell $_ 'Relationship') -eq 'Owner' -and (Get-Cell $_ 'Group') -eq 'staff@contoso.example' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Resolved' -and (Get-Cell $row[0] 'MemberObjectId') -eq 'obj-megan'
    }
    Test-Case 'groups: an owner returned only as a Name is Unresolved and given no address, even when one recipient has that Name' {
        $row = @(Find-Row $groups.Rows 'AsReturned' 'megan' | Where-Object { (Get-Cell $_ 'Relationship') -eq 'Owner' -and (Get-Cell $_ 'Group') -eq 'staff@contoso.example' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unresolved' -and (Get-Cell $row[0] 'MemberAddress') -eq '' -and (Get-Cell $row[0] 'MemberObjectId') -eq ''
    }
    Test-Case 'groups: an owner matched only on a display name is Unresolved and given no address' {
        $row = @(Find-Row $groups.Rows 'AsReturned' 'Megan Bowen' | Where-Object { (Get-Cell $_ 'Relationship') -eq 'Owner' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unresolved' -and (Get-Cell $row[0] 'MemberAddress') -eq '' -and (Get-Cell $row[0] 'MemberObjectId') -eq ''
    }
    Test-Case 'groups: an owner matching several recipients, or none, is Unresolved' {
        $shared = @(Find-Row $groups.Rows 'AsReturned' 'Shared Name')
        $ghost = @(Find-Row $groups.Rows 'AsReturned' 'ghost')
        $shared.Count -eq 1 -and (Get-Cell $shared[0] 'Status') -eq 'Unresolved' -and $ghost.Count -eq 1 -and (Get-Cell $ghost[0] 'Status') -eq 'Unresolved'
    }
    Test-Case 'groups: Microsoft 365 group owners come back as recipients and are Exact' {
        $row = @(Find-Row $groups.Rows 'Group' 'team@contoso.example' | Where-Object { (Get-Cell $_ 'Relationship') -eq 'Owner' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Exact' -and (Get-Cell $row[0] 'MemberAddress') -eq 'megan@contoso.example'
    }
    Test-Case 'groups: a dynamic group is NotExpanded and the run warns BDIT:UNKNOWN' {
        $row = @(Find-Row $groups.Rows 'Group' 'dynamic@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'NotExpanded' -and $groups.Output.Contains('BDIT:UNKNOWN')
    }
    $groupOwners = Invoke-Copy (New-Copy 'exo.group-members' 'groups-flags' @('--Group', 'staff@contoso.example')) 'groups-flags' -Scenario 'flags'
    Test-Case 'groups: owners that were not returned are one Unknown row, not an empty list' {
        $row = @(Find-Row $groupOwners.Rows 'Relationship' 'Owner')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and $groupOwners.Output.Contains('BDIT:UNKNOWN')
    }
    $groupCap = Invoke-Copy (New-Copy 'exo.group-members' 'groups-cap' @('--Group', 'staff@contoso.example', '--IncludeOwners', 'false', '--MaxMembers', '2')) 'groups-cap'
    Test-Case 'groups: a membership list longer than the limit is marked partial' {
        $groupCap.Code -eq 0 -and $groupCap.Rows.Count -eq 2 -and $groupCap.Output.Contains('BDIT:PARTIAL')
    }
    $groupNotGroup = Invoke-Copy (New-Copy 'exo.group-members' 'groups-mailbox' @('--Group', 'alex@contoso.example')) 'groups-mailbox'
    Test-Case 'groups: a recipient that is not a group is refused, not listed as empty' { $groupNotGroup.Code -ne 0 -and -not $groupNotGroup.CsvWritten }

    Write-Output 'Mail flow rules and connectors keep Unknown distinct:'
    $transport = Invoke-Copy $copyById['exo.transport-rules'] 'transport'
    Test-Case 'transport: a blind copy action is RedirectsOrCopies True' {
        $row = @(Find-Row $transport.Rows 'Name' 'Copy finance mail')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'RedirectsOrCopies') -ceq 'True' -and (Get-Cell $row[0] 'BypassesSpamFiltering') -ceq 'False' -and (Get-Cell $row[0] 'Description') -notmatch "`n"
    }
    Test-Case 'transport: SCL -1 is BypassesSpamFiltering True and returned-empty actions are False' {
        $row = @(Find-Row $transport.Rows 'Name' 'Trust partner')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'BypassesSpamFiltering') -ceq 'True' -and (Get-Cell $row[0] 'RedirectsOrCopies') -ceq 'False' -and (Get-Cell $row[0] 'StopRuleProcessing') -ceq 'True'
    }
    Test-Case 'transport: actions and state that were not returned are Unknown, not False' {
        $row = @(Find-Row $transport.Rows 'Name' 'Unreadable rule')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'State') -ceq 'Unknown' -and (Get-Cell $row[0] 'RedirectsOrCopies') -ceq 'Unknown' -and
            (Get-Cell $row[0] 'BypassesSpamFiltering') -ceq 'Unknown' -and (Get-Cell $row[0] 'StopRuleProcessing') -ceq 'Unknown'
    }
    $transportEnabled = Invoke-Copy (New-Copy 'exo.transport-rules' 'transport-enabled' @('--OnlyEnabled', 'true')) 'transport-enabled'
    Test-Case 'transport: only enabled drops a disabled rule and keeps one whose state is Unknown' {
        @(Find-Row $transportEnabled.Rows 'Name' 'Trust partner').Count -eq 0 -and @(Find-Row $transportEnabled.Rows 'Name' 'Unreadable rule').Count -eq 1
    }
    $transportCap = Invoke-Copy (New-Copy 'exo.transport-rules' 'transport-cap' @('--MaxRows', '2')) 'transport-cap'
    Test-Case 'transport: more rules than the limit is marked partial' { $transportCap.Rows.Count -eq 2 -and $transportCap.Output.Contains('BDIT:PARTIAL') }
    $connectors = Invoke-Copy $copyById['exo.connectors'] 'connectors'
    Test-Case 'connectors: required TLS inbound, and an outbound TLS setting returned empty is NotSet' {
        $in = @(Find-Row $connectors.Rows 'Direction' 'Inbound'); $out = @(Find-Row $connectors.Rows 'Direction' 'Outbound')
        $in.Count -eq 1 -and (Get-Cell $in[0] 'TlsRequirement') -eq 'Required' -and (Get-Cell $in[0] 'Enabled') -ceq 'True' -and
            $out.Count -eq 1 -and (Get-Cell $out[0] 'TlsRequirement') -eq 'NotSet' -and (Get-Cell $out[0] 'Enabled') -ceq 'False'
    }
    $connectorFlags = Invoke-Copy (New-Copy 'exo.connectors' 'connectors-flags' @('--OnlyEnabled', 'true')) 'connectors-flags' -Scenario 'flags'
    Test-Case 'connectors: values that were not returned are Unknown; only enabled keeps Unknown and drops disabled' {
        $in = @(Find-Row $connectorFlags.Rows 'Direction' 'Inbound')
        $in.Count -eq 1 -and (Get-Cell $in[0] 'Enabled') -ceq 'Unknown' -and (Get-Cell $in[0] 'TlsRequirement') -eq 'Unknown' -and
            (Get-Cell $in[0] 'ConnectorSource') -eq 'Unknown' -and @(Find-Row $connectorFlags.Rows 'Direction' 'Outbound').Count -eq 0
    }

    Write-Output 'Accepted domains and DKIM, read from Exchange only:'
    $dkim = Invoke-Copy $copyById['exo.domains-dkim'] 'dkim'
    Test-Case 'dkim: a configured, enabled domain is True with its selectors' {
        $row = @(Find-Row $dkim.Rows 'Domain' 'contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'DkimEnabled') -ceq 'True' -and (Get-Cell $row[0] 'IsDefault') -ceq 'True' -and (Get-Cell $row[0] 'Selector1CNAME') -like 'selector1-*'
    }
    Test-Case 'dkim: a configuration whose enabled state was not returned is Unknown and warned' {
        $row = @(Find-Row $dkim.Rows 'Domain' 'contoso.onmicrosoft.com')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'DkimEnabled') -ceq 'Unknown' -and $dkim.Output.Contains('BDIT:UNKNOWN')
    }
    Test-Case 'dkim: a domain with no configuration is NotConfigured, and an unreturned default flag is Unknown' {
        $row = @(Find-Row $dkim.Rows 'Domain' 'nodkim.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'DkimConfigured') -ceq 'False' -and (Get-Cell $row[0] 'DkimEnabled') -eq 'NotConfigured' -and (Get-Cell $row[0] 'IsDefault') -ceq 'Unknown'
    }
    $dkimNamed = Invoke-Copy (New-Copy 'exo.domains-dkim' 'dkim-named' @('--Domain', 'Contoso.example, other.example')) 'dkim-named'
    Test-Case 'dkim: named domains are filtered, and one that is not accepted is listed as NotAccepted' {
        $dkimNamed.Rows.Count -eq 2 -and (Get-Cell @(Find-Row $dkimNamed.Rows 'Domain' 'other.example')[0] 'DomainType') -eq 'NotAccepted'
    }

    Write-Output 'Mobile devices keep devices that cannot be dated:'
    $mobile = Invoke-Copy $copyById['exo.mobile-devices'] 'mobile'
    # Four devices exist and the stand-in returns only two unless a size is asked for, so this also proves the read is sized.
    Test-Case 'mobile: each device is listed against the mailbox address, an undated one as Unknown with a warning' {
        $undated = @(Find-Row $mobile.Rows 'DeviceId' 'U3')
        $mobile.Rows.Count -eq 4 -and @(Find-Row $mobile.Rows 'Mailbox' 'alex@contoso.example').Count -eq 4 -and $undated.Count -eq 1 -and
            (Get-Cell $undated[0] 'SyncStatus') -eq 'Unknown' -and (Get-Cell $undated[0] 'IsManaged') -ceq 'Unknown' -and $mobile.Output.Contains('BDIT:UNKNOWN')
    }
    Test-Case 'mobile: a device whose statistics read fails is Unknown with the reason, and the run continues' {
        $row = @(Find-Row $mobile.Rows 'DeviceId' 'F4')
        $mobile.Code -eq 0 -and $row.Count -eq 1 -and (Get-Cell $row[0] 'SyncStatus') -eq 'Unknown' -and (Get-Cell $row[0] 'Notes') -like '*statistics read failed*'
    }
    $stale = Invoke-Copy (New-Copy 'exo.mobile-devices' 'mobile-stale' @('--StaleDays', '30')) 'mobile-stale'
    Test-Case 'mobile: the stale filter drops a recent device, keeps a stale one and keeps an undated one' {
        @(Find-Row $stale.Rows 'DeviceId' 'R1').Count -eq 0 -and (Get-Cell @(Find-Row $stale.Rows 'DeviceId' 'O2')[0] 'SyncStatus') -eq 'Stale' -and
            (Get-Cell @(Find-Row $stale.Rows 'DeviceId' 'U3')[0] 'SyncStatus') -eq 'Unknown'
    }
    $mobileCap = Invoke-Copy (New-Copy 'exo.mobile-devices' 'mobile-cap' @('--MaxRows', '2')) 'mobile-cap'
    Test-Case 'mobile: more devices than the limit is marked partial, and the device read asks for one more than the limit' {
        $mobileCap.Rows.Count -eq 2 -and $mobileCap.Output.Contains('BDIT:PARTIAL')
    }
    $mobileStaleCap = Invoke-Copy (New-Copy 'exo.mobile-devices' 'mobile-stale-cap' @('--StaleDays', '30', '--MaxRows', '1')) 'mobile-stale-cap'
    Test-Case 'mobile: devices left out by the stale filter do not hide a truncated device read' {
        $mobileStaleCap.Code -eq 0 -and $mobileStaleCap.Rows.Count -le 1 -and $mobileStaleCap.Output.Contains('BDIT:PARTIAL')
    }
    $noSync = Invoke-Copy (New-Copy 'exo.mobile-devices' 'mobile-nosync' @('--IncludeLastSync', 'false', '--StaleDays', '30')) 'mobile-nosync'
    Test-Case 'mobile: a stale filter without reading last sync is refused before any device is read' {
        $noSync.Code -ne 0 -and -not $noSync.CsvWritten -and @($noSync.Reads | Where-Object { $_ -like 'Get-Mobile*' }).Count -eq 0
    }

    Write-Output 'Mailbox auditing, holds and archives keep Unknown distinct:'
    $audit = Invoke-Copy $copyById['exo.mailbox-audit'] 'mailbox-audit'
    Test-Case 'audit: a bypassed account is Bypassed, an audited one NoneFound' {
        (Get-Cell @(Find-Row $audit.Rows 'Mailbox' 'reception@contoso.example')[0] 'Finding') -eq 'Bypassed' -and
            (Get-Cell @(Find-Row $audit.Rows 'Mailbox' 'alex@contoso.example')[0] 'Finding') -eq 'NoneFound' -and
            (Get-Cell @(Find-Row $audit.Rows 'Mailbox' 'alex@contoso.example')[0] 'UnifiedAuditIngestion') -ceq 'True'
    }
    $auditFlags = Invoke-Copy $copyById['exo.mailbox-audit'] 'mailbox-audit-flags' -Scenario 'flags'
    Test-Case 'audit: settings that were not returned are Unknown, the finding is never NoneFound, and the run warns' {
        $null = @(Find-Row $auditFlags.Rows 'Mailbox' 'null@contoso.example')
        $row = @(Find-Row $auditFlags.Rows 'Mailbox' 'null@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'AuditEnabled') -ceq 'Unknown' -and (Get-Cell $row[0] 'AuditBypassEnabled') -ceq 'Unknown' -and
            (Get-Cell $row[0] 'OrganisationAuditDisabled') -ceq 'Unknown' -and (Get-Cell $row[0] 'Finding') -eq 'Unknown' -and
            @($auditFlags.Rows | Where-Object { (Get-Cell $_ 'Finding') -eq 'NoneFound' }).Count -eq 0 -and $auditFlags.Output.Contains('BDIT:UNKNOWN')
    }
    Test-Case 'audit: unified audit ingestion that could not be read is Unknown with the reason, not False' {
        @($auditFlags.Rows | Where-Object { (Get-Cell $_ 'UnifiedAuditIngestion') -cne 'Unknown' }).Count -eq 0 -and
            (Get-Cell @(Find-Row $auditFlags.Rows 'Mailbox' 'false@contoso.example')[0] 'Notes') -like '*could not be read*'
    }
    $holds = Invoke-Copy (New-Copy 'exo.mailbox-holds' 'holds-flags' @('--OnlyOnHold', 'true')) 'holds-flags' -Scenario 'flags'
    Test-Case 'holds: a mailbox with holds is MailboxHold, none is left out, and unreturned holds are Unknown and kept' {
        $on = @(Find-Row $holds.Rows 'Mailbox' 'true@contoso.example'); $unknown = @(Find-Row $holds.Rows 'Mailbox' 'null@contoso.example')
        $on.Count -eq 1 -and (Get-Cell $on[0] 'HoldStatus') -eq 'MailboxHold' -and (Get-Cell $on[0] 'InPlaceHolds') -eq 'mbx0a1b2c3d4e5f' -and
            @(Find-Row $holds.Rows 'Mailbox' 'false@contoso.example').Count -eq 0 -and $unknown.Count -eq 1 -and
            (Get-Cell $unknown[0] 'HoldStatus') -eq 'Unknown' -and (Get-Cell $unknown[0] 'InPlaceHolds') -ceq 'Unknown' -and
            (Get-Cell $unknown[0] 'LitigationHoldEnabled') -ceq 'Unknown' -and (Get-Cell $unknown[0] 'OrganisationHolds') -ceq 'Unknown' -and $holds.Output.Contains('BDIT:UNKNOWN')
    }
    $holdsDefault = Invoke-Copy $copyById['exo.mailbox-holds'] 'holds'
    Test-Case 'holds: a mailbox whose only entry is an exclusion is NoMailboxHold, says so, and organisation-wide holds are shown' {
        $row = @(Find-Row $holdsDefault.Rows 'Mailbox' 'alex@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'HoldStatus') -eq 'NoMailboxHold' -and (Get-Cell $row[0] 'OrganisationHolds') -eq 'mbxorgwide0001' -and
            (Get-Cell $row[0] 'InPlaceHolds') -eq '-mbxexcluded0001' -and (Get-Cell $row[0] 'Notes') -like '*excludes this mailbox*' -and
            (Get-Cell @(Find-Row $holdsDefault.Rows 'Mailbox' 'reception@contoso.example')[0] 'HoldStatus') -eq 'MailboxHold'
    }
    $archives = Invoke-Copy (New-Copy 'exo.archive-mailboxes' 'archive-flags' @('--OnlyWithArchive', 'true')) 'archive-flags' -Scenario 'flags'
    Test-Case 'archive: an archive is True, no archive is left out, and an unreturned archive GUID is Unknown and kept' {
        $on = @(Find-Row $archives.Rows 'Mailbox' 'true@contoso.example'); $unknown = @(Find-Row $archives.Rows 'Mailbox' 'null@contoso.example')
        $on.Count -eq 1 -and (Get-Cell $on[0] 'HasArchive') -ceq 'True' -and @(Find-Row $archives.Rows 'Mailbox' 'false@contoso.example').Count -eq 0 -and
            $unknown.Count -eq 1 -and (Get-Cell $unknown[0] 'HasArchive') -ceq 'Unknown' -and (Get-Cell $unknown[0] 'AutoExpandingArchive') -ceq 'Unknown' -and
            (Get-Cell $unknown[0] 'OrganisationAutoExpanding') -ceq 'Unknown' -and $archives.Output.Contains('BDIT:UNKNOWN')
    }
    Test-Case 'archive: an archive GUID with an ArchiveStatus that is not Active is flagged in Notes, not silently trusted' {
        (Get-Cell @(Find-Row $archives.Rows 'Mailbox' 'true@contoso.example')[0] 'Notes') -like '*does not agree*'
    }
    $archiveDefault = Invoke-Copy $copyById['exo.archive-mailboxes'] 'archive'
    Test-Case 'archive: an empty archive GUID is HasArchive False with no warning' {
        $row = @(Find-Row $archiveDefault.Rows 'Mailbox' 'reception@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'HasArchive') -ceq 'False' -and (Get-Cell $row[0] 'OrganisationAutoExpanding') -ceq 'True' -and -not $archiveDefault.Output.Contains('BDIT:UNKNOWN')
    }

    Write-Output 'Protection policies and resource mailboxes:'
    $protection = Invoke-Copy $copyById['exo.protection-policies'] 'protection'
    Test-Case 'protection: a default policy with no rule is Default, a custom one with no rule NoRule' {
        $default = @(Find-Row $protection.Rows 'PolicyType' 'AntiSpam' | Where-Object { (Get-Cell $_ 'Policy') -eq 'Default' })
        $unused = @(Find-Row $protection.Rows 'Policy' 'Unused')
        $default.Count -eq 1 -and (Get-Cell $default[0] 'RuleState') -eq 'Default' -and $unused.Count -eq 1 -and (Get-Cell $unused[0] 'RuleState') -eq 'NoRule'
    }
    Test-Case 'protection: a preset policy with no rule is Preset, and one not marked custom or preset is Unknown, never NoRule' {
        $preset = @(Find-Row $protection.Rows 'Policy' 'Standard Preset Security Policy1')
        $unmarked = @(Find-Row $protection.Rows 'Policy' 'Unmarked')
        $preset.Count -eq 1 -and (Get-Cell $preset[0] 'RuleState') -eq 'Preset' -and $unmarked.Count -eq 1 -and (Get-Cell $unmarked[0] 'RuleState') -eq 'Unknown'
    }
    Test-Case 'protection: a scoped policy shows its rule, scope, exclusions, counted lists and unreturned settings as Unknown' {
        $row = @(Find-Row $protection.Rows 'Policy' 'Finance strict')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Rule') -eq 'Finance rule' -and (Get-Cell $row[0] 'AppliesTo') -like '*finance@contoso.example*' -and
            (Get-Cell $row[0] 'Excludes') -like '*cfo@contoso.example*' -and (Get-Cell $row[0] 'KeySettings') -like '*AllowedSenderDomains=2*' -and
            (Get-Cell $row[0] 'KeySettings') -like '*BulkThreshold=Unknown*'
    }
    Test-Case 'protection: a rule naming a policy that was not returned is listed with Unknown settings and state' {
        $row = @(Find-Row $protection.Rows 'Rule' 'Dangling rule')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'KeySettings') -eq 'Unknown' -and (Get-Cell $row[0] 'RuleState') -eq 'Unknown' -and (Get-Cell $row[0] 'IsDefault') -ceq 'Unknown'
    }
    $protectionFlags = Invoke-Copy (New-Copy 'exo.protection-policies' 'protection-flags' @('--PolicyType', 'AntiPhish')) 'protection-flags' -Scenario 'flags'
    Test-Case 'protection: one type only, and an unreturned default flag on a policy with no rule is Unknown, not NoRule' {
        $protectionFlags.Rows.Count -eq 1 -and (Get-Cell $protectionFlags.Rows[0] 'RuleState') -eq 'Unknown' -and (Get-Cell $protectionFlags.Rows[0] 'IsDefault') -ceq 'Unknown' -and
            @($protectionFlags.Reads | Where-Object { $_ -like 'Get-Hosted*' -or $_ -like 'Get-Malware*' }).Count -eq 0
    }
    $rooms = Invoke-Copy $copyById['exo.resource-mailboxes'] 'rooms'
    Test-Case 'rooms: only room and equipment mailboxes are listed, with booking settings' {
        $room = @(Find-Row $rooms.Rows 'PrimarySmtpAddress' 'room1@contoso.example')
        $rooms.Rows.Count -eq 2 -and $room.Count -eq 1 -and (Get-Cell $room[0] 'AutomateProcessing') -eq 'AutoAccept' -and
            (Get-Cell $room[0] 'AllowConflicts') -ceq 'False' -and (Get-Cell $room[0] 'ResourceDelegates') -eq 'Megan Bowen'
    }
    Test-Case 'rooms: booking settings that were not returned are Unknown and the run warns' {
        $van = @(Find-Row $rooms.Rows 'PrimarySmtpAddress' 'van@contoso.example')
        $van.Count -eq 1 -and (Get-Cell $van[0] 'AutomateProcessing') -eq 'Unknown' -and (Get-Cell $van[0] 'AllowConflicts') -ceq 'Unknown' -and
            (Get-Cell $van[0] 'AllBookInPolicy') -ceq 'Unknown' -and $rooms.Output.Contains('BDIT:UNKNOWN')
    }
    $roomsOnly = Invoke-Copy (New-Copy 'exo.resource-mailboxes' 'rooms-only' @('--ResourceType', 'RoomMailbox')) 'rooms-only'
    Test-Case 'rooms: a resource type filter lists only that type' { $roomsOnly.Rows.Count -eq 1 -and (Get-Cell $roomsOnly.Rows[0] 'RecipientTypeDetails') -eq 'RoomMailbox' }
    $roomUser = Invoke-Copy (New-Copy 'exo.resource-mailboxes' 'rooms-user' @('--Mailbox', 'alex@contoso.example')) 'rooms-user'
    Test-Case 'rooms: a named mailbox that is not a resource is refused' { $roomUser.Code -ne 0 -and -not $roomUser.CsvWritten }

    Write-Output 'Send on Behalf delegates are resolved to exact identities or Unresolved:'
    $delegates = Invoke-Copy $copyById['exo.send-on-behalf'] 'delegates' -Scenario 'delegates'
    Test-Case 'delegates: an exact address is Resolved to its address and object ID' {
        $row = @(Find-Row $delegates.Rows 'Mailbox' 'delegated@contoso.example' | Where-Object { (Get-Cell $_ 'DelegateAsReturned') -eq 'megan@contoso.example' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Resolved' -and (Get-Cell $row[0] 'DelegateAddress') -eq 'megan@contoso.example' -and (Get-Cell $row[0] 'DelegateObjectId') -eq 'obj-megan'
    }
    Test-Case 'delegates: a Name that one recipient has is still Unresolved and given no address' {
        $rows = @(Find-Row $delegates.Rows 'DelegateAsReturned' 'megan')
        $rows.Count -eq 2 -and @($rows | Where-Object { (Get-Cell $_ 'Status') -ne 'Unresolved' -or (Get-Cell $_ 'DelegateAddress') -ne '' -or (Get-Cell $_ 'DelegateObjectId') -ne '' }).Count -eq 0
    }
    Test-Case 'delegates: a display name match is Unresolved and is given no address' {
        $row = @(Find-Row $delegates.Rows 'DelegateAsReturned' 'Megan Bowen')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unresolved' -and (Get-Cell $row[0] 'DelegateAddress') -eq '' -and (Get-Cell $row[0] 'Notes') -like '*not an exact identity*'
    }
    Test-Case 'delegates: several matches or none are Unresolved, and the run warns' {
        (Get-Cell @(Find-Row $delegates.Rows 'DelegateAsReturned' 'Shared Name')[0] 'Status') -eq 'Unresolved' -and
            (Get-Cell @(Find-Row $delegates.Rows 'DelegateAsReturned' 'ghost')[0] 'Status') -eq 'Unresolved' -and $delegates.Output.Contains('BDIT:UNKNOWN')
    }
    Test-Case 'delegates: a mailbox whose list was not returned is Unknown, not empty' {
        $row = @(Find-Row $delegates.Rows 'Mailbox' 'nolist@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown'
    }
    Test-Case 'delegates: each distinct delegate is looked up once' {
        @($delegates.Reads | Where-Object { $_ -eq 'Get-EXORecipient' }).Count -eq 5 -and @(Find-Row $delegates.Rows 'DelegateAsReturned' 'megan').Count -eq 2
    }
    $delegateCap = Invoke-Copy (New-Copy 'exo.send-on-behalf' 'delegates-cap' @('--MaxRows', '2')) 'delegates-cap' -Scenario 'delegates'
    Test-Case 'delegates: more rows than the limit is marked partial' { $delegateCap.Rows.Count -eq 2 -and $delegateCap.Output.Contains('BDIT:PARTIAL') }

    # The last child run may have been an expected refusal; do not leave its exit code for the calling step to report.
    $global:LASTEXITCODE = 0
    if ($failures.Count -ne 0) { throw ($failures.Count.ToString() + ' synthetic regression case(s) failed: ' + ($failures -join '; ')) }
    Write-Output ('Synthetic run passed for ' + $manifests.Count + ' item(s); a different tenant and a different account were refused each time with nothing read. No Microsoft module or tenant was used.')
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
