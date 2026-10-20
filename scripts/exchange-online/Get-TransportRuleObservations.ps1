# INT-128/132: fixed metadata only. No rule predicates/actions/content or changes.
param([string]$RuleGuids = '', [bool]$OrganisationInventory = $false)
Set-StrictMode -Version Latest
function Unknown([string]$State,[string]$Reason) { return @{state=$State;value=$null;reason=$Reason} }
function TextOf($Item,[string]$Name) {
    $p=$Item.PSObject.Properties[$Name]
    if($null -eq $p -or $p.Value -isnot [string] -or $p.Value.Length -gt 1024 -or $p.Value -match '\p{Cc}'){return $null}
    return $p.Value
}
function GuidOf($Item) {
    $p=$Item.PSObject.Properties['Guid'];if($null -eq $p -or ($p.Value -isnot [string] -and $p.Value -isnot [guid])){return $null}
    try{$g=[guid]$p.Value;if($g -eq [guid]::Empty -or ([string]$p.Value).Length -ne 36){return $null};return $g.ToString('D')}catch{return $null}
}
function Setting($Item,[string]$Name) {
    $p=$Item.PSObject.Properties[$Name]
    if($null -eq $p){return (Unknown 'Missing' ($Name+' was not returned.'))}
    if($null -eq $p.Value){return (Unknown 'ReturnedNull' ($Name+' returned null.'))}
    if($Name -ceq 'Priority') {
        if(($p.Value -is [int] -or $p.Value -is [long]) -and $p.Value -ge 0 -and $p.Value -le 2147483647){return @{state='Value';value=$p.Value;reason=$null}}
    } elseif($Name -ceq 'ActivationDate' -or $Name -ceq 'ExpiryDate') {
        if($p.Value -is [datetime]){return @{state='Value';value=$p.Value.ToString('o');reason=$null}}
        if($p.Value -is [string]){return @{state='Value';value=$p.Value;reason=$null}}
    } elseif($p.Value -is [string] -and -not [string]::IsNullOrWhiteSpace($p.Value)) {return @{state='Value';value=$p.Value;reason=$null}}
    return (Unknown 'Unsupported' ($Name+' returned an unsupported value.'))
}
$targets=[Collections.Generic.List[object]]::new();$policies=[Collections.Generic.List[object]]::new();$seen=@{}
if($OrganisationInventory){$targets.Add([ordered]@{id=$null;name='Organisation inventory';readStatus='NotAttempted';error='Read not attempted.';scope='OrganisationInventory';requestedRuleGuid=$null;requestedResultSize=$null;returnedEntries=$null;excludedEntries=$null})}
else{foreach($id in $RuleGuids.Split(',')){$targets.Add([ordered]@{id=$null;name=$null;readStatus='NotAttempted';error='Read not attempted.';scope='SelectedRule';requestedRuleGuid=$id;requestedResultSize=$null;returnedEntries=$null;excludedEntries=$null})}}
foreach($target in $targets) {
    $batch=[Collections.Generic.List[object]]::new();$batchIds=@{}
    try {
        $sourceParameters=@{};if($target.scope -ceq 'SelectedRule'){$sourceParameters['Identity']=$target.requestedRuleGuid}
        $read=Use-BditRead -Command 'Get-TransportRule' -Parameters $sourceParameters
        $source=@($read.Rows);$target.requestedResultSize=$read.ResultSize
        if($target.scope -ceq 'SelectedRule' -and ($source.Count -ne 1 -or (GuidOf $source[0]) -cne $target.requestedRuleGuid)){throw 'Exact selected rule did not return one matching GUID'}
        $target.readStatus='Collected';$target.error=$null;$target.returnedEntries=$source.Count;$target.excludedEntries=0
        if($source.Count -ge $read.ResultSize){$target.readStatus='Partial';$target.error='The bounded source cap was reached; inventory completeness is unknown.'}
        foreach($item in $source) {
            $guid=GuidOf $item
            if($null -eq $guid){$target.excludedEntries++;$target.readStatus='Partial';$target.error='A source rule had no admissible original GUID; excluded, never assigned an invented ID.';continue}
            if($seen.ContainsKey($guid) -or $batchIds.ContainsKey($guid)){throw 'Duplicate rule GUID'}
            $batchIds[$guid]=$true
            $row=[ordered]@{id=$null;name=(TextOf $item 'Name');readStatus='Collected';error=$null;ruleGuid=$guid;
                priority=(Setting $item 'Priority');state=(Setting $item 'State');mode=(Setting $item 'Mode');activationDate=(Setting $item 'ActivationDate');expiryDate=(Setting $item 'ExpiryDate')}
            foreach($field in @('priority','state','mode','activationDate','expiryDate')){if($row[$field].state -cne 'Value'){$row.readStatus='Partial';$row.error='Some original rule metadata was not returned or could not be checked.'}}
            if($row.readStatus -cne 'Collected'){$target.readStatus='Partial';$target.error='Rule metadata contains unknown values; no effective policy claim.'}
            $batch.Add($row)
        }
        foreach($row in $batch){$policies.Add($row);$seen[$row.ruleGuid]=$true}
    } catch {$target.readStatus='Failed';$target.error='The bounded exact rule read failed, stopped or returned ambiguous identities. No successful empty claim.';$target.returnedEntries=$null;$target.excludedEntries=$null}
}
$status='Collected';$reason=$null
foreach($target in $targets){if($target.readStatus -cne 'Collected'){$status='Partial';$reason='Transport metadata is incomplete. Inspect read diagnostics, caps and unknown values; this is not effective mail-flow/security coverage.'}}
@{Sections=@(@{Id='target-reads';Status=$status;Error=$reason;Rows=$targets.ToArray()},@{Id='policies';Status=$status;Error=$reason;Rows=$policies.ToArray()})}
