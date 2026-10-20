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
$row=[pscustomobject]@{Guid=[guid]'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';ExternalDirectoryObjectId=$null;ManagedBy=@([pscustomobject]@{RawIdentity='original-owner';Private='excluded'});RequireSenderAuthenticationEnabled=$false;Private='excluded'}
if($Case -ceq 'unknown-owner'){$row.ManagedBy=@([pscustomobject]@{DisplayName='Unresolved owner label';Private='excluded'})}
$rows=Read-BditBoundedRows -Producer {$row} -Properties @('Guid','ExternalDirectoryObjectId','ManagedBy','RequireSenderAuthenticationEnabled')
Check ($rows.Count -eq 1 -and $rows[0].Guid -is [guid]) 'Actual Exchange recipient GUID was coerced/replaced'
Check ($null -ne $rows[0].PSObject.Properties['ExternalDirectoryObjectId'] -and $null -eq $rows[0].ExternalDirectoryObjectId) 'Returned null directory ID was replaced'
Check ($rows[0].RequireSenderAuthenticationEnabled -is [bool] -and -not $rows[0].RequireSenderAuthenticationEnabled) 'Reported false was changed'
$expected=if($Case -ceq 'unknown-owner'){'UnresolvedDisplayLabel'}else{'RawIdentity'}
Check ($rows[0].ManagedBy.Count -eq 1 -and $rows[0].ManagedBy[0].Source -ceq $expected) 'ManagedBy lost its source namespace'
Check ($null -eq $rows[0].PSObject.Properties['Private']) 'Unregistered data escaped'
'PASS '+$Case
