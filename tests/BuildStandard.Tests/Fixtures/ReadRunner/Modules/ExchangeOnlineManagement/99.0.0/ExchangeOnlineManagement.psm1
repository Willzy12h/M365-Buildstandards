# Synthetic stand-in for ExchangeOnlineManagement, used only by the runner regressions. It never signs in, never contacts
# a service and holds no credential. BDIT_FAKE_SCENARIO picks a behaviour; every call is appended to BDIT_FAKE_LOG so a
# test can prove which reads happened and that no write was reached.
$script:Connected = $false
$script:Account = $null
$script:Reads = 0
$script:Scenario = [string]$env:BDIT_FAKE_SCENARIO
$script:Tenant = [string]$env:BDIT_FAKE_TENANT

function Write-FakeLog([string]$Text) { if ($env:BDIT_FAKE_LOG) { [IO.File]::AppendAllText($env:BDIT_FAKE_LOG, $Text + "`n") } }

function Connect-ExchangeOnline {
    param([string]$UserPrincipalName, [string[]]$CommandName, [switch]$ShowBanner)
    Write-FakeLog ('Connect ' + $UserPrincipalName + ' ' + ($CommandName -join ','))
    if ($script:Scenario -eq 'connect-fails') { throw 'Synthetic sign-in failure.' }
    $script:Connected = $true
    $script:Account = if ($script:Scenario -eq 'wrong-account') { 'someone.else@synthetic.example' } else { $UserPrincipalName }
    if ($script:Scenario -eq 'wrong-tenant') { $script:Tenant = '22222222-2222-4222-8222-222222222222' }
}

function Disconnect-ExchangeOnline {
    param([switch]$Confirm)
    Write-FakeLog 'Disconnect'
    $script:Connected = $false
}

# The token fields Get-ConnectionInformation documents. By default the token is Active and expires in an hour, as a
# DateTimeOffset. BDIT_FAKE_TOKEN picks another shape: expired, missing, null, malformed, another type or a string form.
function Add-FakeToken($Connection) {
    $now = [DateTimeOffset]::UtcNow
    $status = 'Active'
    $expiry = $now.AddHours(1)
    switch ([string]$env:BDIT_FAKE_TOKEN) {
        'expired-active' { $expiry = $now.AddHours(-1) }
        'expired-status' { $status = 'Expired' }
        'missing-status' { $status = '<absent>' }
        'null-status' { $status = $null }
        'missing-expiry' { $expiry = '<absent>' }
        'null-expiry' { $expiry = $null }
        'malformed-expiry' { $expiry = 'not a date' }
        'number-expiry' { $expiry = 4102444800 }
        'ambiguous-string' { $expiry = $now.AddDays(40).UtcDateTime.ToString('dd/MM/yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture) }
        'offset-string-past' { $expiry = $now.AddMinutes(-10).ToOffset([TimeSpan]::FromHours(14)).ToString('yyyy-MM-ddTHH:mm:sszzz', [Globalization.CultureInfo]::InvariantCulture) }
        'offset-string-future' { $expiry = $now.AddMinutes(30).ToOffset([TimeSpan]::FromHours(-11)).ToString('yyyy-MM-ddTHH:mm:sszzz', [Globalization.CultureInfo]::InvariantCulture) }
        'utc-string-future' { $expiry = $now.AddMinutes(30).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture) }
        'utc-datetime-future' { $expiry = $now.AddMinutes(30).UtcDateTime }
        'unspecified-datetime-past' { $expiry = [DateTime]::SpecifyKind($now.AddMinutes(-30).UtcDateTime, [DateTimeKind]::Unspecified) }
        'unspecified-datetime-future' { $expiry = [DateTime]::SpecifyKind($now.AddMinutes(30).UtcDateTime, [DateTimeKind]::Unspecified) }
        'expires-after-first-read' { if ($script:Reads -ge 1) { $expiry = $now.AddSeconds(-1) } }
        'expires-after-last-read' { if ($script:Reads -ge 3) { $expiry = $now.AddSeconds(-1) } }
    }
    if ($status -ne '<absent>') { $Connection | Add-Member -NotePropertyName TokenStatus -NotePropertyValue $status }
    if (-not ($expiry -is [string] -and $expiry -eq '<absent>')) { $Connection | Add-Member -NotePropertyName TokenExpiryTimeUTC -NotePropertyValue $expiry }
}

function Get-ConnectionInformation {
    if (-not $script:Connected) { return }
    $account = $script:Account
    # Changes only after the last of the three reads, so only the gate's check after a read can notice it.
    if (($script:Scenario -eq 'account-changes' -and $script:Reads -ge 3) -or ($script:Scenario -eq 'inbox-actor-change' -and $script:Reads -ge 2)) { $account = 'someone.else@synthetic.example' }
    $connection = [pscustomobject]@{ State = 'Connected'; IsEopSession = $false; TenantID = $script:Tenant; UserPrincipalName = $account }
    Add-FakeToken $connection
    if ($script:Scenario -eq 'two-connections') { return @($connection, $connection) }
    return $connection
}

function Get-EXOMailbox {
    param([string]$ResultSize, [string[]]$Properties, [string]$RecipientTypeDetails, [string]$Identity)
    $script:Reads++
    Write-FakeLog ('Read Get-EXOMailbox' + $(if ($ResultSize) { ' ResultSize=' + $ResultSize } else { '' }) + $(if ($Properties) { ' Properties=' + ($Properties -join ',') } else { '' }))
    if ($Identity) {
        $actual=if($script:Scenario -eq 'inbox-mismatch'){'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'}else{$Identity}
        $selected=[pscustomobject]@{ExchangeGuid=[guid]$actual;ExternalDirectoryObjectId=$null;DisplayName='Synthetic selected';PrimarySmtpAddress='selected@synthetic.example'}
        if ($Properties -contains 'GrantSendOnBehalfTo') { $selected | Add-Member -NotePropertyName GrantSendOnBehalfTo -NotePropertyValue @([pscustomobject]@{RawIdentity='S-1-5-21-123-456-789-1003'}) }
        return $selected
    }
    if ($script:Scenario -eq 'chatty') { [Console]::Out.Write(('x' * 1100000)); [Console]::Out.Flush() }
    if ($script:Scenario -eq 'noisy-errors') { 1..3000 | ForEach-Object { [Console]::Error.WriteLine(('e' * 200)) } }
    if ($script:Scenario -eq 'hang') { Start-Sleep -Seconds 120 }
    $mailboxes = @(
        [pscustomobject]@{ ExchangeGuid = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; DisplayName = 'Synthetic user'; ExternalDirectoryObjectId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'; PrimarySmtpAddress = 'user@synthetic.example'; RecipientTypeDetails = 'UserMailbox'; IssueWarningQuota = '49 GB (52,613,349,376 bytes)'; ProhibitSendQuota = '49.5 GB (53,150,220,288 bytes)'; ProhibitSendReceiveQuota = '50 GB (53,687,091,200 bytes)'; ArchiveGuid = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd'; ArchiveQuota = '100 GB (107,374,182,400 bytes)' },
        [pscustomobject]@{ ExchangeGuid = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'; DisplayName = 'Synthetic shared'; ExternalDirectoryObjectId = ''; PrimarySmtpAddress = 'shared@synthetic.example'; RecipientTypeDetails = 'SharedMailbox'; IssueWarningQuota = 'Unlimited'; ProhibitSendQuota = 'Unlimited'; ProhibitSendReceiveQuota = 'Unlimited'; ArchiveGuid = '00000000-0000-0000-0000-000000000000'; ArchiveQuota = '0 B (0 bytes)' }
    )
    if ($script:Scenario.StartsWith('settings-')) {
        $mailboxes[1].ExternalDirectoryObjectId = '77777777-7777-4777-8777-777777777777'
        foreach ($mailbox in $mailboxes) {
            foreach ($property in @('LitigationHoldEnabled','RetentionHoldEnabled','ComplianceTagHoldApplied','DelayHoldApplied','DelayReleaseHoldApplied','DeliverToMailboxAndForward')) {
                $mailbox.PSObject.Properties.Add([psnoteproperty]::new($property,$false))
            }
            $mailbox.PSObject.Properties.Add([psnoteproperty]::new('ForwardingAddress',$null))
            $mailbox.PSObject.Properties.Add([psnoteproperty]::new('ForwardingSmtpAddress','smtp:target@synthetic.example'))
            $mailbox.PSObject.Properties.Add([psnoteproperty]::new('InPlaceHolds',@()))
        }
        if ($script:Scenario -eq 'settings-missing-flag') { $mailboxes[0].PSObject.Properties.Remove('LitigationHoldEnabled'); $mailboxes[0].PSObject.Properties.Remove('DeliverToMailboxAndForward') }
        if ($script:Scenario -eq 'settings-malformed-flag') { $mailboxes[0].LitigationHoldEnabled = 'false';$mailboxes[0].DeliverToMailboxAndForward = 'false' }
        if ($script:Scenario -eq 'settings-no-mailbox-guid') { $mailboxes[0].ExchangeGuid = '' }
        if ($script:Scenario -eq 'settings-duplicate-guid') { $mailboxes[1].ExchangeGuid = $mailboxes[0].ExchangeGuid }
        if ($script:Scenario -eq 'settings-null-holds') { $mailboxes[0].InPlaceHolds = $null }
        if ($script:Scenario -eq 'settings-excess-holds') { $mailboxes[0].InPlaceHolds = @(1..101 | ForEach-Object { 'hold' + $_ }) }
    }
    # Shapes the shipped adapter must not trust: a missing quota, a blank archive GUID, a malformed object ID, no mailbox GUID.
    if ($script:Scenario -eq 'no-quota') { $mailboxes[0].IssueWarningQuota = '  ' }
    if ($script:Scenario -eq 'no-archive-guid') { $mailboxes[0].ArchiveGuid = '' }
    if ($script:Scenario -eq 'bad-object-id') { $mailboxes[0].ExternalDirectoryObjectId = 'not-a-guid' }
    if ($script:Scenario -eq 'no-mailbox-guid') { $mailboxes += [pscustomobject]@{ ExchangeGuid = ''; DisplayName = 'No GUID'; RecipientTypeDetails = 'UserMailbox' } }
    if ($script:Scenario -eq 'no-type') { $mailboxes[1].PSObject.Properties.Remove('RecipientTypeDetails') }
    # More than the adapter's 10,000 cap, with one mailbox inside the cap returned without its GUID.
    if ($script:Scenario -eq 'over-cap-missing-guid') {
        $mailboxes = @(for ($i = 0; $i -lt 10001; $i++) {
            [pscustomobject]@{ ExchangeGuid = $(if ($i -eq 5) { '' } else { '{0:x8}-0000-4000-8000-000000000000' -f ($i + 1) }); DisplayName = 'Bulk ' + $i
                PrimarySmtpAddress = 'bulk' + $i + '@synthetic.example'; RecipientTypeDetails = 'UserMailbox'; IssueWarningQuota = 'Unlimited'
                ProhibitSendQuota = 'Unlimited'; ProhibitSendReceiveQuota = 'Unlimited'; ArchiveGuid = '00000000-0000-0000-0000-000000000000'; ArchiveQuota = '0 B (0 bytes)' }
        })
    }
    # With -Properties, return only the documented Minimum property set plus the properties asked for, as the real cmdlet
    # does (AST-20261010-20). ExchangeGuid is not in Minimum. The older synthetic body asks for no properties and keeps the full shape.
    if ($Properties) {
        $minimum = @('ExternalDirectoryObjectId', 'UserPrincipalName', 'Alias', 'DisplayName', 'EmailAddresses', 'PrimarySmtpAddress',
            'RecipientType', 'RecipientTypeDetails', 'Identity', 'Id', 'ExchangeVersion', 'Name', 'DistinguishedName', 'OrganizationId', 'Guid')
        $wanted = $minimum + $Properties
        $mailboxes = @($mailboxes | ForEach-Object {
            $selected = [ordered]@{}
            foreach ($property in $_.PSObject.Properties) { if ($wanted -contains $property.Name) { $selected[$property.Name] = $property.Value } }
            [pscustomobject]$selected
        })
    }
    return $mailboxes
}

function Get-EXOMailboxStatistics {
    param([string]$Identity, [switch]$Archive)
    $script:Reads++
    Write-FakeLog ('Read Get-EXOMailboxStatistics ' + $Identity + $(if ($Archive) { ' -Archive' } else { '' }))
    if ($Archive) {
        if ($script:Scenario -eq 'archive-stats-fail') { throw 'Synthetic archive statistics failure.' }
        return [pscustomobject]@{ TotalItemSize = '3 GB (3,221,225,472 bytes)' }
    }
    if ($script:Scenario -eq 'stats-fail' -and $Identity -like 'bbbbbbbb*') { throw 'Synthetic statistics failure.' }
    # A slow first statistics read. With BDIT_FAKE_COOPERATE it ends as soon as the runner's stop file appears, as a read
    # that returns promptly would; without it, it outlasts the stop grace period, as a hung module call would.
    if ($script:Scenario -eq 'slow-stats' -and $Identity -like 'aaaaaaaa*') {
        Write-FakeLog 'Slow read started'
        for ($i = 0; $i -lt 300; $i++) {
            if ($env:BDIT_FAKE_COOPERATE -and @(Get-ChildItem -LiteralPath $env:BDIT_FAKE_STAGING -Recurse -File -Filter 'stop-*.flag').Count -gt 0) { break }
            Start-Sleep -Milliseconds 100
        }
    }
    $size = if ($Identity -like 'aaaaaaaa*') { '1.2 GB (1,288,490,189 bytes)' } else { '0 B (0 bytes)' }
    return [pscustomobject]@{ TotalItemSize = $size }
}

# Present only to prove a body cannot reach a write: the gate never registers it and the body check refuses it.
function Set-Mailbox { param([string]$Identity) Write-FakeLog ('WRITE Set-Mailbox ' + $Identity) }

function Get-CASMailbox {
    [CmdletBinding()]
    param([string]$Identity)
    if ($Identity -notin @('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa','bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb')) { throw 'Unexpected synthetic mailbox identity' }
    $ExternalDirectoryObjectId = [guid]$(if ($Identity.StartsWith('aaaaaaaa')) { 'cccccccc-cccc-4ccc-8ccc-cccccccccccc' } else { '77777777-7777-4777-8777-777777777777' })
    $script:Reads++; Write-FakeLog ('Read Get-CASMailbox ' + $Identity)
    if ($script:Scenario -eq 'settings-cas-fail') { throw 'Synthetic mailbox-specific read failure' }
    $value = [ordered]@{ ExternalDirectoryObjectId = $ExternalDirectoryObjectId.ToString('D'); PopEnabled = $false; ImapEnabled = $false; MAPIEnabled = $true; EwsEnabled = $false; ActiveSyncEnabled = $false; OWAEnabled = $true; SmtpClientAuthenticationDisabled = $null }
    if ($ExternalDirectoryObjectId.ToString().StartsWith('cccccccc')) {
        if ($script:Scenario -eq 'settings-missing-flag') { $value.Remove('PopEnabled') }
        if ($script:Scenario -eq 'settings-malformed-flag') { $value.PopEnabled = 'false' }
    }
    if ($script:Scenario -eq 'settings-cas-wrong-id') { $value.ExternalDirectoryObjectId = '99999999-9999-4999-8999-999999999999' }
    if ($script:Scenario -eq 'settings-smtp-absent') { $value.Remove('SmtpClientAuthenticationDisabled') }
    if ($script:Scenario -eq 'settings-smtp-enabled') { $value.SmtpClientAuthenticationDisabled = $false }
    if ($script:Scenario -eq 'settings-smtp-disabled') { $value.SmtpClientAuthenticationDisabled = $true }
    [pscustomobject]$value
}
function Get-TransportConfig {
    $script:Reads++; Write-FakeLog 'Read Get-TransportConfig'
    [pscustomobject]@{ SmtpClientAuthenticationDisabled = $(if($script:Scenario -eq 'settings-org-null') { $null } else { $true }) }
}

function Get-InboxRule {
    param([string]$Mailbox,[bool]$IncludeHidden,[int]$ResultSize)
    $script:Reads++; Write-FakeLog ('Read Get-InboxRule ' + $Mailbox + ' hidden=' + $IncludeHidden + ' ResultSize=' + $ResultSize)
    if ($script:Scenario -eq 'inbox-stop' -and $Mailbox -like 'bbbbbbbb*') {
        for ($n=0;$n -lt 300;$n++) {
            if (@(Get-ChildItem -LiteralPath $env:BDIT_FAKE_STAGING -Recurse -File -Filter 'stop-*.flag').Count -gt 0) { break }
            Start-Sleep -Milliseconds 10
        }
    }
    if ($script:Scenario -eq 'inbox-failed') { throw 'Synthetic inbox read failure' }
    if ($script:Scenario -eq 'inbox-empty') { return }
    $row=[pscustomobject]@{RuleIdentity=[uint64]::MaxValue;Identity='raw opaque rule';Name='Synthetic';Enabled=$false;Priority=0;ForwardTo=@();ForwardAsAttachmentTo=@();RedirectTo=@();DeleteMessage=$false;MarkAsRead=$false;StopProcessingRules=$true}
    if ($script:Scenario -eq 'inbox-missing') { $row.PSObject.Properties.Remove('DeleteMessage') }
    if ($script:Scenario -eq 'inbox-missing-id') { $row.PSObject.Properties.Remove('RuleIdentity') }
    if ($script:Scenario -eq 'inbox-raw-identity') { $row.Identity=[pscustomobject]@{RawIdentity='opaque-original';Secret='excluded unrelated'} }
    if ($script:Scenario -eq 'inbox-null') { $row.DeleteMessage=$null }
    if ($script:Scenario -eq 'inbox-unresolved') { $row.ForwardTo=@([pscustomobject]@{DisplayName='Unresolved synthetic label'}) }
    $row
    if ($script:Scenario -eq 'inbox-duplicate') { $row }
}

function Get-EXOMailboxPermission {
    param([string]$Identity,[int]$ResultSize)
    $script:Reads++;Write-FakeLog ('Read Get-EXOMailboxPermission '+$Identity+' ResultSize='+$ResultSize)
    if ($script:Scenario -eq 'permission-failed') { throw 'Synthetic permission read failure' }
    if ($script:Scenario -eq 'permission-empty') { return }
    $row=[pscustomobject]@{User=[pscustomobject]@{RawIdentity='S-1-5-21-123-456-789-1001';Private='excluded'};AccessRights=@('FullAccess','ReadPermission');IsInherited=$false;Deny=$false}
    if ($script:Scenario -eq 'permission-missing-flag') { $row.PSObject.Properties.Remove('Deny') }
    if ($script:Scenario -eq 'permission-null-rights') { $row.AccessRights=$null }
    if ($script:Scenario -eq 'permission-unresolved') { $row.User=[pscustomobject]@{DisplayName='Synthetic unresolved label'} }
    $row
}
function Get-EXORecipientPermission {
    param([string]$Identity,[int]$ResultSize)
    $script:Reads++;Write-FakeLog ('Read Get-EXORecipientPermission '+$Identity+' ResultSize='+$ResultSize)
    if ($script:Scenario -eq 'permission-stop') {
        for ($n=0;$n -lt 300;$n++) {
            if (@(Get-ChildItem -LiteralPath $env:BDIT_FAKE_STAGING -Recurse -File -Filter 'stop-*.flag').Count -gt 0) { break }
            Start-Sleep -Milliseconds 10
        }
    }
    [pscustomobject]@{Trustee=[pscustomobject]@{RawIdentity='S-1-5-21-123-456-789-1002'};AccessRights=@('SendAs');IsInherited=$false;Deny=$false}
}

function Get-EXOMailboxFolderStatistics {
    param([string]$Identity,[string]$FolderScope)
    $script:Reads++;Write-FakeLog ('Read Get-EXOMailboxFolderStatistics '+$Identity+' FolderScope='+$FolderScope)
    if($FolderScope -cne 'Calendar'){throw 'Unexpected synthetic folder scope'}
    if($script:Scenario -eq 'calendar-missing'){return}
    [pscustomobject]@{FolderId='returned-store-folder-id';FolderPath='/Kalender';FolderType='Calendar';Identity='original-folder-identity';Private='excluded'}
    if($script:Scenario -eq 'calendar-ambiguous'){[pscustomobject]@{FolderId='second-id';FolderPath='/Other';FolderType='Calendar';Identity='other-identity'}}
}
function Get-EXOMailboxFolderPermission {
    param([string]$Identity)
    $script:Reads++;Write-FakeLog ('Read Get-EXOMailboxFolderPermission '+$Identity)
    if($script:Scenario -eq 'calendar-failed'){throw 'Synthetic ACL failure'}
    $row=[pscustomobject]@{User=[pscustomobject]@{UserType='Default';DisplayName='Not an identity';Private='excluded'};AccessRights=@('AvailabilityOnly');SharingPermissionFlags=@();Private='excluded'}
    if($script:Scenario -eq 'calendar-missing-rights'){$row.PSObject.Properties.Remove('AccessRights')}
    $row
    if($script:Scenario -eq 'calendar-duplicate'){$row}
}

function Get-DistributionGroup {
    param([string]$Identity)
    $script:Reads++;Write-FakeLog ('Read Get-DistributionGroup '+$Identity)
    $row=[pscustomobject]@{Guid=[guid]$Identity;ExternalDirectoryObjectId=$null;DisplayName='Synthetic group';PrimarySmtpAddress='group@synthetic.example';RecipientTypeDetails='MailUniversalDistributionGroup';ManagedBy=@([pscustomobject]@{RawIdentity='original-owner';Private='excluded'});MemberJoinRestriction='Closed';MemberDepartRestriction='Open';RequireSenderAuthenticationEnabled=$false;HiddenFromAddressListsEnabled=$false;Private='excluded'}
    if($script:Scenario -eq 'group-unsupported'){$row.RecipientTypeDetails='GroupMailbox'}
    if($script:Scenario -eq 'group-unresolved-owner'){$row.ManagedBy=@([pscustomobject]@{DisplayName='Unresolved owner label'})}
    if($script:Scenario -eq 'group-null-flag'){$row.RequireSenderAuthenticationEnabled=$null}
    $row
}
function Get-DistributionGroupMember {
    param([string]$Identity,[int]$ResultSize)
    $script:Reads++;Write-FakeLog ('Read Get-DistributionGroupMember '+$Identity+' ResultSize='+$ResultSize)
    if($script:Scenario -eq 'group-failed'){throw 'Synthetic member read failure'}
    if($script:Scenario -eq 'group-stop' -and $Identity -ne '44444444-4444-4444-8444-444444444444') {
        for($n=0;$n -lt 300;$n++) { if(@(Get-ChildItem -LiteralPath $env:BDIT_FAKE_STAGING -Recurse -File -Filter 'stop-*.flag').Count -gt 0){break};Start-Sleep -Milliseconds 10 }
    }
    if($script:Scenario -eq 'group-empty'){return}
    $row=[pscustomobject]@{Guid=[guid]'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';ExternalDirectoryObjectId=$null;Identity=[pscustomobject]@{RawIdentity='original-member'};DisplayName='Synthetic member';PrimarySmtpAddress='member@synthetic.example';RecipientTypeDetails='UserMailbox';Private='excluded'}
    if($script:Scenario -eq 'group-unresolved-member'){$row.PSObject.Properties.Remove('Guid');$row.Identity=[pscustomobject]@{DisplayName='Unresolved member label'}}
    if($script:Scenario -eq 'group-raw-member'){$row.PSObject.Properties.Remove('Guid');$row.ExternalDirectoryObjectId=[guid]'55555555-5555-4555-8555-555555555555'}
    $row
    if($script:Scenario -eq 'group-duplicate'){$row}
}

Export-ModuleMember -Function Get-DistributionGroup, Get-DistributionGroupMember, Get-EXOMailboxFolderStatistics, Get-EXOMailboxFolderPermission, Get-EXOMailboxPermission, Get-EXORecipientPermission, Get-InboxRule, Get-CASMailbox, Get-TransportConfig, Connect-ExchangeOnline, Disconnect-ExchangeOnline, Get-ConnectionInformation, Get-EXOMailbox, Get-EXOMailboxStatistics, Set-Mailbox
