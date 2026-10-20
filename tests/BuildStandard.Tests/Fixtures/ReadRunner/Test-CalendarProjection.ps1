param([string]$Wrapper,[string]$Case)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
$definition=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Read-BditBoundedRows'},$true)
if($null -eq $definition){throw 'Missing private projection'}
. ([scriptblock]::Create($definition.Extent.Text))
function Assert-BditGate {}
function Check([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
$user=if($Case -ceq 'label'){[pscustomobject]@{DisplayName='Synthetic ambiguous label';Private='excluded'}}else{[pscustomobject]@{UserType=$Case;DisplayName='Not an identity';Private='excluded'}}
$row=[pscustomobject]@{User=$user;AccessRights=@('Reviewer');SharingPermissionFlags=@();Private='excluded'}
$rows=Read-BditBoundedRows -Producer {$row} -Properties @('User','AccessRights','SharingPermissionFlags')
Check ($rows.Count -eq 1) 'Expected exactly one calendar edge'
Check ($rows[0].SharingPermissionFlags -is [array] -and $rows[0].SharingPermissionFlags.Count -eq 0) 'Empty returned sharing flags lost'
Check ($rows[0].AccessRights.Count -eq 1 -and $rows[0].AccessRights[0] -ceq 'Reviewer') 'Rights lost'
Check ($null -eq $rows[0].PSObject.Properties['Private']) 'Unregistered source property escaped'
if($Case -ceq 'label'){Check ($rows[0].User.Source -ceq 'UnresolvedDisplayLabel') 'Label became principal identity'}
else {Check ($rows[0].User.Source -ceq 'SpecialPrincipal' -and $rows[0].User.Value -ceq $Case) 'Typed Default/Anonymous principal lost'}
'PASS '+$Case
