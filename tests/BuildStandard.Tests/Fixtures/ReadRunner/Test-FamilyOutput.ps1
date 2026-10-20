param([string]$Wrapper,[string]$Case)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
foreach($name in @('Require-BditOutputText','Require-BditOutputMap','Convert-BditInboxRuleCell','Convert-BditPermissionCell')) {
 $definition=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true)
 if($null -eq $definition){throw ('Missing fixed family output projection: '+$name)}
 . ([scriptblock]::Create($definition.Extent.Text))
}
function Check([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
function Refused([string]$Section,[string]$Column,$Value){
 $failed=$false;try{$null=Convert-BditInboxRuleCell -Section $Section -Column $Column -Value $Value}catch{$failed=$true}
 Check $failed 'An invalid family output value was admitted'
}
function Observed($Value){return [ordered]@{state='Value';value=$Value;reason=$null}}
function Reference([string]$Value){return [ordered]@{namespace='RawIdentity';value=$Value;externalDirectoryObjectId=$null}}
switch -CaseSensitive ($Case) {
 'permission-principal' {$v=Convert-BditPermissionCell permissions principal (Reference 'S-1-5-21-123');Check ($v.namespace -ceq 'RawIdentity' -and $v.value -ceq 'S-1-5-21-123' -and $null -eq $v.externalDirectoryObjectId) 'Original principal was resolved/dropped'}
 'permission-rights' {$v=Convert-BditPermissionCell permissions rights (Observed @('FullAccess','ReadPermission'));Check ($v.value.Count -eq 2 -and $v.value[0] -ceq 'FullAccess') 'Original rights list was reordered/joined'}
 'permission-false' {$v=Convert-BditPermissionCell permissions deny (Observed $false);Check ($v.value -is [bool] -and -not $v.value) 'False deny was changed'}
 'permission-forged-flag' {$failed=$false;try{$null=Convert-BditPermissionCell permissions deny (Observed 'false')}catch{$failed=$true};Check $failed 'String deny became false'}
 'permission-forged-rights' {$failed=$false;try{$null=Convert-BditPermissionCell permissions rights (Observed 'FullAccess')}catch{$failed=$true};Check $failed 'Scalar rights became a list'}
 'permission-rights-overflow' {$failed=$false;try{$null=Convert-BditPermissionCell permissions rights (Observed @(0..100|ForEach-Object {'FullAccess'}))}catch{$failed=$true};Check $failed 'Unbounded rights list was admitted'}
 'permission-unknown-with-value' {$failed=$false;try{$null=Convert-BditPermissionCell permissions rights ([ordered]@{state='Missing';value=@();reason='Unknown'})}catch{$failed=$true};Check $failed 'Unknown rights became empty'}
 'permission-forged-principal' {$v=Reference 'raw';$v.namespace='EntraNameJoin';$failed=$false;try{$null=Convert-BditPermissionCell permissions principal $v}catch{$failed=$true};Check $failed 'Invented principal namespace admitted'}
 'permission-unknown-column' {$failed=$false;try{$null=Convert-BditPermissionCell permissions messageBody 'content'}catch{$failed=$true};Check $failed 'Unregistered field admitted'}
 'false' {$v=Convert-BditInboxRuleCell rules enabled (Observed $false);Check ($v.value -is [bool] -and -not $v.value) 'False was coerced'}
 'missing' {$v=Convert-BditInboxRuleCell rules enabled ([ordered]@{state='Missing';value=$null;reason="Not returned`nSynthetic reason"});Check ($v.state -ceq 'Missing' -and $null -eq $v.value -and $v.reason.Contains("`n")) 'Unknown state/reason was changed'}
 'empty' {$v=Convert-BditInboxRuleCell rules forwardTo (Observed @());Check ($null -ne $v.value -and @($v.value).Count -eq 0) 'Empty list was lost'}
 'reference' {$v=Convert-BditInboxRuleCell rules forwardTo (Observed @((Reference 'opaque source')));Check ($v.value[0].namespace -ceq 'RawIdentity' -and $v.value[0].value -ceq 'opaque source') 'Raw reference was changed'}
 'counts' {$v=Convert-BditInboxRuleCell target-reads returnedEntries 0;Check ($v -is [int] -and $v -eq 0) 'Reported zero was changed';$v=Convert-BditInboxRuleCell target-reads excludedEntries $null;Check ($null -eq $v) 'Unknown count became zero'}
 'blank-name' {$v=Convert-BditInboxRuleCell rules name '';Check ($v -is [string] -and $v.Length -eq 0) 'Returned blank name became unknown'}
 'forged-boolean' {Refused rules enabled (Observed 'false')}
 'unknown-with-value' {Refused rules enabled ([ordered]@{state='Missing';value=$false;reason='Missing'})}
 'reasonless' {Refused rules enabled ([ordered]@{state='Missing';value=$null;reason=$null})}
 'unknown-property' {$v=Observed $false;$v['secret']='unregistered';Refused rules enabled $v}
 'list-limit' {Refused rules forwardTo (Observed @(0..100|ForEach-Object {Reference 'raw'}))}
 'reference-limit' {Refused rules forwardTo (Observed @((Reference ('x'*1025))))}
 'bad-reference' {$v=Reference 'raw';$v.namespace='InventedDirectoryIdentity';Refused rules forwardTo (Observed @($v))}
 'forged-id' {Refused rules id 'sha256:forged'}
 'unknown-column' {Refused rules messageContents 'not admitted'}
 'count-overflow' {Refused target-reads returnedEntries 5001}
 'string-overflow' {Refused rules ruleIdentity ('x'*1025)}
 'unknown-state' {Refused rules enabled ([ordered]@{state='NotSet';value=$null;reason='Not returned'})}
 'control-identity' {Refused rules ruleIdentity "source`nidentity"}
 'priority-type' {Refused rules priority (Observed 0.1)}
 'scalar-flag' {Refused rules enabled $false}
 'read-state' {Refused target-reads childReadStatus 'NotSet'}
}
'PASS '+$Case
