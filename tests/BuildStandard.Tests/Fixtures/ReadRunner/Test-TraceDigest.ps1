param([string]$Wrapper,[string]$Payload,[string]$Expected)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
foreach($name in @('ConvertTo-BditTraceJsonString','Get-BditTraceFilterDigest')) {
    $definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true)
    if($null -eq $definition){throw ('Missing fixed trace digest function '+$name)}
    . ([scriptblock]::Create($definition.Extent.Text))
}
$inputValues=([Text.UTF8Encoding]::new($false,$true)).GetString([Convert]::FromBase64String($Payload))|ConvertFrom-Json
$actual=Get-BditTraceFilterDigest $inputValues.sender $inputValues.recipient $inputValues.internetMessageId $inputValues.messageTraceId $inputValues.includeSubject $inputValues.maximumRows
if($actual -cne $Expected){throw ('Fixed trace tuple digest differed: '+$actual+' versus '+$Expected)}
'PASS trace tuple digest'
