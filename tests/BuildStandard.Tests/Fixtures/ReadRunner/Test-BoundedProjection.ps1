param([string]$Wrapper,[string]$Case)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Wrapper,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Wrapper parse failed'}
$definition=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Read-BditBoundedRows'},$true)
if($null -eq $definition){throw 'Missing private bounded projection'}
. ([scriptblock]::Create($definition.Extent.Text))
function Assert-BditGate { }
function Check([bool]$Value,[string]$Message){if(-not $Value){throw $Message}}
switch -CaseSensitive ($Case) {
 'values' {
  $rows=Read-BditBoundedRows -Producer { [pscustomobject]@{Id='opaque';Flag=$false;ReturnedNull=$null;Rights=@();Secret='not selected'} } -Properties @('Id','Flag','Missing','ReturnedNull','Rights')
  Check ($rows.Count -eq 1) 'Expected one bounded row'
  Check ($rows[0].Flag -is [bool] -and -not $rows[0].Flag) 'False was lost/coerced'
  Check ($null -ne $rows[0].PSObject.Properties['ReturnedNull'] -and $null -eq $rows[0].ReturnedNull) 'Returned null lost'
  Check ($null -eq $rows[0].PSObject.Properties['Missing']) 'Missing property invented'
  Check ($null -eq $rows[0].PSObject.Properties['Secret']) 'Unselected property escaped'
  Check ($null -ne $rows[0].Rights -and @($rows[0].Rights).Count -eq 0) 'Returned empty list became unknown'
 }
 'rows' {
  $script:Produced=0;$refused=$false
  try{$null=Read-BditBoundedRows -Producer {for($i=0;$i -lt 6000;$i++){$script:Produced++;[pscustomobject]@{Id=[string]$i}}} -Properties @('Id')}
  catch{if($_.Exception.Message -like '*BDIT-READ-LIMIT*'){$refused=$true}else{throw}}
  Check $refused 'Overflow was not refused';Check ($script:Produced -eq 5001) 'Producer enumerated beyond the sentinel row'
 }
 'field' {
  $refused=$false
  try{$null=Read-BditBoundedRows -Producer {[pscustomobject]@{Id=('x'*1025)}} -Properties @('Id')}catch{if($_.Exception.Message -like '*BDIT-READ-LIMIT*'){$refused=$true}else{throw}}
  Check $refused 'Oversized field was not refused'
 }
 'list' {
  $refused=$false
  try{$null=Read-BditBoundedRows -Producer {[pscustomobject]@{Values=@(0..100)}} -Properties @('Values')}catch{if($_.Exception.Message -like '*BDIT-READ-LIMIT*'){$refused=$true}else{throw}}
  Check $refused 'Oversized list was not refused'
 }
 'reference' {
  $rows=Read-BditBoundedRows -Producer {[pscustomobject]@{User=[pscustomobject]@{RawIdentity='opaque-principal';Secret='must not escape'}}} -Properties @('User')
  Check ($rows[0].User.Source -ceq 'RawIdentity' -and $rows[0].User.Value -ceq 'opaque-principal') 'Exact reference was not preserved'
  Check ($null -eq $rows[0].User.PSObject.Properties['Secret']) 'An arbitrary raw principal object was copied'
 }
 'budget' {
  $script:Produced=0;$refused=$false
  try{$null=Read-BditBoundedRows -Producer {for($i=0;$i -lt 400;$i++){$script:Produced++;[pscustomobject]@{Values=@(1..100|ForEach-Object {'x'*1024})}}} -Properties @('Values')}
  catch{if($_.Exception.Message -like '*BDIT-READ-LIMIT*'){$refused=$true}else{throw}}
  Check $refused 'Projected byte overflow was not refused';Check ($script:Produced -lt 400) 'Byte budget did not stop the producer'
 }
 'shape' {
  $refused=$false
  try{$null=Read-BditBoundedRows -Producer {[pscustomobject]@{User=[pscustomobject]@{Unexpected='not an approved field';Secret='must not be serialised'}}} -Properties @('User')}
  catch{if($_.Exception.Message -like '*BDIT-READ-SHAPE*'){$refused=$true}else{throw}}
  Check $refused 'An arbitrary complex source was serialised or resolved by display name'
 }
 'empty-identity' {
  function Assert-BditGate {throw 'BDIT-IDENTITY'}
  $refused=$false
  try{$null=Read-BditBoundedRows -Producer {} -Properties @('Id')}catch{if($_.Exception.Message -like '*BDIT-IDENTITY*'){$refused=$true}else{throw}}
  Check $refused 'An empty source concealed a final identity failure'
 }
 'distinguished-reference' {
  $rows=Read-BditBoundedRows -Producer {[pscustomobject]@{User=[pscustomobject]@{DistinguishedName='CN=Exact,OU=Recipients,DC=synthetic,DC=example';Secret='must not escape'}}} -Properties @('User')
  Check ($rows[0].User.Source -ceq 'DistinguishedName' -and $rows[0].User.Value -ceq 'CN=Exact,OU=Recipients,DC=synthetic,DC=example') 'Original Exchange reference namespace/value lost'
  Check ($null -eq $rows[0].User.PSObject.Properties['Secret']) 'Arbitrary reference object escaped'
 }
 'special-reference' {
  $rows=Read-BditBoundedRows -Producer {[pscustomobject]@{User=[pscustomobject]@{UserType='Default';DisplayName='Not used as identity'}}} -Properties @('User')
  Check ($rows[0].User.Source -ceq 'SpecialPrincipal' -and $rows[0].User.Value -ceq 'Default') 'Special principal was guessed from display name or lost'
 }
 'unresolved-reference' {
  $rows=Read-BditBoundedRows -Producer {[pscustomobject]@{User=[pscustomobject]@{DisplayName='same display label';Secret='must not escape'}}} -Properties @('User')
  Check ($rows[0].User.Source -ceq 'UnresolvedDisplayLabel' -and $rows[0].User.Value -ceq 'same display label') 'An unresolved label became a service identity'
  Check ($null -eq $rows[0].User.PSObject.Properties['Secret']) 'Arbitrary principal object escaped'
 }
 'identity' {
  $script:GateCalls=0
  function Assert-BditGate {$script:GateCalls++;if($script:GateCalls -gt 1){throw 'BDIT-IDENTITY'}}
  $refused=$false;$result=$null
  try{$result=Read-BditBoundedRows -Producer {[pscustomobject]@{Id='one'};[pscustomobject]@{Id='two'}} -Properties @('Id')}catch{if($_.Exception.Message -like '*BDIT-IDENTITY*'){$refused=$true}else{throw}}
  Check $refused 'Identity change was not refused';Check ($null -eq $result) 'Partial output escaped identity failure'
 }
 default {throw 'Unknown synthetic case'}
}
Write-Output ('PASS '+$Case)
