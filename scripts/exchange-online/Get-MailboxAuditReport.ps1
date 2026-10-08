<#
Mailbox auditing: the organisation setting, unified audit log ingestion and, per mailbox, AuditEnabled, the audited
actions, log age and whether the mailbox's account bypasses auditing.
Read only. Finding is Bypassed when the account bypasses mailbox auditing, OrganisationAuditingOff when auditing is
turned off for the organisation, Unknown when either could not be read, and NoneFound otherwise; NoneFound is not proof
that events are recorded. A true or false value Exchange did not return is shown as Unknown, never as False.
#>
param(
    [string[]]$Mailbox,
    [switch]$IncludeBypass,
    [int]$MaxMailboxes = 1000
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
$orgDisabled = Get-Flag $organisation 'AuditDisabled'
# Reading the admin audit log configuration needs an audit role; without it the value is Unknown, not False.
$ingestion = 'Unknown'
$ingestionNote = ''
try { $ingestion = Get-Flag (Get-AdminAuditLogConfig) 'UnifiedAuditLogIngestionEnabled' }
catch { $ingestionNote = 'Unified audit log ingestion could not be read: ' + $_.Exception.Message }

$properties = @('AuditEnabled', 'AuditLogAgeLimit', 'DefaultAuditSet', 'AuditOwner', 'AuditDelegate', 'AuditAdmin')
if ($Mailbox) {
    $targets = @(foreach ($identity in $Mailbox) { Get-EXOMailbox -Identity $identity -Properties $properties })
} else {
    $targets = @(Get-EXOMailbox -ResultSize ($MaxMailboxes + 1) -Properties $properties)
    if ($targets.Count -gt $MaxMailboxes) {
        Write-Warning ('BDIT:PARTIAL More than ' + $MaxMailboxes + ' mailboxes exist; only the first ' + $MaxMailboxes + ' were checked.')
        $targets = @($targets | Select-Object -First $MaxMailboxes)
    }
}

$unknown = 0
foreach ($target in $targets) {
    $notes = @()
    if ($ingestionNote) { $notes += $ingestionNote }
    if ($ingestion -eq 'Unknown' -and -not $ingestionNote) { $notes += 'Exchange did not return whether unified audit log ingestion is on.' }
    $bypass = 'NotChecked'
    if ($IncludeBypass) {
        $bypass = Get-Flag (Get-MailboxAuditBypassAssociation -Identity $target.ExchangeGuid.ToString()) 'AuditBypassEnabled'
        if ($bypass -eq 'Unknown') { $notes += 'Exchange did not return whether this account bypasses mailbox auditing.' }
    }
    $enabled = Get-Flag $target 'AuditEnabled'
    if ($enabled -eq 'Unknown') { $notes += 'Exchange did not return AuditEnabled.' }
    if ($orgDisabled -eq 'Unknown') { $notes += 'Exchange did not return whether auditing is turned off for the organisation.' }
    $finding = 'NoneFound'
    if ($bypass -eq 'NotChecked') { $finding = 'BypassNotChecked' }
    if ($bypass -eq 'Unknown' -or $orgDisabled -eq 'Unknown') { $finding = 'Unknown' }
    if ($orgDisabled -eq 'True') { $finding = 'OrganisationAuditingOff' }
    if ($bypass -eq 'True') { $finding = 'Bypassed' }
    if ($finding -eq 'Unknown') { $unknown++ }
    [pscustomobject]@{
        Mailbox = [string]$target.PrimarySmtpAddress
        RecipientTypeDetails = Get-Text (Get-Value $target 'RecipientTypeDetails')
        OrganisationAuditDisabled = $orgDisabled
        UnifiedAuditIngestion = $ingestion
        AuditEnabled = $enabled
        AuditBypassEnabled = $bypass
        AuditLogAgeLimit = Get-Text (Get-Value $target 'AuditLogAgeLimit')
        DefaultAuditSet = Get-Text (Get-Value $target 'DefaultAuditSet')
        AuditOwner = Get-Text (Get-Value $target 'AuditOwner')
        AuditDelegate = Get-Text (Get-Value $target 'AuditDelegate')
        AuditAdmin = Get-Text (Get-Value $target 'AuditAdmin')
        Finding = $finding
        Notes = $notes -join ' '
    }
}
if ($unknown -gt 0) {
    Write-Warning ('BDIT:UNKNOWN ' + $unknown + ' mailbox(es) could not be checked fully for auditing. They are listed with Finding Unknown and the reason.')
}
if ($ingestion -eq 'Unknown') {
    Write-Warning 'BDIT:UNKNOWN Whether unified audit log ingestion is on could not be read. UnifiedAuditIngestion is Unknown on every row.'
}
