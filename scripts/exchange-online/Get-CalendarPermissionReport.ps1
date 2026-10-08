<#
Calendar permissions for one or more mailboxes, using each mailbox's own default calendar folder name.
Read only.
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Mailbox
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($identity in $Mailbox) {
    $calendar = @(Get-EXOMailboxFolderStatistics -Identity $identity -FolderScope Calendar | Where-Object { $_.FolderType -eq 'Calendar' }) | Select-Object -First 1
    if ($null -eq $calendar) { throw ('No default calendar folder was found for ' + $identity + '.') }
    $folder = $identity + ':\' + ([string]$calendar.FolderPath).TrimStart('/').Replace('/', '\')
    foreach ($entry in @(Get-EXOMailboxFolderPermission -Identity $folder)) {
        [pscustomobject]@{
            Mailbox = $identity
            Folder = [string]$calendar.FolderPath
            User = [string]$entry.User
            AccessRights = (@($entry.AccessRights) | ForEach-Object { [string]$_ }) -join ', '
            SharingPermissionFlags = [string]$entry.SharingPermissionFlags
        }
    }
}
