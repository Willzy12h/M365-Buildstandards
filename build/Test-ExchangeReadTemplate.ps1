#requires -Version 5.1
<# Parse only. Never invokes the capture script or loads Exchange modules. #>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$ScriptPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path -LiteralPath $ScriptPath).Path, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw ('Exchange template syntax errors: ' + ($parseErrors.Message -join '; ')) }
$allowed = @(
    'Set-StrictMode', 'Test-Path', 'Get-Module', 'Import-Module', 'Get-ConnectionInformation',
    'Where-Object', 'Sort-Object', 'Select-Object', 'ForEach-Object', 'ConvertFrom-Json', 'ConvertTo-Json',
    'Write-Output', 'Write-Warning', 'Assert-CaptureConnection', 'Read-CaptureCollection',
    'Connect-ExchangeOnline', 'Connect-IPPSSession', 'Disconnect-ExchangeOnline',
    'Get-AcceptedDomain', 'Get-AdminAuditLogConfig', 'Get-UnifiedAuditLogRetentionPolicy', 'Get-TransportRule',
    'Get-EOPProtectionPolicyRule', 'Get-ATPProtectionPolicyRule', 'Get-HostedOutboundSpamFilterPolicy',
    'Get-TransportConfig', 'Get-ExternalInOutlook', 'Get-OrganizationConfig', 'Get-DkimSigningConfig'
)
$commands = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))
foreach ($command in $commands) {
    $name = $command.GetCommandName()
    if ($null -eq $name) {
        # The only dynamic invocation is the scriptblock parameter in the fixed read function.
        $first = $command.CommandElements[0]
        if ($command.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Ampersand -or
            $first -isnot [System.Management.Automation.Language.VariableExpressionAst] -or $first.VariablePath.UserPath -ne 'Read') {
            throw 'An unsupported dynamic invocation exists in the Exchange read template.'
        }
    } elseif ($name -notin $allowed) { throw ('Unsupported command in Exchange read template: ' + $name) }
    foreach ($element in $command.CommandElements) {
        if ($element -is [System.Management.Automation.Language.CommandParameterAst] -and
            $element.ParameterName -in @('ExecutionPolicy', 'Credential', 'AccessToken', 'Certificate', 'CertificatePassword', 'CertificateThumbprint')) {
            throw 'The read template contains an unsupported authentication or policy parameter.'
        }
    }
}
foreach ($parameter in $ast.ParamBlock.Parameters) {
    if ($parameter.Name.VariablePath.UserPath -notin @('OutputFile', 'IncludePurview', 'Integrated', 'UserPrincipalName')) {
        throw 'The read template accepts an unsupported input.'
    }
}
Write-Output ('Exchange template parsed; ' + $commands.Count + ' allow-listed local/read commands. No script execution or tenant operation.')
