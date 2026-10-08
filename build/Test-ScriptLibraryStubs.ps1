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
function Get-Scenario { [string]$env:BDIT_STUB_SCENARIO }
# Every tenant read is logged, so a refused run can be shown to have read nothing.
function Write-StubRead([string]$Name) { if ($env:BDIT_STUB_READS) { [IO.File]::AppendAllText($env:BDIT_STUB_READS, $Name + "`n") } }
function Connect-ExchangeOnline { }
function Disconnect-ExchangeOnline { [IO.File]::WriteAllText($env:BDIT_STUB_DISCONNECT, 'disconnected') }
function Get-ConnectionInformation {
    $upn = 'admin@contoso.example'
    if ($env:BDIT_STUB_UPN) { $upn = $env:BDIT_STUB_UPN }
    [pscustomobject]@{ State = 'Connected'; IsEopSession = $false; TenantID = $env:BDIT_STUB_TENANT; UserPrincipalName = $upn }
}
function New-StubMailbox([string]$Name, [string]$Smtp, [string]$Type, [string]$Guid, [string]$Quota, [object]$Hold, [object]$Hidden, [object]$KeepCopy, [object]$OnBehalf) {
    [pscustomobject]@{
        DisplayName = $Name; PrimarySmtpAddress = $Smtp; RecipientTypeDetails = $Type; ExchangeGuid = [guid]$Guid
        IssueWarningQuota = $Quota; ProhibitSendQuota = $Quota; ProhibitSendReceiveQuota = $Quota; ArchiveStatus = 'None'
        LitigationHoldEnabled = $Hold; GrantSendOnBehalfTo = $OnBehalf; ForwardingSmtpAddress = 'smtp:out@external.example'
        ForwardingAddress = $null; DeliverToMailboxAndForward = $KeepCopy; HiddenFromAddressListsEnabled = $Hidden
    }
}
function Get-Mailboxes {
    $hundred = '100 GB (107,374,182,400 bytes)'
    switch (Get-Scenario) {
        'quota-unknown' {
            return @(
                (New-StubMailbox 'Unlimited' 'unlimited@contoso.example' 'SharedMailbox' '33333333-3333-3333-3333-333333333333' 'Unlimited' $false $false $false @()),
                (New-StubMailbox 'Malformed quota' 'malformed@contoso.example' 'UserMailbox' '44444444-4444-4444-4444-444444444444' 'about 100 GB' $false $false $false @()),
                (New-StubMailbox 'No size' 'nosize@contoso.example' 'UserMailbox' '55555555-5555-5555-5555-555555555555' $hundred $false $false $false @()),
                (New-StubMailbox 'Over' 'over@contoso.example' 'UserMailbox' '66666666-6666-6666-6666-666666666666' $hundred $false $false $false @())
            )
        }
        'flags' {
            return @(
                (New-StubMailbox 'Flags true' 'true@contoso.example' 'SharedMailbox' '77777777-7777-7777-7777-777777777777' $hundred $true $true $true @()),
                (New-StubMailbox 'Flags false' 'false@contoso.example' 'SharedMailbox' '88888888-8888-8888-8888-888888888888' $hundred $false $false $false @()),
                (New-StubMailbox 'Flags null' 'null@contoso.example' 'SharedMailbox' '99999999-9999-9999-9999-999999999999' $hundred $null $null $null @())
            )
        }
        'access' {
            return @(
                (New-StubMailbox 'Exact' 'exact@contoso.example' 'SharedMailbox' 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' $hundred $false $false $false @('Alex Wilber')),
                (New-StubMailbox 'Namesake' 'namesake@contoso.example' 'SharedMailbox' 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' $hundred $false $false $false @('alex.other@contoso.example')),
                (New-StubMailbox 'Denied' 'denied@contoso.example' 'SharedMailbox' 'cccccccc-cccc-cccc-cccc-cccccccccccc' $hundred $false $false $false @('alex@contoso.example'))
            )
        }
    }
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
function Get-EXOMailbox { Write-StubRead 'Get-EXOMailbox'; Get-Mailboxes }
function Get-EXOMailboxStatistics {
    param($Identity)
    Write-StubRead 'Get-EXOMailboxStatistics'
    if ((Get-Scenario) -eq 'quota-unknown' -and [string]$Identity -eq '55555555-5555-5555-5555-555555555555') {
        return [pscustomobject]@{ TotalItemSize = $null; ItemCount = $null }
    }
    [pscustomobject]@{ TotalItemSize = '96.5 GB (103,616,086,016 bytes)'; ItemCount = 1234 }
}
function Get-EXOMailboxPermission {
    param($Identity)
    Write-StubRead 'Get-EXOMailboxPermission'
    switch (Get-Scenario) {
        'flags' {
            return @([pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $null; Deny = $null })
        }
        'access' {
            switch ([string]$Identity) {
                'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa' { return @([pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false }) }
                'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb' {
                    # A different recipient with the same display name, named once by sign-in name and once by display name only.
                    return @(
                        [pscustomobject]@{ User = 'alex.other@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false },
                        [pscustomobject]@{ User = 'Alex Wilber'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false }
                    )
                }
                'cccccccc-cccc-cccc-cccc-cccccccccccc' { return @([pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $true }) }
            }
            return @()
        }
    }
    @(
        [pscustomobject]@{ User = 'NT AUTHORITY\SELF'; AccessRights = @('FullAccess', 'ReadPermission'); IsInherited = $false; Deny = $false },
        [pscustomobject]@{ User = 'alex@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $false; Deny = $false },
        [pscustomobject]@{ User = 'admins@contoso.example'; AccessRights = @('FullAccess'); IsInherited = $true; Deny = $false }
    )
}
function Get-EXORecipientPermission {
    param($Identity, $Trustee, $ResultSize)
    Write-StubRead 'Get-EXORecipientPermission'
    switch (Get-Scenario) {
        'flags' {
            return @([pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $null; AccessControlType = $null })
        }
        'access' {
            return @(
                [pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' },
                [pscustomobject]@{ Identity = 'finance@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Deny' }
            )
        }
        'sendas-saturated' {
            # More entries exist than any one request may return: only the requested number comes back.
            $available = 6000
            if ($null -ne $ResultSize -and [int]$ResultSize -lt $available) { $available = [int]$ResultSize }
            return @(1..$available | ForEach-Object {
                [pscustomobject]@{ Identity = ('shared' + $_ + '@contoso.example'); Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' }
            })
        }
    }
    [pscustomobject]@{ Identity = 'reception@contoso.example'; Trustee = 'alex@contoso.example'; AccessRights = @('SendAs'); IsInherited = $false; AccessControlType = 'Allow' }
}
function Get-EXORecipient {
    Write-StubRead 'Get-EXORecipient'
    [pscustomobject]@{ PrimarySmtpAddress = 'alex@contoso.example'; Name = 'alex'; DisplayName = 'Alex Wilber'; DistinguishedName = 'CN=alex' }
}
function Get-EXOMailboxFolderStatistics {
    Write-StubRead 'Get-EXOMailboxFolderStatistics'
    @([pscustomobject]@{ FolderType = 'Inbox'; FolderPath = '/Inbox' }, [pscustomobject]@{ FolderType = 'Calendar'; FolderPath = '/Calendar' })
}
function Get-EXOMailboxFolderPermission {
    Write-StubRead 'Get-EXOMailboxFolderPermission'
    [pscustomobject]@{ User = 'Default'; AccessRights = @('AvailabilityOnly'); SharingPermissionFlags = $null }
}
function Get-InboxRule {
    Write-StubRead 'Get-InboxRule'
    if ((Get-Scenario) -eq 'flags') {
        return @([pscustomobject]@{
            Name = 'Unreadable flags'; Enabled = $null; Priority = 1; From = $null; SubjectContainsWords = $null
            ForwardTo = @('"Outside" [SMTP:x@external.example]'); ForwardAsAttachmentTo = $null; RedirectTo = $null
            MoveToFolder = $null; DeleteMessage = $null; MarkAsRead = $null; StopProcessingRules = $null
        })
    }
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
function Get-AcceptedDomain { Write-StubRead 'Get-AcceptedDomain'; [pscustomobject]@{ DomainName = 'contoso.example' } }
function Get-User {
    param($Identity)
    Write-StubRead 'Get-User'
    if ((Get-Scenario) -eq 'flags') {
        switch ([string]$Identity) {
            '77777777-7777-7777-7777-777777777777' { return [pscustomobject]@{ AccountDisabled = $true } }
            '88888888-8888-8888-8888-888888888888' { return [pscustomobject]@{ AccountDisabled = $false } }
            default { return [pscustomobject]@{ AccountDisabled = $null } }
        }
    }
    [pscustomobject]@{ AccountDisabled = $true }
}
function Get-MessageTraceV2 {
    Write-StubRead 'Get-MessageTraceV2'
    [pscustomobject]@{
        Received = [datetime]::UtcNow.AddDays(-1); SenderAddress = 'alex@contoso.example'; RecipientAddress = 'x@external.example'
        Subject = 'Quarterly report'; Status = 'Delivered'; FromIP = '203.0.113.10'; ToIP = '198.51.100.20'; Size = 20480
        MessageId = '<abc@contoso.example>'; MessageTraceId = [guid]::NewGuid()
    }
}
function Search-UnifiedAuditLog {
    Write-StubRead 'Search-UnifiedAuditLog'
    # Pages of a fixed size; an empty page means the session has no more records.
    $size = 1
    $pages = 1
    if ($env:BDIT_STUB_AUDIT_PAGE_SIZE) { $size = [int]$env:BDIT_STUB_AUDIT_PAGE_SIZE }
    if ($env:BDIT_STUB_AUDIT_PAGES) { $pages = [int]$env:BDIT_STUB_AUDIT_PAGES }
    $script:auditPages++
    if ($script:auditPages -gt $pages) { return }
    foreach ($index in 1..$size) {
        [pscustomobject]@{
            CreationDate = [datetime]::UtcNow.AddDays(-2); UserIds = 'alex@contoso.example'; Operations = 'MailItemsAccessed'
            RecordType = 'ExchangeItem'; AuditData = '{"Workload":"Exchange","ObjectId":"alex@contoso.example","ClientIP":"203.0.113.10"}'
        }
    }
}
Export-ModuleMember -Function *
'@

$work = Join-Path ([IO.Path]::GetTempPath()) ('bdit-library-' + [guid]::NewGuid().ToString('N'))
$moduleDirectory = Join-Path (Join-Path (Join-Path $work 'modules') 'ExchangeOnlineManagement') '99.0.0'
[void][IO.Directory]::CreateDirectory($moduleDirectory)

# Produces a Copy script with bdit exactly as the desktop page would, for the synthetic tenant.
function New-Copy([string]$Id, [string]$Name, [string[]]$Form = @(), [string]$Account = 'admin@contoso.example') {
    $path = Join-Path $work ($Name + '.ps1')
    $signIn = @()
    if ($Account) { $signIn = @('--account', $Account) }
    & dotnet $Bdit script --id $Id --copy --tenant $tenant --tenant-name 'Contoso (synthetic)' @signIn --out $path @Form | Out-Host
    if ($LASTEXITCODE -ne 0) { throw ($Id + ': bdit could not produce a Copy script.') }
    return $path
}

# Runs a Copy script in a child PowerShell against the stand-in module and reports what it did.
function Invoke-Copy([string]$Path, [string]$Name, [string]$Scenario = '', [string]$Upn = 'admin@contoso.example', [string]$Tenant = $tenant, [hashtable]$Environment = @{}) {
    $csv = Join-Path $work ($Name + '.csv')
    $reads = Join-Path $work ($Name + '.reads')
    $env:BDIT_STUB_TENANT = $Tenant
    $env:BDIT_STUB_UPN = $Upn
    $env:BDIT_STUB_SCENARIO = $Scenario
    $env:BDIT_STUB_DISCONNECT = Join-Path $work ($Name + '.disconnected')
    $env:BDIT_STUB_READS = $reads
    $env:BDIT_STUB_AUDIT_PAGE_SIZE = ''
    $env:BDIT_STUB_AUDIT_PAGES = ''
    foreach ($key in $Environment.Keys) { Set-Item -LiteralPath ('env:' + $key) -Value $Environment[$key] }
    # Windows PowerShell 5.1 turns a child's stderr into a terminating error under 'Stop'; the exit code is what is checked.
    $output = & { $ErrorActionPreference = 'Continue'; & $PowerShell -NoLogo -NoProfile -NonInteractive -File $Path -OutputCsv $csv 2>&1 | Out-String }
    $code = $LASTEXITCODE
    $rows = @()
    if (Test-Path -LiteralPath $csv) { $rows = @(Import-Csv -LiteralPath $csv) }
    $readList = @()
    if (Test-Path -LiteralPath $reads) { $readList = @(Get-Content -LiteralPath $reads | Where-Object { $_ }) }
    [pscustomobject]@{
        # Whitespace is removed because Windows PowerShell 5.1 may wrap a child's error or warning text at any column.
        Code = $code; Output = ([string]$output -replace '\s', ''); Csv = $csv; CsvWritten = (Test-Path -LiteralPath $csv); Rows = $rows
        Disconnected = (Test-Path -LiteralPath $env:BDIT_STUB_DISCONNECT); Reads = $readList
    }
}

# A missing column reads as $null, so a check against an older output shape fails rather than stopping the run.
function Get-Cell([object]$Row, [string]$Column) {
    if ($null -ne $Row -and $Row.PSObject.Properties[$Column]) { return [string]$Row.$Column }
    return $null
}

function Find-Row([object[]]$Rows, [string]$Column, [string]$Value) {
    return @($Rows | Where-Object { (Get-Cell $_ $Column) -eq $Value })
}

$failures = [System.Collections.Generic.List[string]]::new()
function Test-Case([string]$Case, [scriptblock]$Check) {
    $passed = $false
    try { $passed = [bool](& $Check) } catch { $passed = $false; $Case = $Case + ' (' + $_.Exception.Message + ')' }
    if ($passed) { Write-Output ('  pass: ' + $Case) } else { $failures.Add($Case); Write-Output ('  FAIL: ' + $Case) }
}

try {
    [IO.File]::WriteAllText((Join-Path $moduleDirectory 'ExchangeOnlineManagement.psm1'), $stub)
    [IO.File]::WriteAllText((Join-Path $moduleDirectory 'ExchangeOnlineManagement.psd1'),
        "@{ ModuleVersion = '99.0.0'; RootModule = 'ExchangeOnlineManagement.psm1'; GUID = '$([guid]::NewGuid())'; FunctionsToExport = '*' }")
    $modulePath = Join-Path $work 'modules'
    $env:PSModulePath = $modulePath + [IO.Path]::PathSeparator + $env:PSModulePath

    $manifests = @(Get-ChildItem -LiteralPath $Library -Recurse -Filter '*.json' | Where-Object { $_.Name -ne 'registry.json' })
    $copies = @()
    $copyById = @{}
    foreach ($manifestFile in $manifests) {
        $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
        if (-not $samples.ContainsKey($manifest.id)) { throw ($manifest.id + ' has no sample form in this test.') }
        $copy = New-Copy $manifest.id $manifest.id @($samples[$manifest.id])
        $copies += $copy
        $copyById[$manifest.id] = $copy

        $run = Invoke-Copy $copy $manifest.id
        if ($run.Code -ne 0) { throw ($manifest.id + ': the Copy script failed against the synthetic module.' + [Environment]::NewLine + $run.Output) }
        if (-not $run.Disconnected) { throw ($manifest.id + ': the Copy script did not disconnect.') }
        $header = ((Get-Content -LiteralPath $run.Csv -TotalCount 1) -replace '"', '') -split ','
        if (($header -join ',') -cne (@($manifest.outputSchema.columns) -join ',')) {
            throw ($manifest.id + ': CSV columns ' + ($header -join ',') + ' differ from the manifest.')
        }
        if ($run.Rows.Count -lt 1) { throw ($manifest.id + ': no synthetic rows were written.') }
        Write-Output ($manifest.id + ': ' + $run.Rows.Count + ' synthetic row(s), manifest columns, disconnected.')

        # Signed in to another tenant: the wrapper must refuse before reading, still disconnect, and write nothing.
        $wrong = Invoke-Copy $copy ($manifest.id + '-wrong') -Tenant '9b2c1d4e-0000-4000-8000-000000000000'
        if ($wrong.Code -eq 0 -or $wrong.CsvWritten) { throw ($manifest.id + ': a different tenant was not refused.') }
        if (-not $wrong.Disconnected) { throw ($manifest.id + ': the refused run did not disconnect.') }
        if ($wrong.Reads.Count -ne 0) { throw ($manifest.id + ': the refused run read ' + ($wrong.Reads -join ', ') + '.') }
    }
    $check = Join-Path $PSScriptRoot 'Test-ScriptLibrary.ps1'
    & $check -Library $Library -CopyScript $copies

    # Regressions for Astra's review of PR #46 (AST-20261008-05 to -09). Each case is recorded, then all failures are reported together.
    Write-Output 'Signed-in account (AST-20261008-07):'
    foreach ($id in @($copyById.Keys | Sort-Object)) {
        $other = Invoke-Copy $copyById[$id] ($id + '-account') -Upn 'other@contoso.example'
        Test-Case ($id + ': another account in the same tenant is refused, nothing read or written, disconnected') {
            $other.Code -ne 0 -and -not $other.CsvWritten -and $other.Reads.Count -eq 0 -and $other.Disconnected -and
                $other.Output.Contains('other@contoso.example') -and $other.Output.Contains('Nothingwasread')
        }
    }
    $upperCase = Invoke-Copy $copyById['exo.mailbox-inventory'] 'account-case' -Upn 'ADMIN@Contoso.Example'
    Test-Case 'the reviewed account matches regardless of letter case' { $upperCase.Code -eq 0 -and $upperCase.Rows.Count -ge 1 }
    $anyCopy = New-Copy 'exo.mailbox-inventory' 'any-account' -Account ''
    $any = Invoke-Copy $anyCopy 'any-account' -Upn 'other@contoso.example'
    Test-Case 'with no reviewed account, the account chosen at the prompt is used' { $any.Code -eq 0 -and $any.Rows.Count -ge 1 }

    Write-Output 'Quota audit keeps what it cannot measure (AST-20261008-05):'
    $quotaDefault = New-Copy 'exo.quota-audit' 'quota-default'
    $quotaAll = New-Copy 'exo.quota-audit' 'quota-all' @('--IncludeAll', 'true')
    foreach ($pair in @(@($quotaDefault, 'default inputs'), @($quotaAll, 'IncludeAll'))) {
        $run = Invoke-Copy $pair[0] ('quota-' + $pair[1].Replace(' ', '-')) -Scenario 'quota-unknown'
        $label = $pair[1]
        Test-Case ('quota (' + $label + '): Unlimited quota listed as Unknown with a reason') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'unlimited@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and (Get-Cell $row[0] 'Reason') -like '*Unlimited*'
        }
        Test-Case ('quota (' + $label + '): malformed quota listed as Unknown with a reason') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'malformed@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and (Get-Cell $row[0] 'Reason') -like '*quota*'
        }
        Test-Case ('quota (' + $label + '): missing size listed as Unknown with a reason') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'nosize@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Unknown' -and (Get-Cell $row[0] 'Reason') -like '*size*'
        }
        Test-Case ('quota (' + $label + '): a measured mailbox over the threshold is still listed, and the run says some were not measured') {
            $row = @(Find-Row $run.Rows 'PrimarySmtpAddress' 'over@contoso.example')
            $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'AtOrAboveThreshold' -and $run.Output.Contains('BDIT:UNKNOWN')
        }
    }

    Write-Output 'True, false and not returned stay distinct (AST-20261008-06):'
    $flagForms = @{
        'exo.mailbox-inventory' = @()
        'exo.shared-mailboxes' = @()
        'exo.external-forwarding' = @('--IncludeInboxRules', 'true')
        'exo.inbox-rules' = @('--Mailbox', 'null@contoso.example')
        'exo.mailbox-permissions' = @('--IncludeInherited', 'true')
        'exo.user-mailbox-access' = @('--User', 'alex@contoso.example')
    }
    $flags = @{}
    foreach ($id in $flagForms.Keys) { $flags[$id] = Invoke-Copy (New-Copy $id ($id + '-flags') $flagForms[$id]) ($id + '-flags') -Scenario 'flags' }
    foreach ($expect in @(@('true@contoso.example', 'True'), @('false@contoso.example', 'False'), @('null@contoso.example', 'Unknown'))) {
        $address = $expect[0]; $value = $expect[1]
        Test-Case ('inventory: litigation hold ' + $value + ' for ' + $address) {
            $row = @(Find-Row $flags['exo.mailbox-inventory'].Rows 'PrimarySmtpAddress' $address)
            $row.Count -eq 1 -and (Get-Cell $row[0] 'LitigationHoldEnabled') -ceq $value -and (((Get-Cell $row[0] 'Notes') -ne '') -eq ($value -eq 'Unknown'))
        }
        Test-Case ('shared: sign-in blocked, hidden and hold ' + $value + ' for ' + $address) {
            $row = @(Find-Row $flags['exo.shared-mailboxes'].Rows 'PrimarySmtpAddress' $address)
            $row.Count -eq 1 -and (Get-Cell $row[0] 'SignInBlocked') -ceq $value -and (Get-Cell $row[0] 'HiddenFromAddressLists') -ceq $value -and
                (Get-Cell $row[0] 'LitigationHoldEnabled') -ceq $value -and (((Get-Cell $row[0] 'Notes') -ne '') -eq ($value -eq 'Unknown'))
        }
        Test-Case ('forwarding: keeps a copy ' + $value + ' for ' + $address) {
            $row = @(Find-Row $flags['exo.external-forwarding'].Rows 'Mailbox' $address | Where-Object { (Get-Cell $_ 'Source') -eq 'MailboxForwarding' })
            $row.Count -eq 1 -and (Get-Cell $row[0] 'KeepsCopy') -ceq $value
        }
    }
    Test-Case 'forwarding: an inbox rule whose delete action was not returned does not claim a copy is kept' {
        $rows = @($flags['exo.external-forwarding'].Rows | Where-Object { (Get-Cell $_ 'Source') -eq 'InboxRule' })
        $rows.Count -ge 1 -and @($rows | Where-Object { (Get-Cell $_ 'KeepsCopy') -cne 'Unknown' }).Count -eq 0
    }
    Test-Case 'inbox rules: flags that were not returned are Unknown, not False' {
        $row = @($flags['exo.inbox-rules'].Rows)
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Enabled') -ceq 'Unknown' -and (Get-Cell $row[0] 'DeleteMessage') -ceq 'Unknown' -and
            (Get-Cell $row[0] 'MarkAsRead') -ceq 'Unknown' -and (Get-Cell $row[0] 'StopProcessingRules') -ceq 'Unknown'
    }
    Test-Case 'mailbox permissions: inheritance and deny that were not returned are Unknown' {
        $rows = @($flags['exo.mailbox-permissions'].Rows | Where-Object { (Get-Cell $_ 'Permission') -ne 'SendOnBehalf' })
        $rows.Count -ge 2 -and @($rows | Where-Object { (Get-Cell $_ 'IsInherited') -cne 'Unknown' -or (Get-Cell $_ 'Deny') -cne 'Unknown' }).Count -eq 0
    }
    Test-Case 'user mailbox access: inheritance that was not returned is Unknown and the entry is not called granted' {
        $rows = @($flags['exo.user-mailbox-access'].Rows)
        $rows.Count -ge 1 -and @($rows | Where-Object { (Get-Cell $_ 'IsInherited') -cne 'Unknown' -or (Get-Cell $_ 'Status') -eq 'Granted' }).Count -eq 0
    }
    Test-Case 'the default run keeps known values as True and False' {
        $row = @(Find-Row (Invoke-Copy $copyById['exo.shared-mailboxes'] 'shared-known').Rows 'PrimarySmtpAddress' 'reception@contoso.example')
        $row.Count -eq 1 -and (Get-Cell $row[0] 'HiddenFromAddressLists') -ceq 'True' -and (Get-Cell $row[0] 'Notes') -eq ''
    }

    Write-Output 'A user''s mailbox access uses exact identities and keeps deny entries (AST-20261008-08):'
    $access = Invoke-Copy $copyById['exo.user-mailbox-access'] 'access' -Scenario 'access'
    Test-Case 'access: an exact identity with an allow entry is Granted' {
        $row = @(Find-Row $access.Rows 'Mailbox' 'exact@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'FullAccess' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Granted'
    }
    Test-Case 'access: another recipient with the same display name is never Granted to this user' {
        $rows = @(Find-Row $access.Rows 'Mailbox' 'namesake@contoso.example')
        @($rows | Where-Object { (Get-Cell $_ 'Status') -ne 'Unresolved' }).Count -eq 0 -and
            @($rows | Where-Object { (Get-Cell $_ 'Holder') -eq 'alex.other@contoso.example' }).Count -eq 0
    }
    Test-Case 'access: an entry naming only the display name is kept as Unresolved' {
        $rows = @(Find-Row $access.Rows 'Mailbox' 'namesake@contoso.example' | Where-Object { (Get-Cell $_ 'Holder') -eq 'Alex Wilber' })
        $rows.Count -eq 1 -and (Get-Cell $rows[0] 'Status') -eq 'Unresolved' -and (Get-Cell $rows[0] 'Notes') -ne ''
    }
    Test-Case 'access: a display-name Send on Behalf delegate is Unresolved, an exact one Granted' {
        $name = @(Find-Row $access.Rows 'Mailbox' 'exact@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'SendOnBehalf' })
        $exact = @(Find-Row $access.Rows 'Mailbox' 'denied@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'SendOnBehalf' })
        $name.Count -eq 1 -and (Get-Cell $name[0] 'Status') -eq 'Unresolved' -and $exact.Count -eq 1 -and (Get-Cell $exact[0] 'Status') -eq 'Granted'
    }
    Test-Case 'access: a deny Full Access entry is kept as Denied' {
        $row = @(Find-Row $access.Rows 'Mailbox' 'denied@contoso.example' | Where-Object { (Get-Cell $_ 'Permission') -eq 'FullAccess' })
        $row.Count -eq 1 -and (Get-Cell $row[0] 'Status') -eq 'Denied'
    }
    Test-Case 'access: a deny Send As entry is kept as Denied, an allow one Granted' {
        $deny = @(Find-Row $access.Rows 'Mailbox' 'finance@contoso.example')
        $allow = @(Find-Row $access.Rows 'Mailbox' 'reception@contoso.example')
        $deny.Count -eq 1 -and (Get-Cell $deny[0] 'Status') -eq 'Denied' -and $allow.Count -eq 1 -and (Get-Cell $allow[0] 'Status') -eq 'Granted'
    }

    Write-Output 'A stop at a limit is never presented as complete (AST-20261008-09):'
    $auditCopy = New-Copy 'exo.unified-audit-search' 'audit-cap' @('--StartDate', $from, '--EndDate', $to, '--UserIds', 'alex@contoso.example', '--MaxRows', '3')
    $atCap = Invoke-Copy $auditCopy 'audit-more' -Environment @{ BDIT_STUB_AUDIT_PAGE_SIZE = '3'; BDIT_STUB_AUDIT_PAGES = '2' }
    Test-Case 'audit: a page ending exactly at the limit with another page waiting is marked partial' {
        $atCap.Code -eq 0 -and $atCap.Rows.Count -eq 3 -and $atCap.Output.Contains('BDIT:PARTIAL')
    }
    $exact = Invoke-Copy $auditCopy 'audit-exact' -Environment @{ BDIT_STUB_AUDIT_PAGE_SIZE = '3'; BDIT_STUB_AUDIT_PAGES = '1' }
    Test-Case 'audit: exactly the limit with nothing more is complete, proved by an empty next page' {
        $exact.Code -eq 0 -and $exact.Rows.Count -eq 3 -and -not $exact.Output.Contains('BDIT:PARTIAL')
    }
    $midPage = Invoke-Copy $auditCopy 'audit-mid' -Environment @{ BDIT_STUB_AUDIT_PAGE_SIZE = '5'; BDIT_STUB_AUDIT_PAGES = '1' }
    Test-Case 'audit: a stop inside a page is marked partial' { $midPage.Code -eq 0 -and $midPage.Rows.Count -eq 3 -and $midPage.Output.Contains('BDIT:PARTIAL') }
    $saturated = Invoke-Copy $copyById['exo.user-mailbox-access'] 'sendas-saturated' -Scenario 'sendas-saturated'
    Test-Case 'access: a Send As result at the request limit is marked partial' {
        $saturated.Code -eq 0 -and $saturated.Output.Contains('BDIT:PARTIAL') -and @(Find-Row $saturated.Rows 'Permission' 'SendAs').Count -le 5000
    }

    # The last child run may have been an expected refusal; do not leave its exit code for the calling step to report.
    $global:LASTEXITCODE = 0
    if ($failures.Count -ne 0) { throw ($failures.Count.ToString() + ' synthetic regression case(s) failed: ' + ($failures -join '; ')) }
    Write-Output ('Synthetic run passed for ' + $manifests.Count + ' item(s); a different tenant and a different account were refused each time with nothing read. No Microsoft module or tenant was used.')
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
