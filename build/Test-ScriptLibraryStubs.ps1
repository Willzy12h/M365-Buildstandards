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
}

$stub = @'
$script:auditPages = 0
function Connect-ExchangeOnline { }
function Disconnect-ExchangeOnline { [IO.File]::WriteAllText($env:BDIT_STUB_DISCONNECT, 'disconnected') }
function Get-ConnectionInformation {
    [pscustomobject]@{ State = 'Connected'; IsEopSession = $false; TenantID = $env:BDIT_STUB_TENANT; UserPrincipalName = 'admin@contoso.example' }
}
function Get-Mailboxes {
    @(
        [pscustomobject]@{
            DisplayName = 'Alex Wilber'; PrimarySmtpAddress = 'alex@contoso.example'; RecipientTypeDetails = 'UserMailbox'
            ExchangeGuid = [guid]'11111111-1111-1111-1111-111111111111'
            IssueWarningQuota = '98 GB (105,226,698,752 bytes)'; ProhibitSendQuota = '99 GB (106,300,440,576 bytes)'
            ProhibitSendReceiveQuota = '100 GB (107,374,182,400 bytes)'; ArchiveStatus = 'Active'; LitigationHoldEnabled = $false
            GrantSendOnBehalfTo = @('Megan Bowen'); ForwardingSmtpAddress = 'smtp:out@external.example'; ForwardingAddress = $null
            DeliverToMailboxAndForward = $true; HiddenFromAddressListsEnabled = $false
        },
        [pscustomobject]@{
            DisplayName = 'Reception'; PrimarySmtpAddress = 'reception@contoso.example'; RecipientTypeDetails = 'SharedMailbox'
            ExchangeGuid = [guid]'22222222-2222-2222-2222-222222222222'
            IssueWarningQuota = 'Unlimited'; ProhibitSendQuota = 'Unlimited'; ProhibitSendReceiveQuota = 'Unlimited'
            ArchiveStatus = 'None'; LitigationHoldEnabled = $true; GrantSendOnBehalfTo = @(); ForwardingSmtpAddress = $null
            ForwardingAddress = 'Internal Contact'; DeliverToMailboxAndForward = $false; HiddenFromAddressListsEnabled = $true
        }
    )
}
function Get-EXOMailbox { Get-Mailboxes }
function Get-EXOMailboxStatistics { [pscustomobject]@{ TotalItemSize = '96.5 GB (103,616,086,016 bytes)'; ItemCount = 1234 } }
function Get-EXOMailboxPermission {
    @(
        [pscustomobject]@{ User = 'NT AUTHORITY\SELF'; AccessRights = @('FullAccess', 'ReadPermission'); IsInherited = $false; Deny = $false },
        [pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false },
        [pscustomobject]@{ User = 'admins@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $true; Deny = $false }
    )
}
function Get-EXORecipientPermission {
    [pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' }
}
function Get-EXORecipient {
    [pscustomobject]@{ PrimarySmtpAddress = 'alex@contoso.example'; Name = 'alex'; DisplayName = 'Alex Wilber'; DistinguishedName = 'CN=alex' }
}
function Get-EXOMailboxFolderStatistics {
    @([pscustomobject]@{ FolderType = 'Inbox'; FolderPath = '/Inbox' }, [pscustomobject]@{ FolderType = 'Calendar'; FolderPath = '/Calendar' })
}
function Get-EXOMailboxFolderPermission {
    [pscustomobject]@{ User = 'Default'; AccessRights = @('AvailabilityOnly'); SharingPermissionFlags = $null }
}
function Get-InboxRule {
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
function Get-AcceptedDomain { [pscustomobject]@{ DomainName = 'contoso.example' } }
function Get-User { [pscustomobject]@{ AccountDisabled = $true } }
function Get-MessageTraceV2 {
    [pscustomobject]@{
        Received = [datetime]::UtcNow.AddDays(-1); SenderAddress = 'alex@contoso.example'; RecipientAddress = 'x@external.example'
        Subject = 'Quarterly report'; Status = 'Delivered'; FromIP = '203.0.113.10'; ToIP = '198.51.100.20'; Size = 20480
        MessageId = '<abc@contoso.example>'; MessageTraceId = [guid]::NewGuid()
    }
}
function Search-UnifiedAuditLog {
    $script:auditPages++
    if ($script:auditPages -gt 1) { return }
    [pscustomobject]@{
        CreationDate = [datetime]::UtcNow.AddDays(-2); UserIds = 'alex@contoso.example'; Operations = 'MailItemsAccessed'
        RecordType = 'ExchangeItem'; AuditData = '{"Workload":"Exchange","ObjectId":"alex@contoso.example","ClientIP":"203.0.113.10"}'
    }
}
Export-ModuleMember -Function *
'@

$work = Join-Path ([IO.Path]::GetTempPath()) ('bdit-library-' + [guid]::NewGuid().ToString('N'))
$moduleDirectory = Join-Path (Join-Path (Join-Path $work 'modules') 'ExchangeOnlineManagement') '99.0.0'
[void][IO.Directory]::CreateDirectory($moduleDirectory)
try {
    [IO.File]::WriteAllText((Join-Path $moduleDirectory 'ExchangeOnlineManagement.psm1'), $stub)
    [IO.File]::WriteAllText((Join-Path $moduleDirectory 'ExchangeOnlineManagement.psd1'),
        "@{ ModuleVersion = '99.0.0'; RootModule = 'ExchangeOnlineManagement.psm1'; GUID = '$([guid]::NewGuid())'; FunctionsToExport = '*' }")
    $modulePath = Join-Path $work 'modules'
    $env:PSModulePath = $modulePath + [IO.Path]::PathSeparator + $env:PSModulePath

    $manifests = @(Get-ChildItem -LiteralPath $Library -Recurse -Filter '*.json' | Where-Object { $_.Name -ne 'registry.json' })
    $copies = @()
    foreach ($manifestFile in $manifests) {
        $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
        if (-not $samples.ContainsKey($manifest.id)) { throw ($manifest.id + ' has no sample form in this test.') }
        $copy = Join-Path $work ($manifest.id + '.ps1')
        & dotnet $Bdit script --id $manifest.id --copy --tenant $tenant --tenant-name 'Contoso (synthetic)' --account 'admin@contoso.example' --out $copy @($samples[$manifest.id])
        if ($LASTEXITCODE -ne 0) { throw ($manifest.id + ': bdit could not produce a Copy script.') }
        $copies += $copy

        $csv = Join-Path $work ($manifest.id + '.csv')
        $env:BDIT_STUB_TENANT = $tenant
        $env:BDIT_STUB_DISCONNECT = Join-Path $work ($manifest.id + '.disconnected')
        # Windows PowerShell 5.1 turns a child's stderr into a terminating error under 'Stop'; the exit code is what is checked.
        & { $ErrorActionPreference = 'Continue'; & $PowerShell -NoLogo -NoProfile -NonInteractive -File $copy -OutputCsv $csv 2>&1 | Out-Null }
        if ($LASTEXITCODE -ne 0) { throw ($manifest.id + ': the Copy script failed against the synthetic module.') }
        if (-not (Test-Path -LiteralPath $env:BDIT_STUB_DISCONNECT)) { throw ($manifest.id + ': the Copy script did not disconnect.') }
        $header = ((Get-Content -LiteralPath $csv -TotalCount 1) -replace '"', '') -split ','
        if (($header -join ',') -cne (@($manifest.outputSchema.columns) -join ',')) {
            throw ($manifest.id + ': CSV columns ' + ($header -join ',') + ' differ from the manifest.')
        }
        $rows = @(Import-Csv -LiteralPath $csv)
        if ($rows.Count -lt 1) { throw ($manifest.id + ': no synthetic rows were written.') }
        Write-Output ($manifest.id + ': ' + $rows.Count + ' synthetic row(s), manifest columns, disconnected.')

        # Signed in to another tenant: the wrapper must refuse before reading, still disconnect, and write nothing.
        $wrongCsv = Join-Path $work ($manifest.id + '-wrong.csv')
        $env:BDIT_STUB_TENANT = '9b2c1d4e-0000-4000-8000-000000000000'
        $env:BDIT_STUB_DISCONNECT = Join-Path $work ($manifest.id + '-wrong.disconnected')
        & { $ErrorActionPreference = 'Continue'; & $PowerShell -NoLogo -NoProfile -NonInteractive -File $copy -OutputCsv $wrongCsv 2>&1 | Out-Null }
        if ($LASTEXITCODE -eq 0 -or (Test-Path -LiteralPath $wrongCsv)) { throw ($manifest.id + ': a different tenant was not refused.') }
        if (-not (Test-Path -LiteralPath $env:BDIT_STUB_DISCONNECT)) { throw ($manifest.id + ': the refused run did not disconnect.') }
    }
    $check = Join-Path $PSScriptRoot 'Test-ScriptLibrary.ps1'
    & $check -Library $Library -CopyScript $copies
    # The last child run was the expected refusal; do not leave its exit code for the calling step to report.
    $global:LASTEXITCODE = 0
    Write-Output ('Synthetic run passed for ' + $manifests.Count + ' item(s); a different tenant was refused each time. No Microsoft module or tenant was used.')
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
