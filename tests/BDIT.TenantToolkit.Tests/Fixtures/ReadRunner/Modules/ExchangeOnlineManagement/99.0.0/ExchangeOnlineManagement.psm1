# Synthetic stand-in for ExchangeOnlineManagement, used only by the runner regressions. It never signs in, never contacts
# a service and holds no credential. BDIT_FAKE_SCENARIO picks a behaviour; every call is appended to BDIT_FAKE_LOG so a
# test can prove which reads happened and that no write was reached.
$script:Connected = $false
$script:Account = $null
$script:Reads = 0
$script:Scenario = [string]$env:BDIT_FAKE_SCENARIO
$script:Tenant = [string]$env:BDIT_FAKE_TENANT

function Write-FakeLog([string]$Text) { if ($env:BDIT_FAKE_LOG) { [IO.File]::AppendAllText($env:BDIT_FAKE_LOG, $Text + "`n") } }

function Connect-ExchangeOnline {
    param([string]$UserPrincipalName, [string[]]$CommandName, [switch]$ShowBanner)
    Write-FakeLog ('Connect ' + $UserPrincipalName + ' ' + ($CommandName -join ','))
    if ($script:Scenario -eq 'connect-fails') { throw 'Synthetic sign-in failure.' }
    $script:Connected = $true
    $script:Account = if ($script:Scenario -eq 'wrong-account') { 'someone.else@synthetic.example' } else { $UserPrincipalName }
    if ($script:Scenario -eq 'wrong-tenant') { $script:Tenant = '22222222-2222-4222-8222-222222222222' }
}

function Disconnect-ExchangeOnline {
    param([switch]$Confirm)
    Write-FakeLog 'Disconnect'
    $script:Connected = $false
}

# The token fields Get-ConnectionInformation documents. By default the token is Active and expires in an hour, as a
# DateTimeOffset. BDIT_FAKE_TOKEN picks another shape: expired, missing, null, malformed, another type or a string form.
function Add-FakeToken($Connection) {
    $now = [DateTimeOffset]::UtcNow
    $status = 'Active'
    $expiry = $now.AddHours(1)
    switch ([string]$env:BDIT_FAKE_TOKEN) {
        'expired-active' { $expiry = $now.AddHours(-1) }
        'expired-status' { $status = 'Expired' }
        'missing-status' { $status = '<absent>' }
        'null-status' { $status = $null }
        'missing-expiry' { $expiry = '<absent>' }
        'null-expiry' { $expiry = $null }
        'malformed-expiry' { $expiry = 'not a date' }
        'number-expiry' { $expiry = 4102444800 }
        'ambiguous-string' { $expiry = $now.AddDays(40).UtcDateTime.ToString('dd/MM/yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture) }
        'offset-string-past' { $expiry = $now.AddMinutes(-10).ToOffset([TimeSpan]::FromHours(14)).ToString('yyyy-MM-ddTHH:mm:sszzz', [Globalization.CultureInfo]::InvariantCulture) }
        'offset-string-future' { $expiry = $now.AddMinutes(30).ToOffset([TimeSpan]::FromHours(-11)).ToString('yyyy-MM-ddTHH:mm:sszzz', [Globalization.CultureInfo]::InvariantCulture) }
        'utc-string-future' { $expiry = $now.AddMinutes(30).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture) }
        'utc-datetime-future' { $expiry = $now.AddMinutes(30).UtcDateTime }
        'unspecified-datetime-past' { $expiry = [DateTime]::SpecifyKind($now.AddMinutes(-30).UtcDateTime, [DateTimeKind]::Unspecified) }
        'unspecified-datetime-future' { $expiry = [DateTime]::SpecifyKind($now.AddMinutes(30).UtcDateTime, [DateTimeKind]::Unspecified) }
        'expires-after-first-read' { if ($script:Reads -ge 1) { $expiry = $now.AddSeconds(-1) } }
        'expires-after-last-read' { if ($script:Reads -ge 3) { $expiry = $now.AddSeconds(-1) } }
    }
    if ($status -ne '<absent>') { $Connection | Add-Member -NotePropertyName TokenStatus -NotePropertyValue $status }
    if (-not ($expiry -is [string] -and $expiry -eq '<absent>')) { $Connection | Add-Member -NotePropertyName TokenExpiryTimeUTC -NotePropertyValue $expiry }
}

function Get-ConnectionInformation {
    if (-not $script:Connected) { return }
    $account = $script:Account
    # Changes only after the last of the three reads, so only the gate's check after a read can notice it.
    if ($script:Scenario -eq 'account-changes' -and $script:Reads -ge 3) { $account = 'someone.else@synthetic.example' }
    $connection = [pscustomobject]@{ State = 'Connected'; IsEopSession = $false; TenantID = $script:Tenant; UserPrincipalName = $account }
    Add-FakeToken $connection
    if ($script:Scenario -eq 'two-connections') { return @($connection, $connection) }
    return $connection
}

function Get-EXOMailbox {
    param([string]$ResultSize, [string[]]$Properties, [string]$RecipientTypeDetails)
    $script:Reads++
    Write-FakeLog 'Read Get-EXOMailbox'
    if ($script:Scenario -eq 'chatty') { [Console]::Out.Write(('x' * 1100000)); [Console]::Out.Flush() }
    if ($script:Scenario -eq 'noisy-errors') { 1..3000 | ForEach-Object { [Console]::Error.WriteLine(('e' * 200)) } }
    if ($script:Scenario -eq 'hang') { Start-Sleep -Seconds 120 }
    return @(
        [pscustomobject]@{ ExchangeGuid = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'; DisplayName = 'Synthetic user'; ExternalDirectoryObjectId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc'; PrimarySmtpAddress = 'user@synthetic.example'; RecipientTypeDetails = 'UserMailbox'; IssueWarningQuota = '49 GB (52,613,349,376 bytes)'; ProhibitSendQuota = '49.5 GB (53,150,220,288 bytes)'; ProhibitSendReceiveQuota = '50 GB (53,687,091,200 bytes)' },
        [pscustomobject]@{ ExchangeGuid = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'; DisplayName = 'Synthetic shared'; ExternalDirectoryObjectId = ''; PrimarySmtpAddress = 'shared@synthetic.example'; RecipientTypeDetails = 'SharedMailbox'; IssueWarningQuota = 'Unlimited'; ProhibitSendQuota = 'Unlimited'; ProhibitSendReceiveQuota = 'Unlimited' }
    )
}

function Get-EXOMailboxStatistics {
    param([string]$Identity)
    $script:Reads++
    Write-FakeLog ('Read Get-EXOMailboxStatistics ' + $Identity)
    if ($script:Scenario -eq 'stats-fail' -and $Identity -like 'bbbbbbbb*') { throw 'Synthetic statistics failure.' }
    # A slow first statistics read. With BDIT_FAKE_COOPERATE it ends as soon as the runner's stop file appears, as a read
    # that returns promptly would; without it, it outlasts the stop grace period, as a hung module call would.
    if ($script:Scenario -eq 'slow-stats' -and $Identity -like 'aaaaaaaa*') {
        Write-FakeLog 'Slow read started'
        for ($i = 0; $i -lt 300; $i++) {
            if ($env:BDIT_FAKE_COOPERATE -and @(Get-ChildItem -LiteralPath $env:BDIT_FAKE_STAGING -Recurse -Filter 'stop-*.flag').Count -gt 0) { break }
            Start-Sleep -Milliseconds 100
        }
    }
    $size = if ($Identity -like 'aaaaaaaa*') { '1.2 GB (1,288,490,189 bytes)' } else { '0 B (0 bytes)' }
    return [pscustomobject]@{ TotalItemSize = $size }
}

# Present only to prove a body cannot reach a write: the gate never registers it and the body check refuses it.
function Set-Mailbox { param([string]$Identity) Write-FakeLog ('WRITE Set-Mailbox ' + $Identity) }

Export-ModuleMember -Function Connect-ExchangeOnline, Disconnect-ExchangeOnline, Get-ConnectionInformation, Get-EXOMailbox, Get-EXOMailboxStatistics, Set-Mailbox
