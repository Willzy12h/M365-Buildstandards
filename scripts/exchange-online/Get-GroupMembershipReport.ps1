<#
Members and owners of named distribution, mail-enabled security and Microsoft 365 groups.
Read only. Each member is listed with the exact identities Exchange returned for it (primary SMTP address and object
ID); a member returned without either is Unresolved. A distribution group's owners may be returned by Exchange as
names, so each is looked up and is Resolved only when Exchange finds exactly one recipient whose primary SMTP address,
distinguished name, object ID or GUID is that value. A name, alias or display name, several matches or none is
Unresolved. Nested groups are listed as members and not expanded. Dynamic distribution groups are calculated at
delivery and are not expanded.
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Group,
    [switch]$IncludeOwners,
    [int]$MaxMembers = 5000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Value([object]$Item, [string]$Name) {
    if ($null -ne $Item -and $Item.PSObject.Properties[$Name]) { return $Item.$Name }
    return $null
}

$lookups = @{}
# Exchange's own identity lookup, accepted only when the value is an exact identity (primary SMTP address, distinguished
# name, object ID or GUID) of the one recipient it finds. A name, alias or display name is never treated as exact.
function Get-HolderIdentity([string]$Value) {
    if ($lookups.ContainsKey($Value)) { return $lookups[$Value] }
    $found = @()
    $failure = ''
    try { $found = @(Get-EXORecipient -Identity $Value -Properties DistinguishedName, DisplayName) }
    catch { $failure = $_.Exception.Message }
    $result = [pscustomobject]@{ Status = 'Unresolved'; Address = ''; ObjectId = ''; Type = ''; DisplayName = ''; Note = '' }
    if ($found.Count -eq 1) {
        $candidate = $found[0]
        $unique = @('PrimarySmtpAddress', 'DistinguishedName', 'ExternalDirectoryObjectId', 'Guid') |
            ForEach-Object { [string](Get-Value $candidate $_) } | Where-Object { $_ }
        $exact = @($unique | Where-Object { [string]::Equals($_, $Value, [StringComparison]::OrdinalIgnoreCase) })
        if ($exact.Count -gt 0) {
            $result.Status = 'Resolved'
            $result.Address = [string](Get-Value $candidate 'PrimarySmtpAddress')
            $result.ObjectId = [string](Get-Value $candidate 'ExternalDirectoryObjectId')
            $result.Type = [string](Get-Value $candidate 'RecipientTypeDetails')
            $result.DisplayName = [string](Get-Value $candidate 'DisplayName')
        } else {
            $result.Note = 'Exchange returned this value as a name, alias or display name, which is not an exact identity and another recipient can share. Confirm who it is before acting.'
        }
    } elseif ($found.Count -gt 1) {
        $result.Note = 'This value matches more than one recipient. Confirm who it is before acting.'
    } else {
        $result.Note = 'Exchange could not resolve this value to a recipient.'
        if ($failure) { $result.Note = $result.Note + ' ' + $failure }
    }
    $lookups[$Value] = $result
    return $result
}

function Get-MemberRow([string]$GroupAddress, [string]$GroupType, [string]$Relationship, [object]$Member) {
    $address = [string](Get-Value $Member 'PrimarySmtpAddress')
    $objectId = [string](Get-Value $Member 'ExternalDirectoryObjectId')
    $status = 'Exact'
    $notes = @()
    if (-not $address -and -not $objectId) {
        $status = 'Unresolved'
        $notes += 'Exchange returned this entry without an address or object ID. Confirm who it is before acting.'
    }
    [pscustomobject]@{
        Group = $GroupAddress; GroupType = $GroupType; Relationship = $Relationship; Status = $status
        MemberName = [string](Get-Value $Member 'DisplayName'); MemberAddress = $address; MemberObjectId = $objectId
        MemberType = [string](Get-Value $Member 'RecipientTypeDetails'); AsReturned = [string](Get-Value $Member 'Name'); Notes = $notes -join ' '
    }
}

function Get-Bounded([object[]]$Items, [string]$What) {
    if ($Items.Count -gt $MaxMembers) {
        Write-Warning ('BDIT:PARTIAL ' + $What + ' has more than ' + $MaxMembers + ' entries; only the first ' + $MaxMembers + ' are listed.')
        return @($Items | Select-Object -First $MaxMembers)
    }
    return $Items
}

$distributionTypes = @('MailUniversalDistributionGroup', 'MailUniversalSecurityGroup', 'MailNonUniversalGroup', 'RoomList')
$unknown = 0
$unresolved = 0
foreach ($identity in $Group) {
    $found = @(Get-EXORecipient -Identity $identity)
    if ($found.Count -ne 1) { throw ($identity + ' matches ' + $found.Count + ' recipients. Name the group by its email address.') }
    $target = $found[0]
    $type = [string](Get-Value $target 'RecipientTypeDetails')
    $address = [string](Get-Value $target 'PrimarySmtpAddress')
    $key = $address
    if (-not $key) { $key = [string](Get-Value $target 'ExternalDirectoryObjectId') }
    if (-not $key) { throw ('Exchange returned no address or object ID for ' + $identity + '.') }

    if ($distributionTypes -contains $type) {
        $members = Get-Bounded @(Get-DistributionGroupMember -Identity $key -ResultSize ($MaxMembers + 1)) ($address + ' membership')
        foreach ($member in $members) {
            $row = Get-MemberRow $address $type 'Member' $member
            if ($row.Status -eq 'Unresolved') { $unresolved++ }
            $row
        }
        if ($IncludeOwners) {
            $details = Get-DistributionGroup -Identity $key
            if ($null -eq $details -or -not $details.PSObject.Properties['ManagedBy'] -or $null -eq $details.ManagedBy) {
                $unknown++
                [pscustomobject]@{
                    Group = $address; GroupType = $type; Relationship = 'Owner'; Status = 'Unknown'; MemberName = ''; MemberAddress = ''
                    MemberObjectId = ''; MemberType = ''; AsReturned = ''; Notes = 'Exchange did not return the owners of this group.'
                }
                continue
            }
            # Bounded before any lookup, so a long owner list cannot make one recipient read per entry past the limit.
            $owners = Get-Bounded @(@($details.ManagedBy) | Where-Object { $null -ne $_ -and [string]$_ -ne '' }) ($address + ' ownership')
            foreach ($owner in $owners) {
                $resolved = Get-HolderIdentity ([string]$owner)
                if ($resolved.Status -ne 'Resolved') { $unresolved++ }
                [pscustomobject]@{
                    Group = $address; GroupType = $type; Relationship = 'Owner'; Status = $resolved.Status; MemberName = $resolved.DisplayName
                    MemberAddress = $resolved.Address; MemberObjectId = $resolved.ObjectId; MemberType = $resolved.Type
                    AsReturned = [string]$owner; Notes = $resolved.Note
                }
            }
        }
    } elseif ($type -eq 'GroupMailbox') {
        $members = Get-Bounded @(Get-UnifiedGroupLinks -Identity $key -LinkType Members -ResultSize ($MaxMembers + 1)) ($address + ' membership')
        foreach ($member in $members) {
            $row = Get-MemberRow $address $type 'Member' $member
            if ($row.Status -eq 'Unresolved') { $unresolved++ }
            $row
        }
        if ($IncludeOwners) {
            $owners = Get-Bounded @(Get-UnifiedGroupLinks -Identity $key -LinkType Owners -ResultSize ($MaxMembers + 1)) ($address + ' ownership')
            foreach ($owner in $owners) {
                $row = Get-MemberRow $address $type 'Owner' $owner
                if ($row.Status -eq 'Unresolved') { $unresolved++ }
                $row
            }
        }
    } elseif ($type -eq 'DynamicDistributionGroup') {
        $unknown++
        [pscustomobject]@{
            Group = $address; GroupType = $type; Relationship = 'Member'; Status = 'NotExpanded'; MemberName = ''; MemberAddress = ''
            MemberObjectId = ''; MemberType = ''; AsReturned = ''
            Notes = 'Dynamic membership is calculated by Exchange when mail is delivered and is not listed here.'
        }
    } else {
        throw ($identity + ' is a ' + $type + ', not a distribution, mail-enabled security or Microsoft 365 group.')
    }
}
if ($unknown -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknown + ' group membership or ownership list(s) could not be listed. Each is shown with Status Unknown or NotExpanded and the reason.')
}
if ($unresolved -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unresolved + ' member(s) or owner(s) could not be resolved to exactly one recipient. They are listed as Unresolved.')
}
