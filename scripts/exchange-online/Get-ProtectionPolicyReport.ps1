<#
Exchange Online Protection policies: inbound anti-spam, outbound spam, anti-phishing and anti-malware, each with the
rule that scopes it, its state and priority, who it applies to and its key settings.
Read only. A default policy applies to everyone no other policy of its type covers. A custom policy with no rule is
listed as NoRule because it applies to no one. A preset security policy (Standard or Strict) is scoped by a separate
preset rule that this item does not read, so it is listed as Preset, not NoRule; a policy that may be a preset is
Unknown. A rule is matched to its policy by the policy identity the rule names. A setting Exchange did not return is
shown as Unknown, and so is one returned null; a list returned empty is counted as 0. Defender for Office 365 policies (Safe Links, Safe Attachments) are not included.
#>
param(
    [string[]]$PolicyType = @('AntiSpam', 'OutboundSpam', 'AntiPhish', 'AntiMalware')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# True when any column holds Unknown, alone or as a name=Unknown setting, so the run can say what was not measured.
function Test-UnknownValue([object]$Row) {
    foreach ($property in @($Row.PSObject.Properties)) {
        if ([string]$property.Value -cmatch '(^|=)Unknown(;|$)') { return $true }
    }
    return $false
}
$unknownRows = 0

function Get-Value([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
    return $null
}

function Get-Text([object]$Value) {
    if ($null -eq $Value) { return '' }
    return ((@($Value) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ }) -join ', ') -replace '\s+', ' '
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

# Name=value pairs; a setting that was not returned, or was returned null, is Unknown. One returned as an empty value is
# NotSet. Lists are counted, and only a list returned empty counts as 0.
function Get-Settings([object]$Item, [string[]]$Names, [string[]]$Counted) {
    $pairs = @()
    foreach ($name in $Names) {
        $value = 'Unknown'
        if ($null -ne $Item -and $Item.PSObject.Properties[$name] -and $null -ne $Item.$name) {
            $raw = $Item.$name
            if ($Counted -contains $name) { $value = [string]@($raw | Where-Object { $null -ne $_ -and [string]$_ -ne '' }).Count }
            elseif ($raw -is [bool]) { if ($raw) { $value = 'True' } else { $value = 'False' } }
            else { $value = Get-Text $raw; if ($value -eq '') { $value = 'NotSet' } }
        }
        $pairs += ($name + '=' + $value)
    }
    return $pairs -join '; '
}

# Who a rule includes or excludes, from the recipient (or, for outbound, sender) conditions it holds.
function Get-Scope([object]$Rule, [string[]]$Names) {
    $parts = @()
    foreach ($name in $Names) {
        if ($null -eq $Rule -or -not $Rule.PSObject.Properties[$name]) { $parts += ($name + '=Unknown'); continue }
        $text = Get-Text $Rule.$name
        if ($text) { $parts += ($name + '=' + $text) }
    }
    return $parts -join '; '
}

$types = @(
    @{ Type = 'AntiSpam'; Link = 'HostedContentFilterPolicy'; Include = @('SentTo', 'SentToMemberOf', 'RecipientDomainIs'); Exclude = @('ExceptIfSentTo', 'ExceptIfSentToMemberOf', 'ExceptIfRecipientDomainIs')
       Settings = @('SpamAction', 'HighConfidenceSpamAction', 'PhishSpamAction', 'HighConfidencePhishAction', 'BulkThreshold', 'QuarantineRetentionPeriod', 'AllowedSenders', 'AllowedSenderDomains')
       Counted = @('AllowedSenders', 'AllowedSenderDomains') },
    @{ Type = 'OutboundSpam'; Link = 'HostedOutboundSpamFilterPolicy'; Include = @('From', 'FromMemberOf', 'SenderDomainIs'); Exclude = @('ExceptIfFrom', 'ExceptIfFromMemberOf', 'ExceptIfSenderDomainIs')
       Settings = @('RecipientLimitExternalPerHour', 'RecipientLimitInternalPerHour', 'RecipientLimitPerDay', 'ActionWhenThresholdReached', 'AutoForwardingMode')
       Counted = @() },
    @{ Type = 'AntiPhish'; Link = 'AntiPhishPolicy'; Include = @('SentTo', 'SentToMemberOf', 'RecipientDomainIs'); Exclude = @('ExceptIfSentTo', 'ExceptIfSentToMemberOf', 'ExceptIfRecipientDomainIs')
       Settings = @('Enabled', 'PhishThresholdLevel', 'EnableSpoofIntelligence', 'EnableMailboxIntelligence', 'EnableTargetedUserProtection', 'EnableOrganizationDomainsProtection', 'HonorDmarcPolicy')
       Counted = @() },
    @{ Type = 'AntiMalware'; Link = 'MalwareFilterPolicy'; Include = @('SentTo', 'SentToMemberOf', 'RecipientDomainIs'); Exclude = @('ExceptIfSentTo', 'ExceptIfSentToMemberOf', 'ExceptIfRecipientDomainIs')
       Settings = @('EnableFileFilter', 'FileTypeAction', 'ZapEnabled', 'EnableInternalSenderAdminNotifications')
       Counted = @() }
)

foreach ($type in $types) {
    if ($PolicyType -notcontains $type.Type) { continue }
    switch ($type.Type) {
        'AntiSpam' { $policies = @(Get-HostedContentFilterPolicy); $rules = @(Get-HostedContentFilterRule) }
        'OutboundSpam' { $policies = @(Get-HostedOutboundSpamFilterPolicy); $rules = @(Get-HostedOutboundSpamFilterRule) }
        'AntiPhish' { $policies = @(Get-AntiPhishPolicy); $rules = @(Get-AntiPhishRule) }
        'AntiMalware' { $policies = @(Get-MalwareFilterPolicy); $rules = @(Get-MalwareFilterRule) }
    }
    $matched = @{}
    foreach ($policy in $policies) {
        $name = Get-Text (Get-Value $policy 'Name')
        $identity = Get-Text (Get-Value $policy 'Identity')
        $isDefault = Get-Flag $policy 'IsDefault'
        $settings = Get-Settings $policy $type.Settings $type.Counted
        $own = @($rules | Where-Object {
            $link = Get-Text (Get-Value $_ $type.Link)
            $link -ne '' -and ($link -eq $name -or $link -eq $identity)
        })
        foreach ($rule in $own) { $matched[[string](Get-Text (Get-Value $rule 'Name'))] = $true }
        if ($own.Count -eq 0) {
            # Only a policy Exchange marks as custom can be said to apply to no one: a preset policy has its own rule type.
            $recommended = 'Unknown'
            if ($null -ne $policy -and $policy.PSObject.Properties['RecommendedPolicyType']) { $recommended = Get-Text $policy.RecommendedPolicyType }
            $state = 'Unknown'
            $note = 'No rule of this type names this policy, and Exchange did not return whether it is a custom or preset policy.'
            if ($recommended -eq 'Custom') { $state = 'NoRule'; $note = 'No rule applies this policy to anyone.' }
            if ($recommended -eq 'Standard' -or $recommended -eq 'Strict') {
                $state = 'Preset'
                $note = 'Preset security policy (' + $recommended + '): scoped by a preset rule this item does not read, so who it applies to is not shown.'
            }
            if ($isDefault -eq 'True') { $state = 'Default'; $note = 'Default policy: applies to everyone no other policy of this type covers.' }
            if ($isDefault -eq 'Unknown') { $state = 'Unknown'; $note = 'Exchange did not return whether this is the default policy, and no rule names it.' }
            $row = [pscustomobject]@{
                PolicyType = $type.Type; Policy = $name; IsDefault = $isDefault; Rule = ''; RuleState = $state; Priority = ''
                AppliesTo = ''; Excludes = ''; KeySettings = $settings; Notes = $note
            }
            if (Test-UnknownValue $row) { $unknownRows++ }
            $row
            continue
        }
        foreach ($rule in $own) {
            $state = Get-Text (Get-Value $rule 'State')
            $notes = @()
            if (-not $state) { $state = 'Unknown'; $notes += 'Exchange did not return whether this rule is enabled.' }
            $row = [pscustomobject]@{
                PolicyType = $type.Type; Policy = $name; IsDefault = $isDefault; Rule = Get-Text (Get-Value $rule 'Name'); RuleState = $state
                Priority = Get-Text (Get-Value $rule 'Priority'); AppliesTo = Get-Scope $rule $type.Include; Excludes = Get-Scope $rule $type.Exclude
                KeySettings = $settings; Notes = $notes -join ' '
            }
            if (Test-UnknownValue $row) { $unknownRows++ }
            $row
        }
    }
    foreach ($rule in $rules) {
        if ($matched.ContainsKey([string](Get-Text (Get-Value $rule 'Name')))) { continue }
        $state = Get-Text (Get-Value $rule 'State')
        if (-not $state) { $state = 'Unknown' }
        $row = [pscustomobject]@{
            PolicyType = $type.Type; Policy = Get-Text (Get-Value $rule $type.Link); IsDefault = 'Unknown'; Rule = Get-Text (Get-Value $rule 'Name')
            RuleState = $state; Priority = Get-Text (Get-Value $rule 'Priority')
            AppliesTo = Get-Scope $rule $type.Include; Excludes = Get-Scope $rule $type.Exclude; KeySettings = 'Unknown'
            Notes = 'This rule names a policy that was not returned, so its settings are unknown.'
        }
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
