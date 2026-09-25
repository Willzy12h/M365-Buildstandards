#requires -Version 5.1
<#
.SYNOPSIS
Read-only, delegated Exchange/Purview configuration export for offline assessment.
.DESCRIPTION
Run manually in a fresh PowerShell process with an already installed supported
ExchangeOnlineManagement module (3.2.0 or later) and a read-only RBAC account.
The toolkit never runs this script. No installation, policy bypass, certificates,
credentials, tokens, mailbox contents or write cmdlets are included.
Runtime compatibility and actual responses remain unverified until engineer testing.
.EXAMPLE
./Read-ExchangeCapture.ps1 -OutputFile ./exchange-capture.json -IncludePurview
.LINK
https://learn.microsoft.com/powershell/exchange/exchange-online-powershell-v2
.LINK
https://learn.microsoft.com/powershell/module/exchangepowershell/get-connectioninformation
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [switch]$IncludePurview
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedTenant = [guid]'__TENANT__'
$domain = '__DOMAIN__'
if (Test-Path -LiteralPath $OutputFile) { throw 'Use a new output filename; existing evidence is never overwritten.' }
Import-Module ExchangeOnlineManagement -MinimumVersion 3.2.0 -ErrorAction Stop
if (@(Get-ConnectionInformation).Count -ne 0) { throw 'Use a fresh PowerShell process with no existing Exchange or Purview connection.' }
$definitions = @'
__DEFINITIONS__
'@ | ConvertFrom-Json
$capture = [ordered]@{
    schemaVersion = 1; id = [guid]::NewGuid().ToString(); tenantId = $expectedTenant.ToString()
    exchangeTenantId = ''; purviewTenantId = $null; delegated = $true
    domain = $domain; capturedAt = [DateTimeOffset]::UtcNow.ToString('O')
    moduleVersion = (Get-Module ExchangeOnlineManagement).Version.ToString()
    source = '__SOURCE__'; collections = [ordered]@{}; dns = @()
}
foreach ($entry in $definitions.PSObject.Properties) {
    $capture.collections[$entry.Name] = [ordered]@{ status = 'Not attempted'; command = $entry.Value.command; items = @() }
}

function Assert-CaptureConnection {
    param([bool]$Purview)
    $connections = @(Get-ConnectionInformation | Where-Object { $_.State -eq 'Connected' -and $_.IsEopSession -eq $Purview })
    if ($connections.Count -ne 1 -or [guid]$connections[0].TenantID -ne $expectedTenant) {
        throw 'The current connection is ambiguous or belongs to another tenant. Nothing will be captured.'
    }
    return $connections[0].TenantID.ToString()
}

function Read-CaptureCollection {
    param([string]$Key, [scriptblock]$Read)
    $definition = $definitions.PSObject.Properties[$Key].Value
    try {
        $null = Assert-CaptureConnection -Purview $definition.purview
        $projected = @()
        foreach ($item in @(& $Read)) {
            $row = [ordered]@{}
            foreach ($field in $definition.fields.PSObject.Properties) {
                $property = $item.PSObject.Properties[$field.Name]
                # A missing property is preserved as null, never converted to false or an empty successful read.
                if ($null -eq $property) { $row[$field.Name] = $null; continue }
                $value = $property.Value
                switch ($field.Value) {
                    'Boolean' { if ($value -is [bool]) { $row[$field.Name] = $value } else { $row[$field.Name] = $null } }
                    'Number' { if ($null -ne $value) { $row[$field.Name] = [int]$value } else { $row[$field.Name] = $null } }
                    'TextList' { $row[$field.Name] = @($value | ForEach-Object { $_.ToString() }) }
                    default { if ($null -ne $value) { $row[$field.Name] = $value.ToString() } else { $row[$field.Name] = $null } }
                }
            }
            $projected += $row
            if ($projected.Count -gt 10000) { throw 'Collection exceeds the supported item limit.' }
        }
        $capture.collections[$Key] = [ordered]@{ status = 'Collected'; command = $definition.command; items = @($projected) }
    }
    catch {
        $capture.collections[$Key] = [ordered]@{ status = 'Error'; command = $definition.command; error = 'Read failed or was incomplete. Check RBAC, connection and module compatibility; absence cannot be inferred.'; items = @() }
        Write-Warning ('Collection failed: ' + $Key + '. Review the local PowerShell error; no error details or authentication data are exported.')
    }
}

try {
    $readCommands = @($definitions.PSObject.Properties | Where-Object { -not $_.Value.purview } | ForEach-Object { $_.Value.command })
    Connect-ExchangeOnline -CommandName $readCommands -ShowBanner:$false -ErrorAction Stop
    $capture.exchangeTenantId = Assert-CaptureConnection -Purview $false
    Read-CaptureCollection 'acceptedDomains' { Get-AcceptedDomain -ErrorAction Stop }
    Read-CaptureCollection 'auditConfig' { Get-AdminAuditLogConfig -ErrorAction Stop }
    Read-CaptureCollection 'transportRules' { Get-TransportRule -ResultSize Unlimited -ErrorAction Stop }
    Read-CaptureCollection 'presetEop' { Get-EOPProtectionPolicyRule -ErrorAction Stop }
    Read-CaptureCollection 'presetAtp' { Get-ATPProtectionPolicyRule -ErrorAction Stop }
    Read-CaptureCollection 'outboundPolicies' { Get-HostedOutboundSpamFilterPolicy -ErrorAction Stop }
    Read-CaptureCollection 'transportConfig' { Get-TransportConfig -ErrorAction Stop }
    Read-CaptureCollection 'externalTags' { Get-ExternalInOutlook -ErrorAction Stop }
    Read-CaptureCollection 'organisation' { Get-OrganizationConfig -ErrorAction Stop }
    Read-CaptureCollection 'dkim' { Get-DkimSigningConfig -ErrorAction Stop }
    if ($IncludePurview) {
        try {
            Connect-IPPSSession -CommandName Get-UnifiedAuditLogRetentionPolicy -ShowBanner:$false -ErrorAction Stop
            $capture.purviewTenantId = Assert-CaptureConnection -Purview $true
            Read-CaptureCollection 'auditRetention' { Get-UnifiedAuditLogRetentionPolicy -ErrorAction Stop }
        }
        catch {
            $capture.collections['auditRetention'] = [ordered]@{ status = 'Error'; command = 'Get-UnifiedAuditLogRetentionPolicy'; error = 'Purview connection or tenant verification failed; retention is unknown.'; items = @() }
        }
    }
    $json = $capture | ConvertTo-Json -Depth 16
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    if ($bytes.Length -gt 2097152) { throw 'The capture exceeds the supported 2 MiB import limit.' }
    $file = [IO.File]::Open([IO.Path]::GetFullPath($OutputFile), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $file.Write($bytes, 0, $bytes.Length); $file.Flush($true) } finally { $file.Dispose() }
    Write-Output ('Saved read-only observations to ' + $OutputFile + '. Import and review all unknown results; no tenant changes were made.')
}
finally { Disconnect-ExchangeOnline -Confirm:$false -ErrorAction Continue }
