<#
Messaging records management (MRM) retention policies and their retention tags: each policy's tags, and each tag's
type, age limit, retention action and whether it is enabled. With unlinked tags ticked, a tag no policy links to is
NotLinked, but only when every policy's links were returned and resolved; otherwise its link state is Unknown.
Read only. A policy's tag links are matched to tags only when a link is exactly a tag's Name, Identity or
DistinguishedName as Exchange returns them, and exactly one tag matches; anything else is Unresolved and its tag
settings are not shown. A true or false value Exchange did not return is shown as Unknown, never as False. An age limit
that was not returned is Unknown, except on a tag Exchange reports as having retention disabled, where it is
NotApplicable. Microsoft Purview retention policies and labels are not read.
#>
param(
    [switch]$IncludeUnlinkedTags
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
    return ((@($Value) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ }) -join '; ') -replace '\s+', ' '
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

# The value as returned; NotSet when it was returned empty; Unknown when it was not returned or was returned null.
function Get-Setting([object]$Item, [string]$Name) {
    if ($null -eq $Item -or -not $Item.PSObject.Properties[$Name] -or $null -eq $Item.$Name) { return 'Unknown' }
    $text = Get-Text $Item.$Name
    if ($text -eq '') { return 'NotSet' }
    return $text
}

$policies = @(Get-RetentionPolicy)
$tags = @(Get-RetentionPolicyTag)

# Each tag's exact identities, as Exchange returned them. Nothing is matched on a display name or by case folding.
function Get-TagKeys([object]$Tag) {
    return @(@('Name', 'Identity', 'DistinguishedName') | ForEach-Object { Get-Text (Get-Value $Tag $_) } | Where-Object { $_ })
}

function Get-TagRow([string]$Policy, [string]$PolicyIsDefault, [string]$LinkAsReturned, [string]$LinkStatus, [object]$Tag, [string[]]$Notes) {
    $notes = @($Notes)
    $tagName = ''; $type = ''; $age = ''; $action = ''; $enabled = ''
    if ($null -ne $Tag) {
        $tagName = Get-Text (Get-Value $Tag 'Name')
        if (-not $tagName) { $tagName = 'Unknown'; $notes += 'Exchange did not return the tag name.' }
        $type = Get-Setting $Tag 'Type'
        $enabled = Get-Flag $Tag 'RetentionEnabled'
        $action = Get-Setting $Tag 'RetentionAction'
        $age = Get-Setting $Tag 'AgeLimitForRetention'
        if ($age -eq 'Unknown' -and $enabled -eq 'False') { $age = 'NotApplicable' }
        $missing = @()
        foreach ($pair in @(@('Type', $type), @('RetentionEnabled', $enabled), @('RetentionAction', $action), @('AgeLimitForRetention', $age))) {
            if ($pair[1] -eq 'Unknown') { $missing += $pair[0] }
        }
        if ($missing.Count -gt 0) { $notes += ('Exchange did not return ' + ($missing -join ', ') + ' for this tag.') }
    }
    [pscustomobject]@{
        Policy = $Policy
        PolicyIsDefault = $PolicyIsDefault
        TagLinkAsReturned = $LinkAsReturned
        LinkStatus = $LinkStatus
        Tag = $tagName
        TagType = $type
        AgeLimitForRetention = $age
        RetentionAction = $action
        RetentionEnabled = $enabled
        Notes = $notes -join ' '
    }
}

$linkedTags = @{}
# A tag can be called NotLinked only when every policy's links were returned and every link resolved to one tag.
$linksUncertain = $false
foreach ($policy in $policies) {
    $policyName = Get-Text (Get-Value $policy 'Name')
    if (-not $policyName) { $policyName = 'Unknown' }
    $isDefault = Get-Flag $policy 'IsDefault'
    $links = $null
    if ($null -ne $policy -and $policy.PSObject.Properties['RetentionPolicyTagLinks']) { $links = $policy.RetentionPolicyTagLinks }
    if ($null -eq $links) {
        $linksUncertain = $true
        $row = Get-TagRow $policyName $isDefault '' 'Unknown' $null @('Exchange did not return this policy''s tag links, so which tags it holds is unknown.')
        $unknownRows++
        $row
        continue
    }
    $linkList = @(@($links) | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ } | Where-Object { $_ -ne '' })
    if ($linkList.Count -eq 0) {
        $row = Get-TagRow $policyName $isDefault '' 'NoTags' $null @('Exchange returned no tag links for this policy.')
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
        continue
    }
    foreach ($link in $linkList) {
        $found = @($tags | Where-Object { (Get-TagKeys $_) -contains $link })
        if ($found.Count -eq 1) {
            foreach ($key in (Get-TagKeys $found[0])) { $linkedTags[$key] = $true }
            $row = Get-TagRow $policyName $isDefault $link 'Resolved' $found[0] @()
        } else {
            $why = 'No returned tag has this exact Name, Identity or DistinguishedName, so the tag settings are not shown.'
            if ($found.Count -gt 1) { $why = 'More than one returned tag has this exact value, so the tag settings are not shown.' }
            $row = Get-TagRow $policyName $isDefault $link 'Unresolved' $null @($why)
        }
        if ((Test-UnknownValue $row) -or $row.LinkStatus -eq 'Unresolved') { $unknownRows++ }
        $row
    }
}
if ($IncludeUnlinkedTags) {
    foreach ($tag in $tags) {
        $keys = @(Get-TagKeys $tag)
        if (@($keys | Where-Object { $linkedTags.ContainsKey($_) }).Count -gt 0) { continue }
        $notes = @('No returned policy links to this tag by an exact identity.')
        $status = 'NotLinked'
        if ($linksUncertain) {
            $status = 'Unknown'
            $notes = @('A policy''s tag links were not returned or did not resolve to exactly one tag, so whether a policy links to this tag is unknown.')
        }
        if ($keys.Count -eq 0) {
            $status = 'Unknown'
            $notes = @('Exchange returned this tag without a Name, Identity or DistinguishedName, so whether a policy links to it is unknown.')
        }
        $row = Get-TagRow '' '' '' $status $tag $notes
        if (Test-UnknownValue $row) { $unknownRows++ }
        $row
    }
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return, a tag link that could not be resolved to exactly one tag, or a value that could not be read. Each is shown as Unknown or Unresolved and Notes says why.')
}
