#requires -Version 5.1
<#
Parse only. Never runs a library script, never loads Exchange modules and never connects to a tenant.

Checks every script in the library against its manifest: it parses, uses only allow-listed read commands or its own
functions, has no dynamic invocation, declares exactly the manifest's parameters and takes no credential or policy
parameter. With -CopyScript it checks generated Copy scripts too: every form value must be a constant literal, the
reviewed body must appear unchanged, and the wrapper may use only the fixed sign-in, tenant check and CSV commands.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Library,
    [string[]]$CopyScript = @()
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Language = 'System.Management.Automation.Language'

$readCommands = @(
    'Get-EXOMailbox', 'Get-EXOMailboxStatistics', 'Get-EXOMailboxPermission', 'Get-EXORecipientPermission', 'Get-EXORecipient',
    'Get-EXOMailboxFolderStatistics', 'Get-EXOMailboxFolderPermission', 'Get-InboxRule', 'Get-AcceptedDomain', 'Get-User',
    'Get-MessageTraceV2', 'Search-UnifiedAuditLog',
    'Set-StrictMode', 'Where-Object', 'ForEach-Object', 'Select-Object', 'Sort-Object', 'Measure-Object', 'Group-Object',
    'ConvertFrom-Json', 'Write-Warning'
)
$wrapperCommands = @(
    'Connect-ExchangeOnline', 'Get-ConnectionInformation', 'Disconnect-ExchangeOnline', 'Where-Object', 'Write-Host',
    'Format-Table', 'Out-String', 'Join-Path', 'Get-Date', 'Select-Object', 'Export-Csv', 'Set-StrictMode'
)
$forbiddenParameters = @('ExecutionPolicy', 'Credential', 'AccessToken', 'Certificate', 'CertificatePassword', 'CertificateThumbprint', 'AppId')

function Read-Ast([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -ne 0) { throw ((Split-Path -Leaf $Path) + ' has syntax errors: ' + ($errors.Message -join '; ')) }
    return $ast
}

function Find-Nodes($Ast, [string]$Type) {
    $full = "$Language.$Type"
    return @($Ast.FindAll({ param($node) $node.GetType().FullName -eq $full }, $true))
}

function Assert-Commands($Ast, [string[]]$Allowed, [string]$Name, [scriptblock]$AllowDynamic) {
    $functions = @(Find-Nodes $Ast 'FunctionDefinitionAst' | ForEach-Object { $_.Name })
    foreach ($command in (Find-Nodes $Ast 'CommandAst')) {
        $commandName = $command.GetCommandName()
        if ($null -eq $commandName) {
            if (-not (& $AllowDynamic $command)) { throw ($Name + ' has a dynamic invocation: ' + $command.Extent.Text) }
        } elseif ($commandName -notin $Allowed -and $commandName -notin $functions) {
            throw ($Name + ' uses an unsupported command: ' + $commandName)
        }
        foreach ($element in $command.CommandElements) {
            if ($element -is [System.Management.Automation.Language.CommandParameterAst] -and $element.ParameterName -in $forbiddenParameters) {
                throw ($Name + ' passes an unsupported authentication or policy parameter: -' + $element.ParameterName)
            }
        }
    }
    foreach ($member in (Find-Nodes $Ast 'InvokeMemberExpressionAst')) {
        if ($member.Member.Extent.Text -in @('Invoke', 'InvokeReturnAsIs', 'Create', 'InvokeScript', 'NewScriptBlock')) {
            throw ($Name + ' invokes code dynamically: ' + $member.Extent.Text)
        }
    }
}

function Test-Constant($Node) {
    if ($Node -is [System.Management.Automation.Language.StringConstantExpressionAst]) { return $Node.StringConstantType -eq 'SingleQuoted' }
    if ($Node -is [System.Management.Automation.Language.ConstantExpressionAst]) { return $true }
    if ($Node -is [System.Management.Automation.Language.VariableExpressionAst]) { return $Node.VariablePath.UserPath -in @('true', 'false') }
    if ($Node -is [System.Management.Automation.Language.ArrayExpressionAst]) {
        $statements = @($Node.SubExpression.Statements)
        if ($statements.Count -ne 1) { return $false }
        $pipeline = $statements[0]
        if ($pipeline -isnot [System.Management.Automation.Language.PipelineAst] -or $pipeline.PipelineElements.Count -ne 1) { return $false }
        $expression = $pipeline.PipelineElements[0].Expression
        if ($expression -is [System.Management.Automation.Language.ArrayLiteralAst]) {
            foreach ($item in $expression.Elements) { if (-not (Test-Constant $item)) { return $false } }
            return $true
        }
        return Test-Constant $expression
    }
    return $false
}

$libraryRoot = (Resolve-Path -LiteralPath $Library).Path
$bodies = @{}
$checked = 0
foreach ($manifestFile in @(Get-ChildItem -LiteralPath $libraryRoot -Recurse -Filter '*.json' | Where-Object { $_.Name -ne 'registry.json' })) {
    $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
    $scriptFile = Join-Path $libraryRoot $manifest.scriptPath
    $name = $manifest.id
    $ast = Read-Ast $scriptFile
    if ($manifest.mode -ne 'readOnly') { throw ($name + ': only read-only items are checked by this release.') }
    Assert-Commands $ast $readCommands $name { param($command) $false }
    $declared = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath } | Sort-Object)
    $expected = @($manifest.parameters | ForEach-Object { $_.name } | Sort-Object)
    if (($declared -join ',') -cne ($expected -join ',')) {
        throw ($name + ' declares parameters ' + ($declared -join ', ') + ' but its manifest lists ' + ($expected -join ', ') + '.')
    }
    $bodies[$name] = ([IO.File]::ReadAllText($scriptFile)).Replace("`r`n", "`n").TrimEnd("`n")
    $checked++
}
if ($checked -eq 0) { throw 'No library scripts were found.' }
Write-Output ('Library parsed: ' + $checked + ' read-only script(s) use only allow-listed commands and their manifest parameters.')

foreach ($copy in $CopyScript) {
    $name = Split-Path -Leaf $copy
    $ast = Read-Ast $copy
    $text = ([IO.File]::ReadAllText($copy)).Replace("`r`n", "`n")
    $owners = @($bodies.Keys | Where-Object { $text.Contains($bodies[$_]) })
    if ($owners.Count -ne 1) { throw ($name + ' does not contain exactly one unchanged library body.') }
    $allowed = $wrapperCommands + $readCommands
    Assert-Commands $ast $allowed $name {
        param($command)
        # The only dynamic call is the wrapper invoking the reviewed body held in $library.
        $first = $command.CommandElements[0]
        $command.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Ampersand -and
            $first -is [System.Management.Automation.Language.VariableExpressionAst] -and $first.VariablePath.UserPath -eq 'library'
    }
    $assignments = @(Find-Nodes $ast 'AssignmentStatementAst' | Where-Object { $_.Left.Extent.Text -eq '$arguments' })
    if ($assignments.Count -ne 1) { throw ($name + ' must assign the form values once.') }
    $table = $assignments[0].Right.Expression
    if ($table -isnot [System.Management.Automation.Language.HashtableAst]) { throw ($name + ': form values must be a literal table.') }
    foreach ($pair in $table.KeyValuePairs) {
        $element = $pair.Item2.PipelineElements[0]
        if ($element -isnot [System.Management.Automation.Language.CommandExpressionAst] -or -not (Test-Constant $element.Expression)) { throw ($name + ': the value of ' + $pair.Item1.Extent.Text + ' is not a constant literal.') }
    }
    Write-Output ($name + ': ' + $table.KeyValuePairs.Count + ' constant value(s); body of ' + $owners[0] + ' unchanged.')
}
