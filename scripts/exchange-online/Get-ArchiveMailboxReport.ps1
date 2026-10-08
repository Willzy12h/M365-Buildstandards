<#
Archive mailbox status: whether each mailbox has an archive, its status, name and quotas, whether auto-expanding
archiving is on for the mailbox and for the organisation.
Read only. Archive size is not reported: it needs one archive statistics read per mailbox, which this item does not
make. A quota is a configured value, not proof of a licence entitlement. A true or false value Exchange did not return
is shown as Unknown, never as False; with only archives ticked, a mailbox whose archive state is Unknown is kept.
#>
param(
    [string[]]$Mailbox,
    [switch]$OnlyWithArchive,
    [int]$MaxMailboxes = 2000
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
$unknown = 0
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
    if ($hasArchive -eq 'Unknown') { $unknown++; $notes += 'Exchange did not return an archive GUID, so whether this mailbox has an archive is unknown.' }
    if ($OnlyWithArchive -and $hasArchive -eq 'False') { continue }
    $autoExpanding = Get-Flag $target 'AutoExpandingArchiveEnabled'
    if ($autoExpanding -eq 'Unknown') { $notes += 'Exchange did not return whether auto-expanding archiving is on for this mailbox.' }
    [pscustomobject]@{
        Mailbox = [string]$target.PrimarySmtpAddress
        RecipientTypeDetails = Get-Text (Get-Value $target 'RecipientTypeDetails')
        HasArchive = $hasArchive
        ArchiveStatus = Get-Text (Get-Value $target 'ArchiveStatus')
        ArchiveName = Get-Text (Get-Value $target 'ArchiveName')
        ArchiveQuota = Get-Text (Get-Value $target 'ArchiveQuota')
        ArchiveWarningQuota = Get-Text (Get-Value $target 'ArchiveWarningQuota')
        AutoExpandingArchive = $autoExpanding
        OrganisationAutoExpanding = $orgAutoExpanding
        Notes = $notes -join ' '
    }
}
if ($unknown -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknown + ' mailbox(es) did not return whether they have an archive. They are listed with HasArchive Unknown.')
}
if ($orgAutoExpanding -eq 'Unknown') {
    Write-Warning 'BDIT:UNKNOWN Whether auto-expanding archiving is on for the organisation could not be read. OrganisationAutoExpanding is Unknown on every row.'
}
