#requires -Version 5.1
<# Offline only: invokes the owned identity-check function against a local connection stub. No module/sign-in/read. #>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ScriptPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $ScriptPath).Path, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'The generated read template did not parse.' }
$helpers = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Assert-CaptureConnection' }, $true))
if ($helpers.Count -ne 1) { throw 'Expected one owned connection-check function.' }
# Only this function is defined, never the template's module import, authentication or collection blocks.
. ([scriptblock]::Create($helpers[0].Extent.Text))
$expectedTenant = [guid]'11111111-1111-4111-8111-111111111111'
$UserPrincipalName = 'engineer@example.invalid'
$script:connections = @()
$script:stubCalls = 0
function Get-ConnectionInformation { $script:stubCalls++; return $script:connections }
function Connection([string]$Account = 'engineer@example.invalid', [bool]$Purview = $false) {
    return [pscustomobject]@{ TenantID = $expectedTenant.ToString(); State = 'Connected'; IsEopSession = $Purview; UserPrincipalName = $Account }
}
$failures = [Collections.Generic.List[string]]::new()
$checks = 0
function Refuses([string]$Name, [object[]]$Values, [bool]$Purview = $false) {
    $script:connections = $Values
    $refused = $false
    try { $null = Assert-CaptureConnection -Purview $Purview } catch { $refused = $true }
    if (-not $refused) { $failures.Add($Name) }
    $script:checks++
}
foreach ($purview in @($false, $true)) {
    $script:connections = @(Connection 'ENGINEER@example.invalid' $purview)
    $result = Assert-CaptureConnection -Purview $purview
    if ($result -ne $expectedTenant.ToString()) { throw 'Matching tenant/account must return the observed tenant.' }
    $checks++
    Refuses "wrong account (Purview=$purview)" @(Connection 'another@example.invalid' $purview) $purview
    Refuses "unnamed account (Purview=$purview)" @(Connection '' $purview) $purview
    $missing = Connection 'engineer@example.invalid' $purview
    $missing.PSObject.Properties.Remove('UserPrincipalName')
    Refuses "missing account property (Purview=$purview)" @($missing) $purview
    $wrongTenant = Connection 'engineer@example.invalid' $purview; $wrongTenant.TenantID = '22222222-2222-4222-8222-222222222222'
    Refuses "wrong tenant (Purview=$purview)" @($wrongTenant) $purview
    Refuses "ambiguous connections (Purview=$purview)" @((Connection 'engineer@example.invalid' $purview), (Connection 'engineer@example.invalid' $purview)) $purview
    Refuses "no connected resource (Purview=$purview)" @() $purview
}
# Rechecking before a later collection must notice a session/account change; no implicit re-authentication/retry.
$script:connections = @(Connection)
$null = Assert-CaptureConnection -Purview $false; $checks++
Refuses 'account changed between reads' @(Connection 'another@example.invalid')
# Manual export with no supplied account keeps its historical tenant-only boundary explicitly.
$UserPrincipalName = ''
$script:connections = @(Connection '')
$null = Assert-CaptureConnection -Purview $false; $checks++
if ($failures.Count -gt 0) { throw ('Account-check regressions: ' + ($failures -join '; ')) }
Write-Output ('::notice title=Exchange read identity::' + $checks + ' synthetic connection checks passed; local stub calls=' + $script:stubCalls + '; no Microsoft module, sign-in or tenant read.')
