<#
Archive mailbox status: whether each mailbox has an archive, its status, name and quotas, whether auto-expanding
archiving is on for the mailbox and for the organisation.
Read only. Archive size is not reported: it needs one archive statistics read per mailbox, which this item does not
make. A quota is a configured value, not proof of a licence entitlement. A true or false value Exchange did not return
is shown as Unknown, never as False; with only archives ticked, a mailbox whose archive state is Unknown is kept.
A status, name or quota Exchange did not return is Unknown, never blank. A mailbox with no archive has ArchiveName
NotApplicable; its quotas are still shown, because Exchange sets them on every mailbox.
#>
param(
    [string[]]$Mailbox,
    [switch]$OnlyWithArchive,
    [int]$MaxMailboxes = 2000
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

$organisation = Get-OrganizationConfig
$orgAutoExpanding = Get-Flag $organisation 'AutoExpandingArchiveEnabled'

$properties = @('ArchiveGuid', 'ArchiveStatus', 'ArchiveName', 'ArchiveQuota', 'ArchiveWarningQuota', 'AutoExpandingArchiveEnabled')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity -Properties $properties })
} else {
    $targets = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties $properties)
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
}

$empty = [guid]::Empty
foreach ($target in $targets) {
    $notes = @()
    # An archive exists when Exchange returns a non-empty archive GUID; no GUID returned means Unknown.
    $hasArchive = 'Unknown'
    $archiveGuid = Get-Value $target 'ArchiveGuid'
    if ($null -ne $archiveGuid) {
        $parsed = $empty
        if ([guid]::TryParse([string]$archiveGuid, [ref]$parsed)) {
            $hasArchive = 'False'
            if ($parsed -ne $empty) { $hasArchive = 'True' }
        }
    }
    if ($hasArchive -eq 'Unknown') { $notes += 'Exchange did not return an archive GUID, so whether this mailbox has an archive is unknown.' }
    # ArchiveStatus is shown as returned. When it disagrees with the archive GUID, say so rather than choose one.
    $archiveStatus = Get-Text (Get-Value $target 'ArchiveStatus')
    if (($hasArchive -eq 'True' -and $archiveStatus -ne 'Active') -or ($hasArchive -eq 'False' -and $archiveStatus -eq 'Active')) {
        $shown = $archiveStatus
        if (-not $shown) { $shown = 'not returned' }
        $notes += ('ArchiveStatus (' + $shown + ') does not agree with the archive GUID. Check the archive in the admin centre before relying on HasArchive.')
    }
    if (-not $archiveStatus) { $archiveStatus = 'Unknown'; $notes += 'Exchange did not return ArchiveStatus.' }
    # A mailbox with no archive has no archive name. Otherwise a name that was not returned is Unknown.
    $archiveName = Get-Text (Get-Value $target 'ArchiveName')
    if ($hasArchive -eq 'False' -and -not $archiveName) { $archiveName = 'NotApplicable' }
    elseif (-not $archiveName) { $archiveName = 'Unknown'; $notes += 'Exchange did not return the archive name.' }
    # Quotas are configured on every mailbox, with or without an archive, so they are shown as returned (including
    # Unlimited). A quota that was not returned is Unknown, never blank.
    $quotas = @{}
    foreach ($quota in @('ArchiveQuota', 'ArchiveWarningQuota')) {
        $quotas[$quota] = Get-Text (Get-Value $target $quota)
        if (-not $quotas[$quota]) { $quotas[$quota] = 'Unknown'; $notes += ('Exchange did not return ' + $quota + '.') }
    }
    if ($OnlyWithArchive -and $hasArchive -eq 'False') { continue }
    $autoExpanding = Get-Flag $target 'AutoExpandingArchiveEnabled'
    if ($autoExpanding -eq 'Unknown') { $notes += 'Exchange did not return whether auto-expanding archiving is on for this mailbox.' }
    $row = [pscustomobject]@{
        Mailbox = [string]$target.PrimarySmtpAddress
        RecipientTypeDetails = Get-Text (Get-Value $target 'RecipientTypeDetails')
        HasArchive = $hasArchive
        ArchiveStatus = $archiveStatus
        ArchiveName = $archiveName
        ArchiveQuota = $quotas['ArchiveQuota']
        ArchiveWarningQuota = $quotas['ArchiveWarningQuota']
        AutoExpandingArchive = $autoExpanding
        OrganisationAutoExpanding = $orgAutoExpanding
        Notes = $notes -join ' '
    }
    if (Test-UnknownValue $row) { $unknownRows++ }
    $row
}
if ($orgAutoExpanding -eq 'Unknown') {
    Write-Warning 'BDIT:UNKNOWN Whether auto-expanding archiving is on for the organisation could not be read. OrganisationAutoExpanding is Unknown on every row.'
}
if ($unknownRows -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknownRows + ' row(s) hold a value Exchange did not return or that could not be read. Each is shown as Unknown and Notes says why.')
}
