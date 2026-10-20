param([string]$Wrapper,[string]$Case)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
$definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Read-BditBoundedRows'},$true)
if($null -eq $definition){throw 'Missing private bounded projection'}
. ([scriptblock]::Create($definition.Extent.Text))
function Assert-BditGate {}
function Check([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
$user=if($Case -ceq 'self'){'NT AUTHORITY\SELF'}elseif($Case -ceq 'label'){[pscustomobject]@{DisplayName='Synthetic unresolved label';Private='excluded'}}else{[pscustomobject]@{RawIdentity='S-1-5-21-123-456-789-1001';DisplayName='Synthetic ambiguous name';Private='excluded'}}
$row=[pscustomobject]@{User=$user;AccessRights=@('FullAccess');IsInherited=$false;Deny=$null;Private='excluded'}
if($Case -ceq 'missing'){$row.PSObject.Properties.Remove('IsInherited')}
$rows=Read-BditBoundedRows -Producer {$row} -Properties @('User','AccessRights','IsInherited','Deny')
Check ($rows.Count -eq 1) 'Expected exact one original edge'
$result=$rows[0]
Check ($null -eq $result.PSObject.Properties['Private']) 'Unregistered field escaped'
Check ($null -ne $result.PSObject.Properties['Deny'] -and $null -eq $result.Deny) 'Returned null became missing/false'
Check ($result.AccessRights -is [array] -and $result.AccessRights.Count -eq 1 -and $result.AccessRights[0] -ceq 'FullAccess') 'Returned rights were joined or changed'
if($Case -ceq 'missing'){Check ($null -eq $result.PSObject.Properties['IsInherited']) 'Missing inheritance was invented'}else{Check ($result.IsInherited -is [bool] -and -not $result.IsInherited) 'False inheritance was lost'}
if($Case -ceq 'self'){
 Check ($result.User -is [string] -and $result.User -ceq 'NT AUTHORITY\SELF') 'SELF was resolved or replaced'
}elseif($Case -ceq 'label'){
 Check ($result.User.Source -ceq 'UnresolvedDisplayLabel' -and $result.User.Value -ceq 'Synthetic unresolved label') 'A label became exact principal identity'
}else{
 Check ($result.User.Source -ceq 'RawIdentity' -and $result.User.Value -ceq 'S-1-5-21-123-456-789-1001') 'Original SID/raw identity was dropped or name-resolved'
}
'PASS '+$Case
