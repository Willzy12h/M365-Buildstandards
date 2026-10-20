param([string]$Wrapper,[string]$Case)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
foreach($name in @('Read-BditBoundedRows','Read-BditFamilyRows','Use-BditRead')) {
    $definition=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true)
    if($null -ne $definition){. ([scriptblock]::Create($definition.Extent.Text))}
}
function Check([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
function Assert-BditGate {if($script:BadActor){throw 'BDIT-IDENTITY'}}
$script:BadActor=$false;$script:Reads=0;$script:Produced=0;$script:LastSize=0;$script:Failure=$null
$first='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';$second='bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
$script:FamilyVersion=2;$script:FamilyReportId='exo-inbox-rules'
$script:FamilyTargets=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$null=$script:FamilyTargets.Add($first)
$script:FamilyVerifiedTargets=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$script:FamilyPermissionSources=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$script:FamilyCalendarProof=@{}
$script:FamilyIncludeHidden=$false;$script:FamilySourceCalls=0;$script:FamilyRowsRead=0;$script:FamilyProjectedBytes=2
$script:FamilyObservationMaximum=4999
$script:FamilyStarted=[Diagnostics.Stopwatch]::StartNew()
function Get-FakeMailbox {
    param([string]$Identity,[string[]]$Properties)
    $script:Reads++
    if($Case -ceq 'failed-lookup-after-good' -and $script:Reads -ge 2){throw 'Synthetic failed lookup'}
    $guid=if($Case -ceq 'wrong-object' -or ($Case -ceq 'stale-parent' -and $script:Reads -ge 2)){$second}else{$Identity}
    [pscustomobject]@{ExchangeGuid=[guid]$guid;ExternalDirectoryObjectId=$null;DisplayName='Synthetic';PrimarySmtpAddress='mailbox@synthetic.example';GrantSendOnBehalfTo=@([pscustomobject]@{RawIdentity='opaque-principal'});Secret='not selected'}
}
function Get-FakeRules {
    param([string]$Mailbox,[bool]$IncludeHidden,[int]$ResultSize)
    $script:Reads++;$script:LastSize=$ResultSize
    $count=if($Case -ceq 'rows' -or $Case -ceq 'lower-budget'){2}else{1}
    for($n=0;$n -lt $count;$n++){
        $script:Produced++
        [pscustomobject]@{RuleIdentity=('18446744073709551615'+[string]$n);Identity='raw-rule';Name='Synthetic';Enabled=$false;Priority=0;ForwardTo=@();ForwardAsAttachmentTo=@();RedirectTo=@();DeleteMessage=$false;MarkAsRead=$false;StopProcessingRules=$true;Secret='not selected'}
    }
}
function Get-FakePermission {
    param([string]$Identity,[int]$ResultSize)
    $script:Reads++;$script:LastSize=$ResultSize
    [pscustomobject]@{User=[pscustomobject]@{RawIdentity='S-1-5-21-123-456-789-1001';Private='excluded'};Trustee='NT AUTHORITY\SELF';AccessRights=@('FullAccess');IsInherited=$false;Deny=$false;Private='excluded'}
}
function Get-FakeFolders {
    param([string]$Identity,[string]$FolderScope)
    $script:Reads++
    Check ($FolderScope -ceq 'Calendar') 'Calendar scope was not fixed'
    [pscustomobject]@{FolderId='store-folder';FolderPath='/Kalender';FolderType='Calendar';Identity='returned-raw-folder';Private='excluded'}
    if($Case -ceq 'calendar-unknown-type'){[pscustomobject]@{FolderId='unknown-folder';FolderPath='/Unknown';Identity='unknown-raw-folder'}}
    if($Case -ceq 'calendar-ambiguous'){[pscustomobject]@{FolderId='second-folder';FolderPath='/Other';FolderType='Calendar';Identity='second-raw-folder'}}
}
function Get-FakeCalendarAcl {
    param([string]$Identity)
    $script:Reads++;Check ($Identity -ceq ($first+':\Kalender')) 'A guessed or foreign calendar reached the source'
    [pscustomobject]@{User=[pscustomobject]@{UserType='Default';Private='excluded'};AccessRights=@('Reviewer');SharingPermissionFlags=@();Private='excluded'}
}
$Readers=@{'Get-EXOMailboxFolderStatistics'=(Get-Command Get-FakeFolders);'Get-EXOMailboxFolderPermission'=(Get-Command Get-FakeCalendarAcl);'Get-EXOMailboxPermission'=(Get-Command Get-FakePermission);'Get-EXORecipientPermission'=(Get-Command Get-FakePermission);'Get-EXOMailbox' =(Get-Command Get-FakeMailbox);'Get-InboxRule'=(Get-Command Get-FakeRules)}
function Refused([scriptblock]$Work,[int]$ExpectedReads){
    $failed=$false;try{$null=& $Work}catch{$failed=$true}
    Check $failed 'A forbidden/unbounded call was admitted';Check ($script:Reads -eq $ExpectedReads) 'A refused call reached a source reader'
}
switch -CaseSensitive ($Case) {
 'calendar-source' {
    $script:FamilyReportId='exo-calendar-permissions'
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    $folders=@(Use-BditRead -Command 'Get-EXOMailboxFolderStatistics' -Parameters @{Identity=$first})
    Check ($folders.Count -eq 1 -and $folders[0].FolderPath -ceq '/Kalender') 'Localised returned path lost'
    $acl=@(Use-BditRead -Command 'Get-EXOMailboxFolderPermission' -Parameters @{Identity=($first+':\Kalender')})
    Check ($acl.Count -eq 1 -and $acl[0].User.Source -ceq 'SpecialPrincipal' -and $acl[0].User.Value -ceq 'Default') 'Default principal lost'
    Check ($null -eq $acl[0].PSObject.Properties['Private']) 'Unregistered ACL data escaped'
    Check ($script:FamilyRowsRead -eq 2) 'Folder discovery and ACL did not share the raw observation budget'
 }
 'calendar-unknown-type' {
    $script:FamilyReportId='exo-calendar-permissions';$null=$script:FamilyVerifiedTargets.Add($first)
    $null=Use-BditRead -Command 'Get-EXOMailboxFolderStatistics' -Parameters @{Identity=$first}
    Refused {Use-BditRead -Command 'Get-EXOMailboxFolderPermission' -Parameters @{Identity=($first+':\Kalender')}} 1
 }
 'calendar-unverified' {
    $script:FamilyReportId='exo-calendar-permissions';$null=$script:FamilyVerifiedTargets.Add($first)
    Refused {Use-BditRead -Command 'Get-EXOMailboxFolderPermission' -Parameters @{Identity=($first+':\Kalender')}} 0
 }
 'calendar-ambiguous' {
    $script:FamilyReportId='exo-calendar-permissions';$null=$script:FamilyVerifiedTargets.Add($first)
    $null=Use-BditRead -Command 'Get-EXOMailboxFolderStatistics' -Parameters @{Identity=$first}
    Refused {Use-BditRead -Command 'Get-EXOMailboxFolderPermission' -Parameters @{Identity=($first+':\Kalender')}} 1
 }
 'calendar-stale' {
    $script:FamilyReportId='exo-calendar-permissions';$null=$script:FamilyVerifiedTargets.Add($first)
    $null=Use-BditRead -Command 'Get-EXOMailboxFolderStatistics' -Parameters @{Identity=$first}
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    Refused {Use-BditRead -Command 'Get-EXOMailboxFolderPermission' -Parameters @{Identity=($first+':\Kalender')}} 2
 }
 'projection' {
    $rows=@(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first})
    Check ($rows.Count -eq 1) 'Expected one lookup row'
    Check ($null -eq $rows[0].PSObject.Properties['Secret']) 'Unselected source data escaped'
    $rules=@(Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false})
    Check ($rules.Count -eq 1 -and $rules[0].Enabled -is [bool] -and -not $rules[0].Enabled) 'False was lost'
    Check ($script:LastSize -eq 5000) 'ResultSize is not the gate-owned remaining cap plus sentinel'
    Check ($script:FamilyRowsRead -eq 1 -and $script:FamilyProjectedBytes -gt 2) 'Shared budgets were not charged'
    Check ($null -eq $rules[0].PSObject.Properties['Secret']) 'Unselected rule data escaped'
 }
 'lower-budget' {
    $script:FamilyObservationMaximum=99;$script:FamilyRowsRead=98
    $null=$script:FamilyVerifiedTargets.Add($first)
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 1
    Check ($script:LastSize -eq 2 -and $script:FamilyRowsRead -eq 99) 'Lower source cap was not enforced before projection'
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 1
 }
 'diagnostic-reserve' {
    $script:FamilyObservationMaximum=0;$null=$script:FamilyVerifiedTargets.Add($first)
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 0
 }
 'permission-source' {
    $script:FamilyReportId='exo-mailbox-permissions';$null=$script:FamilyPermissionSources.Add('FullAccess')
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    $rows=@(Use-BditRead -Command 'Get-EXOMailboxPermission' -Parameters @{Identity=$first})
    Check ($rows.Count -eq 1 -and $rows[0].User.Source -ceq 'RawIdentity' -and $rows[0].User.Value -ceq 'S-1-5-21-123-456-789-1001') 'Approved raw trustee namespace/value was lost'
    Check ($rows[0].Deny -is [bool] -and -not $rows[0].Deny -and $rows[0].IsInherited -is [bool] -and -not $rows[0].IsInherited) 'Reported false flags were lost'
    Check ($script:LastSize -eq 5000 -and $script:FamilyRowsRead -eq 1) 'The selected source did not share the diagnostic-reserved row budget'
    Check ($null -eq $rows[0].PSObject.Properties['Private']) 'Unregistered permission data escaped'
 }
 'permission-unselected' {
    $script:FamilyReportId='exo-mailbox-permissions';$null=$script:FamilyPermissionSources.Add('SendAs');$null=$script:FamilyVerifiedTargets.Add($first)
    Refused {Use-BditRead -Command 'Get-EXOMailboxPermission' -Parameters @{Identity=$first}} 0
 }
 'permission-send-as' {
    $script:FamilyReportId='exo-mailbox-permissions';$null=$script:FamilyPermissionSources.Add('SendAs');$null=$script:FamilyVerifiedTargets.Add($first)
    $rows=@(Use-BditRead -Command 'Get-EXORecipientPermission' -Parameters @{Identity=$first})
    Check ($rows.Count -eq 1 -and $rows[0].Trustee -ceq 'NT AUTHORITY\SELF') 'SendAs source was inferred or mixed with FullAccess'
    Check ($script:FamilyRowsRead -eq 1 -and $script:LastSize -eq 5000) 'SendAs source budget was not fixed'
 }
 'permission-send-on-behalf' {
    $script:FamilyReportId='exo-mailbox-permissions';$null=$script:FamilyPermissionSources.Add('SendOnBehalf')
    $rows=@(Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first})
    Check ($rows.Count -eq 1 -and $rows[0].GrantSendOnBehalfTo.Count -eq 1 -and $rows[0].GrantSendOnBehalfTo[0].Source -ceq 'RawIdentity') 'Attribute principal lost its original source namespace'
    Check ($script:FamilyRowsRead -eq 1) 'Attribute-derived edges bypassed the shared observation budget'
 }
 'permission-attribute-budget' {
    $script:FamilyReportId='exo-mailbox-permissions';$null=$script:FamilyPermissionSources.Add('SendOnBehalf');$script:FamilyObservationMaximum=0
    Refused {Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}} 1
 }
 'wrong-target' {Refused {Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$second}} 0}
 'unverified-child' {Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 0}
 'wrong-object' {
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 1
 }
 'stale-parent' {
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 2
 }
 'failed-lookup-after-good' {
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    try{$null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}}catch{}
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 2
 }
 'extra-parameter' {Refused {Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first;ResultSize='Unlimited'}} 0}
 'hidden-scope' {
    $null=Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$true}} 1
 }
 'rows' {
    $null=$script:FamilyVerifiedTargets.Add($first);$script:FamilyRowsRead=4998
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 1
    Check ($script:Produced -eq 2 -and $script:FamilyRowsRead -eq 4999) 'Shared row sentinel was not bounded before materialisation'
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 1
 }
 'bytes' {
    $null=$script:FamilyVerifiedTargets.Add($first);$script:FamilyProjectedBytes=33554430
    Refused {Use-BditRead -Command 'Get-InboxRule' -Parameters @{Mailbox=$first;IncludeHidden=$false}} 1
 }
 'calls' {$script:FamilySourceCalls=100;Refused {Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}} 0}
 'actor' {$script:BadActor=$true;Refused {Use-BditRead -Command 'Get-EXOMailbox' -Parameters @{Identity=$first}} 0}
}
'PASS '+$Case
