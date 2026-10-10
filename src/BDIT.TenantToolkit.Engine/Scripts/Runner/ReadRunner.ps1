<#
INT-088 owned read-only runner wrapper. Engine-embedded, plain ASCII, pinned by ReadRunnerTemplate.Sha256 and never edited
at run time. The parent creates every file this wrapper reads or writes; the wrapper cannot choose another path or schema.

Standard output carries only fixed marker lines. The first executable statement writes BDIT:STARTED; without it the parent
treats the run as refused by the host. Rows are written only to the parent-named result file, as one scriptReadResult
schema 1 envelope, and only after the body finished and the connection was checked again.

The body is parsed before it runs and refused unless every command it names is on a fixed list. Registered Exchange reads
are reachable only through Use-BditRead, which lives in a private module with the module command identities captured
after sign-in. Before and after every read it checks for one connection to the expected tenant and account and polls
the parent's stop file. No credential, token, policy change, installation, retry or upload.
#>
param(
    [Parameter(Mandatory = $true)][string]$Request,
    [Parameter(Mandatory = $true)][string]$RequestSha256,
    [Parameter(Mandatory = $true)][string]$Body,
    [Parameter(Mandatory = $true)][string]$Result,
    [Parameter(Mandatory = $true)][string]$Stop
)
[Console]::Out.WriteLine('BDIT:STARTED')
[Console]::Out.Flush()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# -File runs this script in the global scope, which every module scope can see. All run state therefore lives inside
# Invoke-BditRunner, and the script-level parameters are removed before anything else runs, so the body's module scope
# reaches nothing of the wrapper's: not the paths, the request, the captured commands or the gate.
$BditEntry = @($Request, $RequestSha256, $Body, $Result, $Stop)
Remove-Variable -Name Request, RequestSha256, Body, Result, Stop

function Write-BditMarker([string]$Text) { [Console]::Out.WriteLine('BDIT:' + $Text); [Console]::Out.Flush() }
function Stop-BditRun([string]$Code, [int]$Exit = 2) { Write-BditMarker ('FAILED:' + $Code); exit $Exit }

function Get-BditSha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return (-join ($sha.ComputeHash($Bytes) | ForEach-Object { $_.ToString('x2') })) } finally { $sha.Dispose() }
}

function Read-BditBytes([string]$Path, [int]$Maximum) {
    $info = New-Object IO.FileInfo $Path
    if (-not $info.Exists -or $info.Length -gt $Maximum) { return $null }
    return [IO.File]::ReadAllBytes($Path)
}

function ConvertFrom-BditUtf8([byte[]]$Bytes) {
    $strict = New-Object Text.UTF8Encoding($false, $true)
    return $strict.GetString($Bytes)
}

function Assert-BditMembers($Object, [string[]]$Names) {
    if ($null -eq $Object -or $Object -isnot [Management.Automation.PSCustomObject]) { throw 'shape' }
    $actual = @($Object.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actual.Count -ne $Names.Count) { throw 'shape' }
    foreach ($name in $Names) { if ($actual -cnotcontains $name) { throw 'shape' } }
}

function Invoke-BditRunner {
    param([string]$Request, [string]$RequestSha256, [string]$Body, [string]$Result, [string]$Stop)
    Remove-Variable -Name BditEntry -Scope 1
    # ---- the request: exact bytes the parent pinned, then an exact shape ---------------------------------------------
    New-Variable -Name BditRequest -Option Private -Value $null
    try {
        $requestBytes = Read-BditBytes $Request 1048576
        if ($null -eq $requestBytes -or (Get-BditSha256 $requestBytes) -cne $RequestSha256) { throw 'digest' }
        $BditRequest = ConvertFrom-BditUtf8 $requestBytes | ConvertFrom-Json
        Assert-BditMembers $BditRequest @('schemaVersion', 'kind', 'runId', 'scriptId', 'manifestSha256', 'scriptSha256',
            'runnerTemplateSha256', 'adapter', 'reportId', 'reportSchemaVersion', 'tenantId', 'expectedAccountUpn', 'module',
            'sourceCommands', 'sections', 'maximumRows', 'parameters')
        if ($BditRequest.schemaVersion -ne 1 -or $BditRequest.kind -cne 'scriptReadRequest' -or $BditRequest.adapter -cne 'exchangeOnline') { throw 'kind' }
        Assert-BditMembers $BditRequest.module @('name', 'minimumVersion')
        foreach ($section in @($BditRequest.sections)) { Assert-BditMembers $section @('id', 'columns') }
        foreach ($parameter in @($BditRequest.parameters)) { Assert-BditMembers $parameter @('name', 'type', 'bound', 'payload') }
        if ($BditRequest.maximumRows -isnot [int] -and $BditRequest.maximumRows -isnot [long]) { throw 'rows' }
    }
    catch { Stop-BditRun 'RequestInvalid' }

    $bodyBytes = Read-BditBytes $Body 262144
    if ($null -eq $bodyBytes -or (Get-BditSha256 $bodyBytes) -cne [string]$BditRequest.scriptSha256) { Stop-BditRun 'BodyChanged' }
    $bodyText = [Text.Encoding]::ASCII.GetString($bodyBytes)

    # ---- typed parameters: decoded without culture coercion, then re-checked by .NET type -------------------------
    New-Variable -Name BditArguments -Option Private -Value @{}
    try {
        foreach ($parameter in @($BditRequest.parameters)) {
            if ($parameter.bound -isnot [bool]) { throw 'bound' }
            if (-not $parameter.bound) { if ($null -ne $parameter.payload) { throw 'unbound' }; continue }
            switch -CaseSensitive ([string]$parameter.type) {
                'Boolean' { if ($parameter.payload -isnot [bool]) { throw 'type' }; $value = [bool]$parameter.payload }
                'Integer' {
                    if ($parameter.payload -isnot [int] -and $parameter.payload -isnot [long]) { throw 'type' }
                    $value = [long]$parameter.payload
                }
                'Text' {
                    if ($parameter.payload -isnot [string]) { throw 'type' }
                    $value = ConvertFrom-BditUtf8 ([Convert]::FromBase64String($parameter.payload))
                }
                'Date' {
                    if ($parameter.payload -isnot [string]) { throw 'type' }
                    $text = ConvertFrom-BditUtf8 ([Convert]::FromBase64String($parameter.payload))
                    $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
                    $value = [datetime]::ParseExact($text, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, $styles)
                }
                default { throw 'type' }
            }
            if ($BditArguments.ContainsKey([string]$parameter.name)) { throw 'duplicate' }
            $BditArguments[[string]$parameter.name] = $value
        }
    }
    catch { Stop-BditRun 'RequestInvalid' }

    # ---- the body: parsed and checked before anything is imported or signed in -------------------------------------
    $sourceCommands = @($BditRequest.sourceCommands | ForEach-Object { [string]$_ })
    $allowedCommands = @('Use-BditRead', 'Where-Object', 'ForEach-Object', 'Select-Object', 'Sort-Object', 'Group-Object', 'Measure-Object', 'Set-StrictMode')
    $allowedTypes = @('string', 'int', 'long', 'bool', 'switch', 'datetime', 'guid', 'hashtable', 'ordered', 'pscustomobject', 'array',
        'object', 'object[]', 'string[]', 'double', 'decimal', 'int32', 'int64', 'boolean', 'System.Collections.Generic.List[object]',
        'Collections.Generic.List[object]', 'System.Collections.Generic.List[hashtable]', 'Collections.Generic.List[hashtable]')
    $deniedMembers = @('Invoke', 'InvokeReturnAsIs', 'Create', 'GetNewClosure', 'NewBoundScriptBlock', 'InvokeScript', 'AddScript',
        'AddCommand', 'SessionState', 'PSVariable', 'InvokeCommand', 'InvokeProvider', 'Module', 'ScriptBlock', 'GetSteppablePipeline',
        'ForEach', 'Where', 'Assembly', 'DeclaringType', 'ReflectedType', 'BaseType', 'UnderlyingSystemType')
    # Method calls are allow-listed: reflection reaches any .NET type through members that look harmless by name
    # (GetType, Assembly, InvokeMember), so a deny-list cannot be complete.
    $allowedMethods = @('ToString', 'Trim', 'TrimStart', 'TrimEnd', 'ToLower', 'ToUpper', 'ToLowerInvariant', 'ToUpperInvariant',
        'Contains', 'ContainsKey', 'StartsWith', 'EndsWith', 'Replace', 'Split', 'Substring', 'IndexOf', 'LastIndexOf', 'Equals',
        'CompareTo', 'Add', 'ToArray', 'IsNullOrEmpty', 'IsNullOrWhiteSpace', 'Join', 'new', 'AddDays', 'AddHours', 'AddMinutes',
        'AddSeconds', 'ToUniversalTime', 'PadLeft', 'PadRight', 'GetEnumerator')
    $allowedAttributes = @('Parameter', 'CmdletBinding', 'ValidateSet', 'ValidateRange', 'ValidateNotNull', 'ValidateNotNullOrEmpty',
        'AllowNull', 'AllowEmptyString', 'AllowEmptyCollection')
    # Values an allowed cmdlet may be given: literals and literal script blocks, whose contents are checked like the rest of the
    # body. A variable could hold a script block taken from elsewhere, so only these scalar parameters may take one.
    $scalarParameters = @('First', 'Last', 'Skip')
    function Test-BditLiteral($Node) {
        if ($Node -is [Management.Automation.Language.ConstantExpressionAst] -or $Node -is [Management.Automation.Language.ScriptBlockExpressionAst]) { return $true }
        if ($Node -is [Management.Automation.Language.ArrayLiteralAst]) { return @($Node.Elements | Where-Object { -not (Test-BditLiteral $_) }).Count -eq 0 }
        if ($Node -is [Management.Automation.Language.HashtableAst]) {
            foreach ($pair in $Node.KeyValuePairs) {
                if (-not (Test-BditLiteral $pair.Item1)) { return $false }
                $value = $pair.Item2
                if ($value -is [Management.Automation.Language.PipelineAst] -and $value.PipelineElements.Count -eq 1 -and
                    $value.PipelineElements[0] -is [Management.Automation.Language.CommandExpressionAst]) { $value = $value.PipelineElements[0].Expression }
                if (-not (Test-BditLiteral $value)) { return $false }
            }
            return $true
        }
        return $false
    }
    $deniedVariables = @('ExecutionContext', 'Host', 'PSCmdlet', 'MyInvocation', 'PSBoundParameters', 'BditRequest', 'BditArguments', 'BditGate', 'BditEntry')
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($bodyText, [ref]$tokens, [ref]$errors)
    $refusal = $null
    if ($errors.Count -ne 0) { $refusal = 'parse' }
    $defined = @($ast.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $_.Name })
    foreach ($name in $defined) {
        if ($allowedCommands -contains $name -or $sourceCommands -contains $name -or $name -like '*Bdit*' -or $name -like '*Connection*') { $refusal = 'reserved' }
    }
    foreach ($node in $ast.FindAll({ param($n) $true }, $true)) {
        if ($node -is [Management.Automation.Language.CommandAst]) {
            $name = $node.GetCommandName()
            if ($node.InvocationOperator -ne [Management.Automation.Language.TokenKind]::Unknown -or $null -eq $name) { $refusal = 'dynamic'; continue }
            if ($name -ceq 'Use-BditRead') {
                $elements = $node.CommandElements
                $ok = $elements.Count -eq 5 -and
                    $elements[1] -is [Management.Automation.Language.CommandParameterAst] -and $elements[1].ParameterName -ceq 'Command' -and
                    $elements[2] -is [Management.Automation.Language.StringConstantExpressionAst] -and $sourceCommands -ccontains $elements[2].Value -and
                    $elements[3] -is [Management.Automation.Language.CommandParameterAst] -and $elements[3].ParameterName -ceq 'Parameters' -and
                    ($elements[4] -is [Management.Automation.Language.HashtableAst] -or $elements[4] -is [Management.Automation.Language.VariableExpressionAst])
                if (-not $ok) { $refusal = 'gate' }
            }
            elseif ($allowedCommands -contains $name) {
                $elements = $node.CommandElements
                for ($i = 1; $i -lt $elements.Count; $i++) {
                    $element = $elements[$i]
                    if ($element -is [Management.Automation.Language.CommandParameterAst]) {
                        if ($element.ParameterName -like 'Member*' -or ($null -ne $element.Argument -and -not (Test-BditLiteral $element.Argument))) { $refusal = 'argument' }
                        continue
                    }
                    $previous = $elements[$i - 1]
                    $scalar = $element -is [Management.Automation.Language.VariableExpressionAst] -and -not $element.Splatted -and
                        $previous -is [Management.Automation.Language.CommandParameterAst] -and $scalarParameters -contains $previous.ParameterName
                    # ForEach-Object Name calls a member by name, so ForEach-Object takes only script blocks.
                    $memberName = $name -eq 'ForEach-Object' -and $element -isnot [Management.Automation.Language.ScriptBlockExpressionAst]
                    if ($memberName -or (-not $scalar -and -not (Test-BditLiteral $element))) { $refusal = 'argument' }
                }
            }
            elseif ($defined -notcontains $name) { $refusal = 'command' }
        }
        elseif ($node -is [Management.Automation.Language.VariableExpressionAst]) {
            $path = $node.VariablePath
            if (-not $path.IsUnqualified -and -not $path.IsLocal) { $refusal = 'scope' }
            if ($deniedVariables -contains $path.UserPath) { $refusal = 'variable' }
        }
        elseif ($node -is [Management.Automation.Language.TypeExpressionAst] -or $node -is [Management.Automation.Language.TypeConstraintAst]) {
            if ($allowedTypes -notcontains $node.TypeName.FullName) { $refusal = 'type' }
        }
        elseif ($node -is [Management.Automation.Language.MemberExpressionAst]) {
            $member = $node.Member
            if ($member -isnot [Management.Automation.Language.StringConstantExpressionAst]) { $refusal = 'member' }
            elseif ($deniedMembers -contains $member.Value) { $refusal = 'member' }
            elseif ($node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and $allowedMethods -cnotcontains $member.Value) { $refusal = 'method' }
        }
        elseif ($node -is [Management.Automation.Language.AttributeAst]) {
            if ($allowedAttributes -notcontains $node.TypeName.FullName) { $refusal = 'attribute' }
        }
        elseif ($node -is [Management.Automation.Language.TypeDefinitionAst] -or $node -is [Management.Automation.Language.DynamicKeywordStatementAst]) { $refusal = 'definition' }
        elseif ($node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.IsWorkflow) { $refusal = 'definition' }
        elseif ($node -is [Management.Automation.Language.UsingStatementAst]) { $refusal = 'using' }
    }
    if ($null -eq $ast.ParamBlock) { $refusal = 'param' }
    if ($null -ne $ast.ScriptRequirements) { $refusal = 'requires' }
    if ($null -ne $refusal) { Stop-BditRun 'BodyRefused' }

    # ---- module: installed, never installed by the toolkit ------------------------------------------------------------
    $moduleName = [string]$BditRequest.module.name
    $minimum = [version][string]$BditRequest.module.minimumVersion
    $available = @(Get-Module -ListAvailable -Name $moduleName | Where-Object { $_.Version -ge $minimum } | Sort-Object Version -Descending)
    if ($available.Count -eq 0) { Write-BditMarker 'MODULE_MISSING'; exit 3 }
    try {
        $imported = Import-Module -ModuleInfo $available[0] -PassThru -ErrorAction Stop
        if (@(Get-ConnectionInformation).Count -ne 0) { throw 'existing' }
    }
    catch { Stop-BditRun 'ModuleUnavailable' }
    # Captured once, from the imported module or its own nested modules, before the body exists. A later definition with the
    # same name cannot replace what the gate calls.
    $moduleFiles = @($imported.Path) + @($imported.NestedModules | ForEach-Object { $_.Path })
    $identity = @{}
    foreach ($fixed in @('Connect-ExchangeOnline', 'Disconnect-ExchangeOnline', 'Get-ConnectionInformation')) {
        $found = @(Get-Command -Name $fixed -CommandType Cmdlet, Function -ErrorAction SilentlyContinue)
        if ($found.Count -ne 1 -or $null -eq $found[0].Module -or $moduleFiles -notcontains $found[0].Module.Path) { Stop-BditRun 'ModuleUnavailable' }
        $identity[$fixed] = $found[0]
    }
    $before = @(Get-Module | ForEach-Object { $_.Path })

    # ---- sign-in with the confirmed account hint, then pin the source command identities ----------------------------
    $startedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
    Write-BditMarker 'CONNECTING'
    try {
        & $identity['Connect-ExchangeOnline'] -UserPrincipalName ([string]$BditRequest.expectedAccountUpn) -CommandName $sourceCommands -ShowBanner:$false -ErrorAction Stop
    }
    catch { Stop-BditRun 'ConnectFailed' }

    try {
        # Source reads come from the module, its nested modules, or the session module its own sign-in created.
        $owners = $moduleFiles + @(Get-Module | Where-Object { $before -notcontains $_.Path } | ForEach-Object { $_.Path })
        $readers = @{}
        foreach ($command in $sourceCommands) {
            $found = @(Get-Command -Name $command -CommandType Cmdlet, Function -ErrorAction SilentlyContinue)
            if ($found.Count -ne 1 -or $null -eq $found[0].Module -or $owners -notcontains $found[0].Module.Path) { throw 'identity' }
            $readers[$command] = $found[0]
        }
    }
    catch {
        try { & $identity['Disconnect-ExchangeOnline'] -Confirm:$false -ErrorAction SilentlyContinue } catch { }
        Stop-BditRun 'ModuleUnavailable'
    }

    # ---- the private gate --------------------------------------------------------------------------------------------
    New-Variable -Name BditGate -Option Private -Value (New-Module -Name BditReadGate -ArgumentList @(
            [guid][string]$BditRequest.tenantId, ([string]$BditRequest.expectedAccountUpn).Trim(), $identity['Get-ConnectionInformation'], $readers, $Stop) -ScriptBlock {
            param($Tenant, $Account, $Connections, $Readers, $StopFile)
            $script:Failure = $null
            $script:Observed = $null
            # A token is usable only when it is reported Active and its expiry is read as a time after now. A missing, null,
            # malformed or ambiguous status or expiry is never guessed usable. Typed values keep their own meaning: a
            # DateTimeOffset is an instant, a Local DateTime is converted, and an Unspecified DateTime is the documented UTC.
            # Text is accepted only as ISO 8601 with Z or an explicit offset, read with the invariant culture.
            function Test-BditToken($Connection) {
                $statusProperty = $Connection.PSObject.Properties['TokenStatus']
                $expiryProperty = $Connection.PSObject.Properties['TokenExpiryTimeUTC']
                if ($null -eq $statusProperty -or $null -eq $expiryProperty) { return $false }
                if ($statusProperty.Value -isnot [string] -or -not [string]::Equals($statusProperty.Value, 'Active', [StringComparison]::OrdinalIgnoreCase)) { return $false }
                $value = $expiryProperty.Value
                if ($value -is [DateTimeOffset]) { $expiry = $value.UtcDateTime }
                elseif ($value -is [DateTime]) {
                    if ($value.Kind -eq [DateTimeKind]::Local) { $expiry = $value.ToUniversalTime() }
                    else { $expiry = [DateTime]::SpecifyKind($value, [DateTimeKind]::Utc) }
                }
                elseif ($value -is [string]) {
                    if ($value -cnotmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?(Z|[+-][0-9]{2}:[0-9]{2})$') { return $false }
                    $parsed = [DateTimeOffset]::MinValue
                    if (-not [DateTimeOffset]::TryParse($value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) { return $false }
                    $expiry = $parsed.UtcDateTime
                }
                else { return $false }
                return $expiry -gt [DateTime]::UtcNow
            }
            # 'Usable', or why not: 'Identity' (none, several, another tenant or account) or 'Expired' (the token).
            function Test-BditConnection {
                $live = @(& $Connections | Where-Object { $_.State -eq 'Connected' -and -not $_.IsEopSession })
                if ($live.Count -ne 1) { return 'Identity' }
                $tenantProperty = $live[0].PSObject.Properties['TenantID']
                $accountProperty = $live[0].PSObject.Properties['UserPrincipalName']
                if ($null -eq $tenantProperty -or $null -eq $accountProperty) { return 'Identity' }
                $parsed = [guid]::Empty
                if (-not [guid]::TryParse([string]$tenantProperty.Value, [ref]$parsed) -or $parsed -ne $Tenant) { return 'Identity' }
                $observed = ([string]$accountProperty.Value).Trim()
                if (-not [string]::Equals($observed, $Account, [StringComparison]::OrdinalIgnoreCase)) { return 'Identity' }
                if (-not (Test-BditToken $live[0])) { return 'Expired' }
                $script:Observed = $observed
                return 'Usable'
            }
            function Get-BditConnectionFailure([string]$State) { if ($State -eq 'Expired') { return 'TokenExpired' } else { return 'IdentityChanged' } }
            function Assert-BditGate {
                if ($null -ne $script:Failure) { throw 'BDIT-GATE-CLOSED' }
                if ([IO.File]::Exists($StopFile)) { $script:Failure = 'Stopped'; throw 'BDIT-STOP' }
                $state = Test-BditConnection
                if ($state -ne 'Usable') { $script:Failure = Get-BditConnectionFailure $state; throw 'BDIT-IDENTITY' }
            }
            function Use-BditRead {
                param([Parameter(Mandatory = $true)][string]$Command, [Parameter(Mandatory = $true)][hashtable]$Parameters)
                if (-not $Readers.ContainsKey($Command)) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                # Only the read's own parameters, as data. Common parameters and script-block values are refused.
                $common = @('ErrorAction', 'WarningAction', 'InformationAction', 'ErrorVariable', 'WarningVariable', 'InformationVariable',
                    'OutVariable', 'OutBuffer', 'PipelineVariable', 'Verbose', 'Debug', 'WhatIf', 'Confirm', 'ProgressAction')
                foreach ($key in @($Parameters.Keys)) {
                    if ($key -isnot [string] -or $common -contains $key -or $Parameters[$key] -is [scriptblock]) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                }
                Assert-BditGate
                [Console]::Out.WriteLine('BDIT:READ:' + $Command); [Console]::Out.Flush()
                $arguments = @{} + $Parameters
                $arguments['ErrorAction'] = 'Stop'
                $output = & $Readers[$Command] @arguments
                Assert-BditGate
                return $output
            }
            function Get-BditGateState { return @{ Failure = $script:Failure; Observed = $script:Observed } }
            function Close-BditGate {
                if ($null -ne $script:Failure) { return }
                $state = Test-BditConnection
                if ($state -ne 'Usable') { $script:Failure = Get-BditConnectionFailure $state }
            }
            Export-ModuleMember -Function Use-BditRead
        })
    $null = Import-Module -ModuleInfo $BditGate -Global -Force

    $connectionState = 'Identity'
    try { $connectionState = & $BditGate { Test-BditConnection } } catch { $connectionState = 'Identity' }
    if ($connectionState -ne 'Usable') {
        try { & $identity['Disconnect-ExchangeOnline'] -Confirm:$false -ErrorAction SilentlyContinue } catch { }
        if ($connectionState -eq 'Expired') { Stop-BditRun 'ConnectionExpired' }
        Stop-BditRun 'IdentityMismatch'
    }

    # ---- run the body in its own module scope ------------------------------------------------------------------------
    # A module's scope chain is itself and the global scope, so the body cannot see this script's variables: the captured
    # command identities, the request or the gate. It sees only the imported Use-BditRead and its own typed arguments.
    $bodyOutput = $null
    $bodyFailed = $false
    try {
        $bodyHost = New-Module -Name BditBody -ScriptBlock { }
        $bodyOutput = @(& $bodyHost { param($Text, $Arguments) & ([scriptblock]::Create($Text)) @Arguments } $bodyText $BditArguments)
    }
    catch { $bodyFailed = $true }
    & $BditGate { Close-BditGate }
    $gate = & $BditGate { Get-BditGateState }
    try { & $identity['Disconnect-ExchangeOnline'] -Confirm:$false -ErrorAction SilentlyContinue } catch { }
    $endedAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
    if ($bodyFailed -and $null -eq $gate.Failure) { Stop-BditRun 'BodyFailed' }
    if ($gate.Failure -eq 'Unregistered') { Stop-BditRun 'BodyRefused' }

    # ---- project exactly the registered sections and columns ---------------------------------------------------------
    $failureReason = @{
        Stopped = 'The engineer stopped the run before the read finished; later rows were not read.'
        IdentityChanged = 'The Exchange connection no longer matched the verified tenant and account, so no rows from this run are kept.'
        TokenExpired = 'The Exchange sign-in expired, or its expiry could no longer be confirmed, during the run, so no rows from this run are kept.'
    }
    $states = @('Collected', 'Partial', 'Failed', 'NotAttempted', 'Cancelled')
    try {
        $sections = New-Object Collections.ArrayList
        # A stop can end the body before it returns anything; that is a cancelled run with no rows, not invalid output.
        $keepNothing = $gate.Failure -eq 'IdentityChanged' -or $gate.Failure -eq 'TokenExpired' -or ($gate.Failure -eq 'Stopped' -and $bodyFailed)
        if (-not $keepNothing) {
            if ($bodyOutput.Count -ne 1 -or $bodyOutput[0] -isnot [Collections.IDictionary]) { throw 'output' }
            $claimed = $bodyOutput[0]
            if (@($claimed.Keys).Count -ne 1 -or -not $claimed.Contains('Sections')) { throw 'output' }
            $claimedSections = @($claimed['Sections'])
            if ($claimedSections.Count -ne @($BditRequest.sections).Count) { throw 'output' }
        }
        $index = 0
        foreach ($registered in @($BditRequest.sections)) {
            $columns = @($registered.columns | ForEach-Object { [string]$_ })
            $rows = New-Object Collections.ArrayList
            if ($gate.Failure -eq 'IdentityChanged' -or $gate.Failure -eq 'TokenExpired') {
                $status = 'Failed'; $reason = $failureReason[[string]$gate.Failure]
            }
            elseif ($keepNothing) {
                $status = 'Cancelled'; $reason = $failureReason.Stopped
            }
            else {
                $section = $claimedSections[$index]
                if ($section -isnot [Collections.IDictionary] -or @($section.Keys).Count -ne 4) { throw 'output' }
                foreach ($key in @('Id', 'Status', 'Error', 'Rows')) { if (-not $section.Contains($key)) { throw 'output' } }
                if ([string]$section['Id'] -cne [string]$registered.id -or $states -cnotcontains [string]$section['Status']) { throw 'output' }
                $status = [string]$section['Status']
                $reason = if ($null -eq $section['Error']) { $null } else { [string]$section['Error'] }
                $claimedRows = @($section['Rows'])
                if ($claimedRows.Count -gt $BditRequest.maximumRows) { throw 'rows' }
                foreach ($row in $claimedRows) {
                    if ($row -isnot [Collections.IDictionary] -or @($row.Keys).Count -ne $columns.Count) { throw 'output' }
                    $projected = [ordered]@{}
                    foreach ($column in $columns) {
                        if (-not $row.Contains($column)) { throw 'output' }
                        $value = $row[$column]
                        if ($null -ne $value -and $value -isnot [string] -and $value -isnot [bool]) { throw 'output' }
                        $projected[$column] = $value
                    }
                    [void]$rows.Add($projected)
                }
                if ($gate.Failure -eq 'Stopped') { $status = 'Cancelled'; $reason = $failureReason.Stopped }
            }
            [void]$sections.Add([ordered]@{ id = [string]$registered.id; status = $status; error = $reason; limitations = @(); rows = @($rows) })
            $index++
        }
    }
    catch { Stop-BditRun 'OutputInvalid' }

    $parameters = @($BditRequest.parameters | ForEach-Object { [ordered]@{ name = [string]$_.name; type = [string]$_.type; bound = [bool]$_.bound; payload = $_.payload } })
    $envelope = [ordered]@{
        schemaVersion = 1
        kind = 'scriptReadResult'
        runId = [string]$BditRequest.runId
        scriptId = [string]$BditRequest.scriptId
        manifestSha256 = [string]$BditRequest.manifestSha256
        scriptSha256 = [string]$BditRequest.scriptSha256
        runnerTemplateSha256 = [string]$BditRequest.runnerTemplateSha256
        adapter = [string]$BditRequest.adapter
        reportId = [string]$BditRequest.reportId
        reportSchemaVersion = 1
        tenantId = [string]$BditRequest.tenantId
        observedAccountUpn = $gate.Observed
        startedAt = $startedAt
        endedAt = $endedAt
        runtimeVersion = $PSVersionTable.PSVersion.ToString()
        moduleVersion = $imported.Version.ToString()
        parameters = $parameters
        sections = @($sections)
    }
    try {
        $json = ConvertTo-Json -InputObject $envelope -Depth 8 -Compress
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($json)
        $stream = New-Object IO.FileStream($Result, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    }
    catch { Stop-BditRun 'OutputInvalid' }
    Write-BditMarker 'DONE'
    exit 0
}

Invoke-BditRunner -Request $BditEntry[0] -RequestSha256 $BditEntry[1] -Body $BditEntry[2] -Result $BditEntry[3] -Stop $BditEntry[4]
