param([string]$Wrapper,[string]$Case)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
foreach($name in @('Read-BditBoundedRows','Read-BditFamilyRows','Use-BditRead')) {
    $definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true)
    if($null -eq $definition){throw 'Missing fixed source gate'}
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert-BditGate { }
$script:Reads=0;$script:Failure=$null;$script:FamilyVersion=2;$script:FamilyReportId='exo-message-trace'
$script:FamilySourceCalls=0;$script:FamilyRowsRead=0;$script:FamilyProjectedBytes=2;$script:FamilyObservationMaximum=98;$script:FamilyStarted=[Diagnostics.Stopwatch]::StartNew()
$script:TraceNextIndex=0
$script:FrozenTracePlan=[pscustomobject]@{Windows=@([pscustomobject]@{startUtc='2026-10-01T00:00:00.000Z';endUtc='2026-10-11T00:00:00.000Z'},[pscustomobject]@{startUtc='2026-10-11T00:00:00.000Z';endUtc='2026-10-12T00:00:00.000Z'});Sender='sender@synthetic.example';Recipient=$null;InternetMessageId=$null;MessageTraceId=$null;IncludeSubject=($Case -ceq 'subject')}
function Get-FakeTrace {
    param([datetime]$StartDate,[datetime]$EndDate,[int]$ResultSize,[string]$SenderAddress)
    $script:Reads++
    if($StartDate.Kind -ne [DateTimeKind]::Utc -or $StartDate.ToString('yyyy-MM-dd') -cne '2026-10-01' -or $EndDate.ToString('yyyy-MM-dd') -cne '2026-10-11' -or $ResultSize -ne 98 -or $SenderAddress -cne 'sender@synthetic.example'){throw 'Source dates/filter/cap changed'}
    if($Case -ceq 'failed-replay'){throw 'Synthetic failed source'}
    [pscustomobject]@{MessageTraceId=[guid]'44444444-4444-4444-8444-444444444444';RecipientAddress='recipient@synthetic.example';Received=[datetime]::SpecifyKind($StartDate.AddHours(1),[DateTimeKind]::Utc);SenderAddress=$SenderAddress;MessageId='<fixture@message.invalid>';Status='Delivered';Size=0;Subject='private subject';Private='excluded'}
}
$Readers=@{'Get-MessageTraceV2'=(Get-Command Get-FakeTrace)}
$parameters=@{WindowIndex=0}
if($Case -ceq 'arbitrary-date'){$parameters.StartDate='2026-10-02'}
if($Case -ceq 'string-index'){$parameters.WindowIndex='0'}
if($Case -ceq 'skipped-index'){$parameters.WindowIndex=1}
if($Case -ceq 'foreign-family'){$script:FamilyReportId='exo-transport-rules'}
$failed=$false;$result=$null
try{$result=Use-BditRead -Command 'Get-MessageTraceV2' -Parameters $parameters}catch{$failed=$true}
if($Case -cin @('arbitrary-date','string-index','skipped-index','foreign-family')) {
    if(-not $failed -or $script:Reads -ne 0){throw 'Unreviewed input reached the source'}
} elseif($Case -ceq 'failed-replay') {
    if(-not $failed -or $script:Reads -ne 1){throw 'Failed read was not observed'}
    try{Use-BditRead -Command 'Get-MessageTraceV2' -Parameters @{WindowIndex=0};throw 'Replay admitted'}catch{if($script:Reads -ne 1){throw 'Failed source was replayed'}}
} else {
    if($failed -or $script:Reads -ne 1 -or $result.ResultSize -ne 98 -or $result.Rows.Count -ne 1){throw 'Exact trace read was not admitted'}
    if($result.Rows[0].PSObject.Properties['Private']){throw 'Unselected field retained'}
    if($Case -ceq 'subject'){if($result.Rows[0].Subject -cne 'private subject'){throw 'Selected subject lost'}}
    elseif($result.Rows[0].PSObject.Properties['Subject']){throw 'Unselected subject retained'}
    if($result.Rows[0].Received -cne '2026-10-01T01:00:00.0000000Z'){throw 'Source UTC was not preserved'}
    if($Case -ceq 'replay') {
        $refused=$false;try{Use-BditRead -Command 'Get-MessageTraceV2' -Parameters @{WindowIndex=0}}catch{$refused=$true}
        if(-not $refused -or $script:Reads -ne 1){throw 'Reviewed window replayed'}
    }
}
'PASS '+$Case
