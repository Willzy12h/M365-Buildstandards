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
        if (($BditRequest.reportSchemaVersion -isnot [int] -and $BditRequest.reportSchemaVersion -isnot [long]) -or $BditRequest.reportSchemaVersion -notin @(1,2)) { throw 'report' }
        if ($BditRequest.reportSchemaVersion -eq 2) {
            if (@('exo-inbox-rules','exo-mailbox-permissions','exo-calendar-permissions','exo-distribution-groups') -cnotcontains $BditRequest.reportId -or $BditRequest.maximumRows -lt 1 -or $BditRequest.maximumRows -gt 5000) { throw 'report' }
            $expectedCommands = if ($BditRequest.reportId -ceq 'exo-inbox-rules') { 'Get-EXOMailbox,Get-InboxRule' } elseif ($BditRequest.reportId -ceq 'exo-mailbox-permissions') { 'Get-EXOMailbox,Get-EXOMailboxPermission,Get-EXORecipientPermission' } elseif ($BditRequest.reportId -ceq 'exo-calendar-permissions') { 'Get-EXOMailbox,Get-EXOMailboxFolderStatistics,Get-EXOMailboxFolderPermission' } else { 'Get-DistributionGroup,Get-DistributionGroupMember' }
            if ((@($BditRequest.sourceCommands) -join ',') -cne $expectedCommands) { throw 'source' }
        }
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
    if ($BditRequest.reportSchemaVersion -eq 2) {
        try {
            $targetParameter = if ($BditRequest.reportId -ceq 'exo-distribution-groups') { 'GroupGuids' } else { 'MailboxGuids' }
            if ($BditArguments[$targetParameter] -isnot [string]) { throw 'scope' }
            if ($BditRequest.reportId -ceq 'exo-inbox-rules') {
                if ($BditArguments.Count -ne 2 -or $BditArguments['IncludeHidden'] -isnot [bool]) { throw 'scope' }
            } elseif ($BditRequest.reportId -ceq 'exo-calendar-permissions' -or $BditRequest.reportId -ceq 'exo-distribution-groups') {
                if ($BditArguments.Count -ne 1) { throw 'scope' }
            } else {
                if ($BditArguments.Count -ne 4) { throw 'scope' }
                foreach ($kind in @('FullAccess','SendAs','SendOnBehalf')) { if ($BditArguments[$kind] -isnot [bool]) { throw 'scope' } }
                if (-not ($BditArguments['FullAccess'] -or $BditArguments['SendAs'] -or $BditArguments['SendOnBehalf'])) { throw 'scope' }
            }
            $targetIds = $BditArguments[$targetParameter].Split(',')
            if ($targetIds.Count -lt 1 -or $targetIds.Count -gt 50 -or $targetIds.Count -gt $BditRequest.maximumRows) { throw 'scope' }
            $targetSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($target in $targetIds) {
                if ($target -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or $target -ceq '00000000-0000-0000-0000-000000000000' -or -not $targetSet.Add($target)) { throw 'scope' }
            }
        }
        catch { Stop-BditRun 'RequestInvalid' }
    }

    # ---- the body: parsed and checked before anything is imported or signed in -------------------------------------
    $sourceCommands = @($BditRequest.sourceCommands | ForEach-Object { [string]$_ })
    $allowedCommands = @('Use-BditRead', 'Where-Object', 'ForEach-Object', 'Select-Object', 'Sort-Object', 'Group-Object', 'Measure-Object', 'Set-StrictMode')
    $allowedTypes = @('string', 'int', 'long', 'bool', 'switch', 'datetime', 'guid', 'hashtable', 'ordered', 'pscustomobject', 'array',
        'object', 'object[]', 'string[]', 'double', 'decimal', 'uint64', 'int32', 'int64', 'boolean', 'System.Collections.Generic.List[object]',
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
            [guid][string]$BditRequest.tenantId, ([string]$BditRequest.expectedAccountUpn).Trim(), $identity['Get-ConnectionInformation'], $readers, $Stop,
            [int]$BditRequest.reportSchemaVersion, [string]$BditRequest.reportId, $BditArguments, [int]$BditRequest.maximumRows) -ScriptBlock {
            param($Tenant, $Account, $Connections, $Readers, $StopFile, $ReportVersion, $ReportId, $ReportArguments, $MaximumRows)
            $script:Failure = $null
            $script:Observed = $null
            $script:FamilyVersion = $ReportVersion
            $script:FamilyReportId = $ReportId
            $script:FamilyTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $script:FamilyPermissionSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $script:FamilyVerifiedTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $script:FamilyCalendarProof = @{}
            $script:FamilyObservationMaximum = 0
            $script:FamilyIncludeHidden = $false
            $script:FamilySourceCalls = 0
            $script:FamilyRowsRead = 0
            $script:FamilyProjectedBytes = 2
            $script:FamilyStarted = [Diagnostics.Stopwatch]::StartNew()
            if ($ReportVersion -eq 2) {
                $targetParameter = if ($ReportId -ceq 'exo-distribution-groups') { 'GroupGuids' } else { 'MailboxGuids' }
                foreach ($target in $ReportArguments[$targetParameter].Split(',')) { $null = $script:FamilyTargets.Add($target) }
                if ($ReportId -ceq 'exo-inbox-rules') { $script:FamilyIncludeHidden = $ReportArguments['IncludeHidden'] }
                elseif ($ReportId -ceq 'exo-mailbox-permissions') { foreach ($kind in @('FullAccess','SendAs','SendOnBehalf')) { if ($ReportArguments[$kind]) { $null = $script:FamilyPermissionSources.Add($kind) } } }
                # Every selected target retains one diagnostic row, even after failure/cancellation.
                $script:FamilyObservationMaximum = $MaximumRows - $script:FamilyTargets.Count
            }
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
            # INT-128 private bounded projection. Only fixed engine-owned callers may supply the producer/fields;
            # this function is not exported to a library body and adds no command or parameter admission.
            function Read-BditBoundedRows {
                param([scriptblock]$Producer, [string[]]$Properties, [bool]$UseSharedBudget = $false, [bool]$CountAsObservation = $false)
                if ($null -eq $Producer -or $null -eq $Properties -or $Properties.Count -lt 1 -or $Properties.Count -gt 32) { throw 'BDIT-READ-SHAPE' }
                $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($name in $Properties) {
                    if ($name -cnotmatch '^[A-Za-z][A-Za-z0-9]{0,63}$' -or -not $names.Add($name)) { throw 'BDIT-READ-SHAPE' }
                }
                function Convert-BditProjectedValue($Value, [bool]$AllowList = $true) {
                    if ($null -eq $Value) { return $null }
                    if ($Value -is [string]) {
                        if ($Value.Length -gt 1024) { throw 'BDIT-READ-LIMIT' }
                        return $Value
                    }
                    if ($Value -is [bool] -or $Value -is [guid] -or $Value -is [datetime] -or $Value -is [datetimeoffset] -or
                        $Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or $Value -is [uint16] -or
                        $Value -is [int32] -or $Value -is [uint32] -or $Value -is [int64] -or $Value -is [uint64] -or
                        $Value -is [decimal]) { return $Value }
                    if ($Value -is [enum]) { return Convert-BditProjectedValue ([string]$Value) $false }
                    if ($AllowList -and $Value -is [Collections.IEnumerable] -and $Value -isnot [Collections.IDictionary]) {
                        $values = [Collections.Generic.List[object]]::new()
                        foreach ($entry in $Value) {
                            if ($values.Count -ge 100) { throw 'BDIT-READ-LIMIT' }
                            $values.Add((Convert-BditProjectedValue $entry $false))
                        }
                        return ,$values.ToArray()
                    }
                    # Some Exchange principal/reference types expose RawIdentity. Keep only that explicitly labelled
                    # field, never arbitrary object serialisation, display-name resolution or a generic ToString call.
                    if ($Value -isnot [Collections.IDictionary]) {
                        $raw = $Value.PSObject.Properties['RawIdentity']
                        if ($null -ne $raw -and $raw.Value -is [string]) {
                            return [pscustomobject][ordered]@{ Source = 'RawIdentity'; Value = (Convert-BditProjectedValue $raw.Value $false) }
                        }
                        $type = $Value.PSObject.Properties['UserType']
                        if ($null -ne $type -and ($type.Value -is [string] -or $type.Value -is [enum])) {
                            $special = [string]$type.Value
                            if ($special -ceq 'Default' -or $special -ceq 'Anonymous') {
                                return [pscustomobject][ordered]@{ Source = 'SpecialPrincipal'; Value = $special }
                            }
                        }
                        $distinguished = $Value.PSObject.Properties['DistinguishedName']
                        if ($null -ne $distinguished -and $distinguished.Value -is [string]) {
                            return [pscustomobject][ordered]@{ Source = 'DistinguishedName'; Value = (Convert-BditProjectedValue $distinguished.Value $false) }
                        }
                        $label = $Value.PSObject.Properties['DisplayName']
                        if ($null -ne $label -and $label.Value -is [string]) {
                            # A raw unresolved label is not an exact principal identity. The family adapter must keep
                            # it explicitly unresolved/partial, never resolve it by name or assign a fabricated GUID.
                            return [pscustomobject][ordered]@{ Source = 'UnresolvedDisplayLabel'; Value = (Convert-BditProjectedValue $label.Value $false) }
                        }
                    }
                    throw 'BDIT-READ-SHAPE'
                }
                $rows = [Collections.Generic.List[object]]::new()
                $budget = @{ Bytes = 2 }
                $utf8 = [Text.UTF8Encoding]::new($false, $true)
                & $Producer | ForEach-Object {
                    # Detect the sentinel before projections/lookups and before another body-visible array exists.
                    if ($rows.Count -ge 5000) { throw 'BDIT-READ-LIMIT' }
                    Assert-BditGate
                    if ($CountAsObservation) {
                        if ($script:FamilyRowsRead -ge $script:FamilyObservationMaximum) { throw 'BDIT-READ-LIMIT' }
                        $script:FamilyRowsRead++
                    }
                    if ($null -eq $_ -or $_ -is [Collections.IDictionary]) { throw 'BDIT-READ-SHAPE' }
                    $projected = [ordered]@{}
                    foreach ($name in $Properties) {
                        $property = $_.PSObject.Properties[$name]
                        if ($null -ne $property) { $projected[$name] = Convert-BditProjectedValue $property.Value }
                    }
                    # Only already whitelisted, bounded scalar/reference/list values reach JSON sizing. Never raw module objects.
                    $size = $utf8.GetByteCount((ConvertTo-Json -InputObject $projected -Depth 6 -Compress)) + 1
                    $used = if ($UseSharedBudget) { $script:FamilyProjectedBytes } else { $budget.Bytes }
                    if ($size -gt (33554432 - $used)) { throw 'BDIT-READ-LIMIT' }
                    if ($UseSharedBudget) { $script:FamilyProjectedBytes += $size } else { $budget.Bytes += $size }
                    $rows.Add([pscustomobject]$projected)
                }
                Assert-BditGate
                return ,$rows.ToArray()
            }
            # Fixed INT-128 source policy, private to this engine-owned module. No body-provided projections,
            # arbitrary parameter names, unverified child targets or caller-selected ResultSize are admitted.
            function Read-BditFamilyRows {
                param([string]$Command, [hashtable]$Parameters)
                Assert-BditGate
                if (@('exo-inbox-rules','exo-mailbox-permissions','exo-calendar-permissions','exo-distribution-groups') -cnotcontains $script:FamilyReportId) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                if ($script:FamilySourceCalls -ge 100 -or $script:FamilyStarted.Elapsed.TotalSeconds -ge 300) { throw 'BDIT-READ-LIMIT' }
                $arguments = @{ ErrorAction = 'Stop' }
                $observation = $false
                if ($Command -ceq 'Get-EXOMailboxPermission' -or $Command -ceq 'Get-EXORecipientPermission') {
                    $kind = if ($Command -ceq 'Get-EXOMailboxPermission') { 'FullAccess' } else { 'SendAs' }
                    if ($script:FamilyReportId -cne 'exo-mailbox-permissions' -or -not $script:FamilyPermissionSources.Contains($kind) -or
                        $Parameters.Count -ne 1 -or -not $Parameters.ContainsKey('Identity') -or $Parameters['Identity'] -isnot [string] -or
                        -not $script:FamilyTargets.Contains($Parameters['Identity']) -or -not $script:FamilyVerifiedTargets.Contains($Parameters['Identity'])) {
                        $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED'
                    }
                    if ($script:FamilyRowsRead -ge $script:FamilyObservationMaximum) { throw 'BDIT-READ-LIMIT' }
                    $properties = if ($kind -ceq 'FullAccess') { @('User','AccessRights','IsInherited','Deny') } else { @('Trustee','AccessRights','IsInherited','Deny') }
                    $arguments['Identity'] = $Parameters['Identity']
                    $arguments['ResultSize'] = 1 + $script:FamilyObservationMaximum - $script:FamilyRowsRead
                    $observation = $true
                } else {
                switch -CaseSensitive ($Command) {
                    'Get-DistributionGroup' {
                        if ($script:FamilyReportId -cne 'exo-distribution-groups' -or $Parameters.Count -ne 1 -or
                            -not $Parameters.ContainsKey('Identity') -or $Parameters['Identity'] -isnot [string] -or -not $script:FamilyTargets.Contains($Parameters['Identity'])) {
                            $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED'
                        }
                        $null = $script:FamilyVerifiedTargets.Remove($Parameters['Identity'])
                        $properties = @('Guid','ExternalDirectoryObjectId','DisplayName','PrimarySmtpAddress','RecipientTypeDetails','ManagedBy','MemberJoinRestriction','MemberDepartRestriction','RequireSenderAuthenticationEnabled','HiddenFromAddressListsEnabled')
                        $arguments['Identity'] = $Parameters['Identity']
                    }
                    'Get-DistributionGroupMember' {
                        if ($script:FamilyReportId -cne 'exo-distribution-groups' -or $Parameters.Count -ne 1 -or
                            -not $Parameters.ContainsKey('Identity') -or $Parameters['Identity'] -isnot [string] -or -not $script:FamilyTargets.Contains($Parameters['Identity']) -or
                            -not $script:FamilyVerifiedTargets.Contains($Parameters['Identity'])) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                        if ($script:FamilyRowsRead -ge $script:FamilyObservationMaximum) { throw 'BDIT-READ-LIMIT' }
                        $properties = @('Guid','ExternalDirectoryObjectId','Identity','DisplayName','PrimarySmtpAddress','RecipientTypeDetails')
                        $arguments['Identity'] = $Parameters['Identity']; $arguments['ResultSize'] = 1 + $script:FamilyObservationMaximum - $script:FamilyRowsRead
                        $observation = $true
                    }
                    'Get-EXOMailbox' {
                        if ($Parameters.Count -ne 1 -or -not $Parameters.ContainsKey('Identity') -or $Parameters['Identity'] -isnot [string] -or -not $script:FamilyTargets.Contains($Parameters['Identity'])) {
                            $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED'
                        }
                        # A later failed/mismatched lookup cannot leave earlier proof usable for another child read.
                        $null = $script:FamilyVerifiedTargets.Remove($Parameters['Identity'])
                        $script:FamilyCalendarProof.Remove($Parameters['Identity'])
                        $properties = @('ExchangeGuid','ExternalDirectoryObjectId','DisplayName','PrimarySmtpAddress')
                        if ($script:FamilyReportId -ceq 'exo-mailbox-permissions' -and $script:FamilyPermissionSources.Contains('SendOnBehalf')) { $properties += 'GrantSendOnBehalfTo' }
                        $arguments['Identity'] = $Parameters['Identity']
                        $arguments['Properties'] = $properties
                    }
                    'Get-EXOMailboxFolderStatistics' {
                        if ($script:FamilyReportId -cne 'exo-calendar-permissions' -or $Parameters.Count -ne 1 -or
                            -not $Parameters.ContainsKey('Identity') -or $Parameters['Identity'] -isnot [string] -or
                            -not $script:FamilyVerifiedTargets.Contains($Parameters['Identity'])) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                        $script:FamilyCalendarProof.Remove($Parameters['Identity'])
                        $properties = @('FolderId','FolderPath','FolderType','Identity')
                        $arguments['Identity'] = $Parameters['Identity']; $arguments['FolderScope'] = 'Calendar'
                        $observation = $true
                    }
                    'Get-EXOMailboxFolderPermission' {
                        if ($script:FamilyReportId -cne 'exo-calendar-permissions' -or $Parameters.Count -ne 1 -or
                            -not $Parameters.ContainsKey('Identity') -or $Parameters['Identity'] -isnot [string]) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                        $verifiedFolder = $false
                        foreach ($mailbox in @($script:FamilyCalendarProof.Keys)) {
                            if ($script:FamilyCalendarProof[$mailbox] -ceq $Parameters['Identity'] -and $script:FamilyVerifiedTargets.Contains($mailbox)) { $verifiedFolder = $true }
                        }
                        if (-not $verifiedFolder) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                        $properties = @('User','AccessRights','SharingPermissionFlags')
                        $arguments['Identity'] = $Parameters['Identity']; $observation = $true
                    }
                    'Get-InboxRule' {
                        if ($script:FamilyReportId -cne 'exo-inbox-rules') { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                        if ($Parameters.Count -ne 2 -or -not $Parameters.ContainsKey('Mailbox') -or -not $Parameters.ContainsKey('IncludeHidden') -or
                            $Parameters['Mailbox'] -isnot [string] -or -not $script:FamilyTargets.Contains($Parameters['Mailbox']) -or
                            -not $script:FamilyVerifiedTargets.Contains($Parameters['Mailbox']) -or $Parameters['IncludeHidden'] -isnot [bool] -or
                            $Parameters['IncludeHidden'] -ne $script:FamilyIncludeHidden) { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                        if ($script:FamilyRowsRead -ge $script:FamilyObservationMaximum) { throw 'BDIT-READ-LIMIT' }
                        $properties = @('RuleIdentity','Identity','Name','Enabled','Priority','ForwardTo','ForwardAsAttachmentTo','RedirectTo','DeleteMessage','MarkAsRead','StopProcessingRules')
                        $arguments['Mailbox'] = $Parameters['Mailbox']
                        $arguments['IncludeHidden'] = $script:FamilyIncludeHidden
                        $arguments['ResultSize'] = 1 + $script:FamilyObservationMaximum - $script:FamilyRowsRead
                        $observation = $true
                    }
                    default { $script:Failure = 'Unregistered'; throw 'BDIT-UNREGISTERED' }
                }
                }
                $script:FamilySourceCalls++
                [Console]::Out.WriteLine('BDIT:READ:' + $Command); [Console]::Out.Flush()
                $output = Read-BditBoundedRows -Producer { & $Readers[$Command] @arguments } -Properties $properties -UseSharedBudget $true -CountAsObservation $observation
                if ($script:FamilyStarted.Elapsed.TotalSeconds -ge 300) { throw 'BDIT-READ-LIMIT' }
                if ($Command -ceq 'Get-DistributionGroup' -and $output.Count -eq 1) {
                    $guidProperty = $output[0].PSObject.Properties['Guid']; $actual = [guid]::Empty
                    $kindProperty = $output[0].PSObject.Properties['RecipientTypeDetails']
                    if ($null -ne $guidProperty -and ($guidProperty.Value -is [guid] -or $guidProperty.Value -is [string]) -and
                        [guid]::TryParseExact([string]$guidProperty.Value, 'D', [ref]$actual) -and $actual.ToString('D') -ceq $Parameters['Identity']) {
                        $owners = $output[0].PSObject.Properties['ManagedBy']
                        if ($null -ne $owners -and $owners.Value -is [array]) {
                            foreach ($owner in $owners.Value) { if ($script:FamilyRowsRead -ge $script:FamilyObservationMaximum) { throw 'BDIT-READ-LIMIT' }; $script:FamilyRowsRead++ }
                        }
                        if ($null -ne $kindProperty -and $kindProperty.Value -is [string] -and $kindProperty.Value -cin @('MailUniversalDistributionGroup','MailUniversalSecurityGroup','RoomList')) {
                            $null = $script:FamilyVerifiedTargets.Add($Parameters['Identity'])
                        }
                    }
                }
                if ($Command -ceq 'Get-EXOMailboxFolderStatistics') {
                    $calendars = @($output | Where-Object { $p = $_.PSObject.Properties['FolderType']; $null -ne $p -and $p.Value -is [string] -and $p.Value -ceq 'Calendar' })
                    $typesKnown = $true
                    foreach ($folder in $output) { $p = $folder.PSObject.Properties['FolderType']; if ($null -eq $p -or $p.Value -isnot [string] -or [string]::IsNullOrWhiteSpace($p.Value)) { $typesKnown = $false } }
                    if ($typesKnown -and $calendars.Count -eq 1) {
                        $pathProperty = $calendars[0].PSObject.Properties['FolderPath']; $idProperty = $calendars[0].PSObject.Properties['FolderId']
                        if ($null -ne $pathProperty -and $pathProperty.Value -is [string] -and $null -ne $idProperty -and $idProperty.Value -is [string] -and
                            -not [string]::IsNullOrWhiteSpace($idProperty.Value) -and $idProperty.Value -notmatch '[\x00-\x1F\x7F]') {
                            $path = $pathProperty.Value
                            if ($path.Length -ge 2 -and $path.Length -le 980 -and $path[0] -ceq '/' -and -not $path.Contains('\') -and -not $path.Contains(':') -and $path -notmatch '[\x00-\x1F\x7F]') {
                                $validPath = $true
                                foreach ($part in @($path.Split('/') | Select-Object -Skip 1)) { if ([string]::IsNullOrWhiteSpace($part) -or $part -ceq '.' -or $part -ceq '..') { $validPath = $false } }
                                if ($validPath) { $script:FamilyCalendarProof[$Parameters['Identity']] = $Parameters['Identity'] + ':' + $path.Replace('/','\') }
                            }
                        }
                    }
                }
                if ($Command -ceq 'Get-EXOMailbox' -and $output.Count -eq 1) {
                    $returned = $output[0].PSObject.Properties['ExchangeGuid']
                    $actual = [guid]::Empty
                    if ($null -ne $returned -and ($returned.Value -is [guid] -or $returned.Value -is [string]) -and
                        [guid]::TryParseExact([string]$returned.Value, 'D', [ref]$actual) -and $actual.ToString('D') -ceq $Parameters['Identity']) {
                        if ($script:FamilyReportId -ceq 'exo-mailbox-permissions' -and $script:FamilyPermissionSources.Contains('SendOnBehalf')) {
                            $grants = $output[0].PSObject.Properties['GrantSendOnBehalfTo']
                            # The projected attribute is independently bounded to100 values. Each resulting edge also
                            # consumes the shared diagnostic-reserved observation budget before it reaches the body.
                            if ($null -ne $grants -and $grants.Value -is [array]) {
                                foreach ($grant in $grants.Value) {
                                    if ($script:FamilyRowsRead -ge $script:FamilyObservationMaximum) { throw 'BDIT-READ-LIMIT' }
                                    $script:FamilyRowsRead++
                                }
                            }
                        }
                        $null = $script:FamilyVerifiedTargets.Add($Parameters['Identity'])
                    }
                }
                return ,$output
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
                if ($script:FamilyVersion -eq 2) {
                    $output = Read-BditFamilyRows -Command $Command -Parameters $Parameters
                    return $output
                }
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

    # Exact report2 cell projection. Complex module objects never reach serialisation: only these bounded DTO shapes do.
    function Require-BditOutputText($Text, [int]$Maximum, [bool]$Nullable = $false, [bool]$Blank = $false, [bool]$Newline = $false) {
        if ($null -eq $Text) { if ($Nullable) { return $null }; throw 'output' }
        if ($Text -isnot [string] -or $Text.Length -gt $Maximum -or (-not $Blank -and [string]::IsNullOrWhiteSpace($Text))) { throw 'output' }
        $controls = if ($Newline) { '[\x00-\x09\x0B-\x1F\x7F-\x9F]' } else { '\p{Cc}' }
        if ($Text -match $controls) { throw 'output' }
        return $Text
    }
    function Require-BditOutputMap($Map, [string[]]$Keys) {
        if ($Map -isnot [Collections.IDictionary] -or $Map.PSBase.Count -ne $Keys.Count) { throw 'output' }
        foreach ($key in $Keys) { if (@($Map.PSBase.Keys) -cnotcontains $key) { throw 'output' } }
    }

    function Convert-BditInboxRuleCell {
        param([string]$Section, [string]$Column, $Value)
        if ($Section -cnotin @('target-reads','rules')) { throw 'output' }
        if ($Column -ceq 'id') { if ($null -ne $Value) { throw 'output' }; return $null }
        if ($Column -ceq 'name') { return Require-BditOutputText $Value 1024 $true $true }
        if ($Column -ceq 'error') { return Require-BditOutputText $Value 4096 $true $false $true }
        $states = @('Collected','Partial','Failed','NotAttempted','Cancelled')
        if ($Column -ceq 'readStatus' -or ($Section -ceq 'target-reads' -and $Column -cin @('lookupReadStatus','childReadStatus'))) {
            if ($Value -isnot [string] -or $states -cnotcontains $Value) { throw 'output' }
            return $Value
        }
        if ($Section -ceq 'target-reads') {
            if ($Column -cin @('returnedEntries','excludedEntries')) {
                if ($null -eq $Value) { return $null }
                if (($Value -isnot [int] -and $Value -isnot [long]) -or $Value -lt 0 -or $Value -gt 5000) { throw 'output' }
                return $Value
            }
            if ($Column -cin @('requestedExchangeGuid','observedExchangeGuid','externalDirectoryObjectId','primarySmtpAddress')) {
                return Require-BditOutputText $Value 1024 ($Column -cne 'requestedExchangeGuid')
            }
            throw 'output'
        }
        if ($Column -cin @('mailboxExchangeGuid','ruleIdentity','actualIdentityRaw')) {
            return Require-BditOutputText $Value 1024 ($Column -ceq 'actualIdentityRaw')
        }
        $booleans = @('enabled','deleteMessage','markAsRead','stopProcessingRules')
        $lists = @('forwardTo','forwardAsAttachmentTo','redirectTo')
        if ($Column -cnotin $booleans -and $Column -cnotin $lists -and $Column -cne 'priority') { throw 'output' }
        Require-BditOutputMap $Value @('state','value','reason')
        $state = $Value['state']; $actual = $Value['value']; $reason = $Value['reason']
        if ($state -isnot [string] -or $state -cnotin @('Value','ReturnedNull','Missing','Failed','NotAttempted','Unsupported')) { throw 'output' }
        if ($state -cne 'Value') {
            if ($null -ne $actual) { throw 'output' }
            $reason = Require-BditOutputText $reason 4096 $false $false $true
            return [ordered]@{state=$state;value=$null;reason=$reason}
        }
        if ($null -ne $reason -or $null -eq $actual) { throw 'output' }
        if ($Column -cin $booleans) { if ($actual -isnot [bool]) { throw 'output' } }
        elseif ($Column -ceq 'priority') {
            if (($actual -isnot [int] -and $actual -isnot [long]) -or $actual -lt 0 -or $actual -gt 2147483647) { throw 'output' }
        }
        else {
            if ($actual -isnot [array] -or $actual.Count -gt 100) { throw 'output' }
            $references = [Collections.Generic.List[object]]::new()
            foreach ($reference in $actual) {
                Require-BditOutputMap $reference @('namespace','value','externalDirectoryObjectId')
                $source = $reference['namespace']
                if ($source -isnot [string] -or $source -cnotin @('ReturnedText','RawIdentity','DistinguishedName','SpecialPrincipal','UnresolvedDisplayLabel')) { throw 'output' }
                $text = Require-BditOutputText $reference['value'] 1024
                if ($source -ceq 'SpecialPrincipal' -and $text -cnotin @('Default','Anonymous')) { throw 'output' }
                $directory = $reference['externalDirectoryObjectId']
                if ($null -ne $directory -and ($directory -isnot [string] -or $directory -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or $directory -ceq '00000000-0000-0000-0000-000000000000')) { throw 'output' }
                $references.Add([ordered]@{namespace=$source;value=$text;externalDirectoryObjectId=$directory})
            }
            $actual = $references.ToArray()
        }
        return [ordered]@{state=$state;value=$actual;reason=$null}
    }

    function Convert-BditPermissionCell {
        param([string]$Section, [string]$Column, $Value)
        if ($Section -cnotin @('target-reads','permissions')) { throw 'output' }
        if ($Column -cin @('id','name','readStatus','error','mailboxExchangeGuid')) {
            return Convert-BditInboxRuleCell 'rules' $Column $Value
        }
        if ($Section -ceq 'target-reads' -and $Column -cne 'permissionReads') {
            return Convert-BditInboxRuleCell 'target-reads' $Column $Value
        }
        if ($Section -ceq 'target-reads') {
            if ($Value -isnot [array] -or $Value.Count -ne 3) { throw 'output' }
            $reads = [Collections.Generic.List[object]]::new(); $kinds = @('FullAccess','SendAs','SendOnBehalf')
            for ($n = 0; $n -lt 3; $n++) {
                $read = $Value[$n]; Require-BditOutputMap $read @('kind','selected','status','error','returnedEntries','excludedEntries')
                if ($read['kind'] -isnot [string] -or $read['kind'] -cne $kinds[$n] -or $read['selected'] -isnot [bool]) { throw 'output' }
                $reads.Add([ordered]@{ kind=$read['kind']; selected=$read['selected'];
                    status=(Convert-BditInboxRuleCell 'target-reads' 'lookupReadStatus' $read['status']);
                    error=(Require-BditOutputText $read['error'] 4096 $true $false $true);
                    returnedEntries=(Convert-BditInboxRuleCell 'target-reads' 'returnedEntries' $read['returnedEntries']);
                    excludedEntries=(Convert-BditInboxRuleCell 'target-reads' 'excludedEntries' $read['excludedEntries']) })
            }
            return ,$reads.ToArray()
        }
        if ($Column -ceq 'permissionKind') {
            if ($Value -isnot [string] -or $Value -cnotin @('FullAccess','SendAs','SendOnBehalf')) { throw 'output' }; return $Value
        }
        if ($Column -ceq 'principal') {
            $group = Convert-BditInboxRuleCell 'rules' 'forwardTo' ([ordered]@{state='Value';value=@($Value);reason=$null})
            return $group.value[0]
        }
        if ($Column -cin @('isInherited','deny')) { return Convert-BditInboxRuleCell 'rules' 'enabled' $Value }
        if ($Column -cne 'rights') { throw 'output' }
        Require-BditOutputMap $Value @('state','value','reason')
        if ($Value['state'] -cne 'Value') { return Convert-BditInboxRuleCell 'rules' 'enabled' $Value }
        if ($null -ne $Value['reason'] -or $Value['value'] -isnot [array] -or $Value['value'].Count -gt 100) { throw 'output' }
        $rights = [Collections.Generic.List[object]]::new()
        foreach ($right in $Value['value']) { $rights.Add((Require-BditOutputText $right 1024)) }
        return [ordered]@{state='Value';value=$rights.ToArray();reason=$null}
    }

    function Convert-BditCalendarCell {
        param([string]$Section, [string]$Column, $Value)
        if ($Column -cin @('id','name','readStatus','error','mailboxExchangeGuid')) { return Convert-BditInboxRuleCell 'rules' $Column $Value }
        if ($Column -cin @('requestedExchangeGuid','observedExchangeGuid','externalDirectoryObjectId','primarySmtpAddress','lookupReadStatus','returnedEntries','excludedEntries')) {
            return Convert-BditInboxRuleCell $Section $Column $Value
        }
        if ($Section -ceq 'target-reads') {
            if ($Column -cin @('folderReadStatus','permissionReadStatus')) { return Convert-BditInboxRuleCell 'target-reads' 'childReadStatus' $Value }
            if ($Column -cin @('returnedFolders','defaultFolderMatches')) { return Convert-BditInboxRuleCell 'target-reads' 'returnedEntries' $Value }
            if ($Column -cin @('folderId','folderPath','nativeFolderIdentity')) { return Require-BditOutputText $Value 1024 $true }
        }
        if ($Section -ceq 'permissions') {
            if ($Column -ceq 'nativeFolderIdentity') { return Require-BditOutputText $Value 1024 }
            if ($Column -ceq 'principal') { return Convert-BditPermissionCell 'permissions' 'principal' $Value }
            if ($Column -cin @('rights','sharingPermissionFlags')) { return Convert-BditPermissionCell 'permissions' 'rights' $Value }
        }
        throw 'output'
    }

    function Convert-BditGroupCell {
        param([string]$Section, [string]$Column, $Value)
        if ($Section -cnotin @('target-reads','members')) { throw 'output' }
        if ($Column -cin @('id','name','readStatus','error')) { return Convert-BditInboxRuleCell 'rules' $Column $Value }
        if ($Column -cin @('requestedGroupGuid','groupGuid')) { return Require-BditOutputText $Value 1024 }
        if ($Column -cin @('observedGroupGuid','externalDirectoryObjectId','primarySmtpAddress')) { return Require-BditOutputText $Value 1024 $true }
        if ($Section -ceq 'target-reads') {
            if ($Column -cin @('lookupReadStatus','membershipReadStatus')) { return Convert-BditInboxRuleCell 'target-reads' 'childReadStatus' $Value }
            if ($Column -cin @('returnedEntries','excludedEntries')) { return Convert-BditInboxRuleCell 'target-reads' $Column $Value }
            if ($Column -cin @('requireSenderAuthenticationEnabled','hiddenFromAddressListsEnabled')) { return Convert-BditInboxRuleCell 'rules' 'enabled' $Value }
            if ($Column -ceq 'managedBy') { return Convert-BditInboxRuleCell 'rules' 'forwardTo' $Value }
        }
        if ($Section -ceq 'members' -and $Column -ceq 'sourceIdentity') {
            Require-BditOutputMap $Value @('namespace','value','externalDirectoryObjectId')
            if ($Value['namespace'] -ceq 'ExchangeRecipientGuid') {
                $guid = Require-BditOutputText $Value['value'] 36
                if ($guid -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or $guid -ceq '00000000-0000-0000-0000-000000000000') { throw 'output' }
                $directory = Require-BditOutputText $Value['externalDirectoryObjectId'] 36 $true
                if ($null -ne $directory -and ($directory -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or $directory -ceq '00000000-0000-0000-0000-000000000000')) { throw 'output' }
                return [ordered]@{ namespace='ExchangeRecipientGuid'; value=$guid; externalDirectoryObjectId=$directory }
            }
            $reference = Convert-BditPermissionCell 'permissions' 'principal' $Value
            if ($reference['namespace'] -ceq 'SpecialPrincipal') { throw 'output' }; return $reference
        }
        if ($Column -cin @('recipientTypeDetails','memberJoinRestriction','memberDepartRestriction')) {
            Require-BditOutputMap $Value @('state','value','reason')
            if ($Value['state'] -cne 'Value') { return Convert-BditInboxRuleCell 'rules' 'enabled' $Value }
            if ($null -ne $Value['reason']) { throw 'output' }
            return [ordered]@{state='Value';value=(Require-BditOutputText $Value['value'] 1024);reason=$null}
        }
        throw 'output'
    }

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
        # Reserve fixed envelope/parameter space and parent-assigned key growth before accumulating report2 rows.
        $familyOutputRows = 0
        $familyOutputBytes = 65536 + ($requestBytes.Length * 2) + (5000 * 96)
        $outputUtf8 = [Text.UTF8Encoding]::new($false, $true)
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
                    if ($row -isnot [Collections.IDictionary]) { throw 'output' }
                    $keyCount = if ($BditRequest.reportSchemaVersion -eq 2) { $row.PSBase.Count } else { @($row.Keys).Count }
                    if ($keyCount -ne $columns.Count) { throw 'output' }
                    $projected = [ordered]@{}
                    foreach ($column in $columns) {
                        if (-not $row.Contains($column)) { throw 'output' }
                        $value = $row[$column]
                        if ($BditRequest.reportSchemaVersion -eq 2) {
                            if (@($row.PSBase.Keys) -cnotcontains $column) { throw 'output' }
                            if ($BditRequest.reportId -ceq 'exo-mailbox-permissions') {
                                $value = Convert-BditPermissionCell -Section ([string]$registered.id) -Column $column -Value $value
                            } elseif ($BditRequest.reportId -ceq 'exo-calendar-permissions') {
                                $value = Convert-BditCalendarCell -Section ([string]$registered.id) -Column $column -Value $value
                            } elseif ($BditRequest.reportId -ceq 'exo-distribution-groups') {
                                $value = Convert-BditGroupCell -Section ([string]$registered.id) -Column $column -Value $value
                            } else { $value = Convert-BditInboxRuleCell -Section ([string]$registered.id) -Column $column -Value $value }
                        }
                        elseif ($null -ne $value -and $value -isnot [string] -and $value -isnot [bool]) {
                            # INT-121's sole collection-valued column. All other non-scalars remain refused.
                            if ([string]$BditRequest.reportId -cne 'exo-mailbox-holds' -or [string]$registered.id -cne 'holds' -or
                                $column -cne 'inPlaceHoldIds' -or $value -isnot [array] -or $value.Count -gt 100) { throw 'output' }
                            foreach ($hold in $value) {
                                if ($hold -isnot [string] -or [string]::IsNullOrWhiteSpace($hold) -or $hold.Length -gt 256 -or
                                    $hold -match '[\x00-\x1F\x7F]') { throw 'output' }
                            }
                        }
                        $projected[$column] = $value
                    }
                    if ($BditRequest.reportSchemaVersion -eq 2) {
                        $size = $outputUtf8.GetByteCount((ConvertTo-Json -InputObject $projected -Depth 10 -Compress)) + 1
                        if ($familyOutputRows -ge $BditRequest.maximumRows) { throw 'rows' }
                        $familyOutputRows++
                        if ($size -gt (33554432 - $familyOutputBytes)) { throw 'output' }
                        $familyOutputBytes += $size
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
        reportSchemaVersion = [int]$BditRequest.reportSchemaVersion
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
        $depth = if ($BditRequest.reportSchemaVersion -eq 2) { 12 } else { 8 }
        $json = ConvertTo-Json -InputObject $envelope -Depth $depth -Compress
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($json)
        if ($BditRequest.reportSchemaVersion -eq 2 -and $bytes.Length -gt 33554432) { throw 'output' }
        $stream = New-Object IO.FileStream($Result, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
    }
    catch { Stop-BditRun 'OutputInvalid' }
    Write-BditMarker 'DONE'
    exit 0
}

Invoke-BditRunner -Request $BditEntry[0] -RequestSha256 $BditEntry[1] -Body $BditEntry[2] -Result $BditEntry[3] -Stop $BditEntry[4]
