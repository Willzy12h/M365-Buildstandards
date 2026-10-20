param([string]$Wrapper,[string]$Payload,[string]$Valid)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
foreach($name in @('Read-BditTraceContext','Assert-BditUniqueJsonMembers','Assert-BditMembers','ConvertTo-BditTraceJsonString','Get-BditTraceFilterDigest','Get-BditTracePlan')) {
    $definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true)
    if($null -eq $definition){throw ('Missing fixed trace function '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
# A missing framework dependency must fail the fixture, never count as refusing forged input.
Assert-BditUniqueJsonMembers ([Text.Encoding]::UTF8.GetBytes('{}'))
$bytes=[Convert]::FromBase64String($Payload)
$failed=$false;$plan=$null;$failureText=''
try{
    $data=([Text.UTF8Encoding]::new($false,$true)).GetString($bytes)|ConvertFrom-Json
    $requestBytes=[Convert]::FromBase64String($data.requestUtf8Base64)
    Assert-BditUniqueJsonMembers $requestBytes
    $requestObject=([Text.UTF8Encoding]::new($false,$true)).GetString($requestBytes)|ConvertFrom-Json
    $requestObject.traceContext=Read-BditTraceContext $requestBytes
    $arguments=@{};foreach($p in $data.arguments.PSObject.Properties){$arguments[$p.Name]=$p.Value}
    $plan=Get-BditTracePlan $requestObject $arguments
}catch{$failed=$true;$failureText=$_.Exception.Message+' · '+$_.ScriptStackTrace}
if($Valid -ceq 'true') {
    if($failed -or $null -eq $plan -or $plan.Windows.Count -ne 2 -or $plan.Windows[0].startUtc -cne '2026-10-01T00:00:00.000Z'){throw ('The exact frozen trace request was not admitted: '+$failureText)}
} elseif(-not $failed){throw 'A forged trace context was admitted'}
'PASS trace request context'
